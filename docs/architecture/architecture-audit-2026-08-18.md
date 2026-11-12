# Wails.Net 当前架构策略审计

审计基线：2026-08-18 当前工作树。  
审计方式：只读静态分析，未修改文件、未执行构建或测试。  
说明：AGENTS.md 引用的 `.trae/documents/wails-net-dotnet10-implementation-plan.md` 当前不存在，因此以代码和现有架构文档为事实基线。

## 1. 总体结论

三条架构主线选择正确，不建议改变：

- Host/DI/Config/Logging 继续采用 Microsoft.Extensions.*。
- Runtime/Window/IPC 保持 Wails v3 的对象模型。
- Plugin/Security/Capability 继续采用默认拒绝（deny-by-default）思路。

当前主要问题不是缺少抽象，而是“控制面已经建立，执行面仍有分叉”：

| 主线 | 评价 | 主要问题 |
|---|---|---|
| Host/DI/Config/Logging | B | Generic Host 已接入，但不是唯一对象图和唯一生命周期控制面 |
| Runtime/Window/IPC | B- | 多窗口、事件、取消机制较完整，但状态和传输管线存在多份实现 |
| Plugin/Security/Capability | C+ | Capability 模型已存在，但大量调用没有真正进入 ACL |

下一阶段应以“收敛和安全闭环”为主，暂缓继续增加新的插件、传输和管理器抽象。

## 2. 已经做对的部分

1. `DesktopApplicationBuilder` 正确封装 `HostApplicationBuilder`，向外暴露 Services、Configuration、Logging：

   [DesktopApplicationBuilder.cs](../../src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs:47)

2. `DesktopHostOptions`、`PermissionOptions`、`IpcOptions` 均已绑定 Options、Validator 和 `ValidateOnStart`。

3. 插件初始化已经移除临时 `BuildServiceProvider`，通过 `DeferredPermissionRegistrar` 延迟物化权限，避免双 Singleton：

   [DesktopApplicationBuilder.cs](../../src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs:436)

4. 生产绑定使用源生成强类型调用器，没有回退到运行时反射。

5. `WebviewWindow.Impl` 断链问题已经修复，两条窗口创建路径均会回填平台实现：

   [Application.cs](../../src/Wails.Net.Application/Application.cs:919)  
   [WindowManager.cs](../../src/Wails.Net.Application/Managers/WindowManager.cs:106)

6. Strict 模式下 Capability 加载条件已改为与 `PermissionManager.Enabled` 一致：

   [DesktopApplicationBuilder.cs](../../src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs:379)

7. 事件系统已具备通配符、发送窗口、目标窗口和应用事件钩子；IPC 已具备调用取消和分块大小限制。

这些能力说明融合架构的基础已经形成，后续不需要推倒重来。

## 3. P0：必须优先处理的问题

### 3.1 Host 和 Application 存在两套核心对象图

`Application` 自己创建 `BindingManager` 和 `EventProcessor`：

[Application.cs](../../src/Wails.Net.Application/Application.cs:48)

DI 又注册另一套 Singleton：

[ServiceCollectionExtensions.cs](../../src/Wails.Net.Application/Hosting/ServiceCollectionExtensions.cs:31)

Server Mode 的 `MessageProcessor` 使用 DI 实例，而 `Application.RegisterService/RegisterBindings` 写入 Application 自有实例。这会导致不同传输看到不同的绑定、事件和服务状态。

建议：

- 让 `Application` 构造注入 `BindingManager`、`EventProcessor`、`WindowManager` 等实例。
- 删除 `InitializeFromServiceProvider` 服务定位器式组装。
- Host/DI 成为唯一对象图，`Application` 仅作为 facade/coordinator。

### 3.2 所有调用尚未进入统一授权入口

当前存在三个安全断点：

1. Strict 只检查“声明了 required capability”的命令；没有安全声明的命令仍可执行：

   [CommandDispatcher.cs](../../src/Wails.Net.Application/Commands/CommandDispatcher.cs:136)

2. `[Binding]` 调用先直接进入 `Bindings.Call`，绕过 `CommandDispatcher` 和 `PermissionManager`：

   [MessageProcessor.cs](../../src/Wails.Net.Application/Transport/MessageProcessor.cs:327)

3. `window.*` 命令找不到插件命令时，会进入无权限检查的硬编码 fallback：

   [MessageProcessor.cs](../../src/Wails.Net.Application/Transport/MessageProcessor.cs:818)

静态扫描显示，47 个插件约 304 个命令注册点中，只有约 29 个接入 `CommandSecurity`。Process、Stronghold、Store、Updater、Window 等高敏插件仍存在大量匿名命令。

建议建立唯一的 `InvocationGateway`：

```text
Transport
  → 可信 InvocationContext
  → 命令查找
  → Capability/Scope 授权
  → Invocation Scope
  → Binding 或 Plugin Command 调用器
```

Strict 终态必须满足：

- 每个命令显式 `Require(...)` 或 `Public()`。
- `Security == null` 在 Strict 下直接拒绝。
- Binding、插件命令、核心窗口操作使用同一个授权服务。
- manifest 未覆盖命令导致构建失败。

### 3.3 WindowId 和 Origin 不是可信上下文

原生 IPC 仅在消息没有 WindowId 时写入真实窗口 ID，前端可主动携带另一个窗口 ID：

[NativeIpcTransport.cs](../../src/Wails.Net.Application/Transport/NativeIpcTransport.cs:118)

Origin 也来自前端 JSON，而不是平台观测结果。低权限窗口因此可能冒充高权限窗口，remote capability 也无法建立可信边界。

此外，本地来源使用字符串前缀判断：

[IpcOriginValidator.cs](../../src/Wails.Net.Application/Security/IpcOriginValidator.cs:64)

这会错误接受 `http://localhost.evil.example`。

建议：

- 传输层无条件覆盖 WindowId、WindowName、Origin、TopOrigin、IsMainFrame。
- 使用 WebView2 `args.Source`、WKWebView `FrameInfo` 等平台信息。
- 使用 `Uri.TryCreate` 后精确比较 Scheme、Host 和端口。
- null Origin 只有在原生传输能证明来源可信时才视为本地。
- Capability 未声明 remote 时，远程来源默认拒绝，而不是无限制放行。

### 3.4 窗口存在多套注册表

当前至少存在：

- `Application._windows`
- `WindowManager._windows`
- 各平台 `_windows`
- `NativeIpcTransport._windowImpls`

[Application.cs](../../src/Wails.Net.Application/Application.cs:32)  
[WindowManager.cs](../../src/Wails.Net.Application/Managers/WindowManager.cs:18)

`Application.CreateWebviewWindow()` 不登记到 `WindowManager`，但 `EventIPCTransport` 从 `WindowManager` 枚举窗口，因此非 Native IPC 平台可能找不到需要投递事件的窗口。

建议：

- `WindowManager` 成为唯一逻辑窗口注册表和 ID 分配器。
- 平台层只保存 native handle/impl。
- Native IPC 注册表仅作为传输连接表，不承担窗口事实源职责。
- Application 的窗口 API全部委托给 `IWindowManager`。

### 3.5 Server Mode transport 启动顺序错误

`DesktopHostedService` 先注册并启动；`ServerModeTransportInitializer` 后注册。Application 进入 Run 时 transport 尚未注入，后续 initializer 只赋值、不调用 `StartAsync`：

[ServerModeTransportExtensions.cs](../../src/Wails.Net.Application/Hosting/ServerModeTransportExtensions.cs:205)

结果可能是 Server Mode 拥有一个“已注入但未监听”的 WebSocketTransport。

建议在 Host 启动前完成 transport 装配，或将 transport 生命周期并入唯一 Runtime HostedService，避免靠后置 HostedService 修改启动输入。

### 3.6 PlatformStarted 和关闭语义不可靠

`PlatformStarted` 在调用 `_platformApp.Run()` 前置位；如果平台主循环立即失败，TCS 已成功完成，异常随后又被捕获：

[Application.cs](../../src/Wails.Net.Application/Application.cs:1085)

Host 可能报告启动成功，但 UI 主循环实际已经失败。

关闭时，Host 线程直接执行 `Application.Shutdown()`。窗口关闭、平台 Destroy、`PostQuitMessage` 等操作未统一 marshal 到 UI 线程。

建议：

- 平台接口增加真实 `Ready` 回调，由消息循环/GTK MainLoop/Activity bridge 报告。
- 平台运行异常传播到 Host。
- 增加 `ShutdownAsync(CancellationToken)`。
- 窗口销毁和退出消息统一通过平台 dispatcher 投递到 UI 线程。
- Host 的 `ShutdownTimeout` 贯穿 transport、插件、服务和 UI 线程退出。

### 3.7 IPC 缺少有序单写者和控制通道

WebSocket 接收循环串行等待长命令，同连接后续的 cancel 消息无法及时读取。事件广播和 RPC 响应又可能并发写入同一个 WebSocket。

建议：

- 每个连接/窗口建立一个有界 mailbox。
- 单写者负责发送，保证事件和响应顺序。
- call 使用受控并发任务。
- cancel、ping、close 走独立控制通道。
- 定义队列满时的丢弃、拒绝或断连策略，并输出指标。

### 3.8 Stronghold 存在独立的密码学阻断问题

Stronghold 创建金库时生成一次 AES-GCM nonce，后续多次保存继续复用同一 key/nonce：

[StrongholdPlugin.cs](../../src/Wails.Net.Plugins.Stronghold/StrongholdPlugin.cs:254)  
[StrongholdPlugin.cs](../../src/Wails.Net.Plugins.Stronghold/StrongholdPlugin.cs:381)

AES-GCM 在同一密钥下禁止复用 nonce。应每次加密生成新 nonce，并与本次密文一同持久化；文件写入还应采用临时文件加原子替换。该问题应独立于架构重构立即修复。

## 4. P1：下一批结构优化

### 4.1 统一四平台 WebView 管线

Windows、Linux、macOS、Android 分别复制了：

- IPC body 读取和响应序列化
- AssetServer 与 SPA fallback
- runtime 注入
- RuntimeReady 派发
- CSP/nonce 处理

多个平台还在 UI 回调中对异步用户命令执行 `.GetAwaiter().GetResult()`，存在卡 UI和死锁风险。

建议抽取：

- `WebviewRequestPipeline`
- `AssetResponse`
- `RuntimeInjectionPlan`
- `WebviewSecurityPolicy`

平台实现只保留 WebView 创建、原生回调和 dispatcher。

### 4.2 统一生命周期所有权

插件目前同时由 `PluginHostedServiceAdapter` 和 `Application.Run/Shutdown` 管理，依赖 Interlocked 让其中一路成为 no-op。

自定义 `ServiceRegistry` 与 DI 注册的生命周期服务也没有自动同步。

建议：

- Host 成为唯一生命周期控制面。
- 插件、transport、AssetServer、Wails 服务统一适配到一个明确的 Runtime HostedService。
- 启动失败时记录已启动组件并逆序回滚。
- 插件启动失败默认中止启动；可选插件必须显式标记。

### 4.3 让 IpcOptions 成为真实配置

以下字段已定义并验证，但实现没有消费：

- `AllowHttpFallback`
- `AllowWebSocketFallback`
- `UseRandomLoopbackPort`
- `PortStart`
- `PortEnd`

[IpcOptions.cs](../../src/Wails.Net.Application/Security/IpcOptions.cs:17)

应建立 Options→行为的契约测试；无法实现的字段应删除，避免形成配置已生效的错觉。

### 4.4 为每次命令调用建立 DI Scope

`CommandDispatcher` 当前持有根容器，`ICommandContext.Services` 也指向根容器。

建议每次 IPC 调用创建 `IAsyncServiceScope`，用于：

- scoped 数据库连接
- 请求级事务
- Correlation ID
- 调用级审计上下文
- 异步资源释放

### 4.5 完善插件描述符和权限清单

建议引入不可变 `PluginDescriptor`：

```text
Id / Version / Platforms / Dependencies
Commands / Permissions / DefaultPermissionSet
FrontendAssets / InitScript
RequiredServices
```

并要求：

- 重复插件名、命令名构建失败。
- NuGet 包携带权限清单。
- CLI 自动发现引用插件的权限。
- 生成 Capability JSON Schema，支持 IDE 补全。
- manifest 的 Commands、PermissionSets 不再为空占位。

### 4.6 Channel 暂不应视为完整能力

当前 Channel 主要是后端静态原型：

- 没有 `channel:open/message/close` IPC 路由。
- 前端没有对等 API。
- 默认 sender 为 no-op。
- 静态注册表影响多 Host 隔离。
- 尚未定义背压和窗口关闭行为。

应先完成协议、DI 生命周期、窗口关联和背压，再考虑加密。

## 5. P2：质量和可维护性优化

1. `AddWails/AddWailsCore` 改用 `TryAddSingleton/TryAddEnumerable`，确保重复调用幂等。

2. Options 热重载语义需要诚实化。目前 `IOptionsMonitor.CurrentValue` 主要在构建期读取，`ApplicationOptions` 实际是启动快照。启动期配置应使用 `IOptions<T>`，真正支持热更新的配置才使用 `IOptionsMonitor.OnChange`。

3. 日志桥接应实现 `ISupportExternalScope`，保留结构化 state、EventId、Exception 和 Scope，而不是拼接成字符串：

   [LogServiceLoggerProvider.cs](../../src/Wails.Net.Application/Logging/LogServiceLoggerProvider.cs:85)

4. `LogService` 的内存日志列表应设容量上限，避免长期运行持续增长。

5. 全局异常事件应保存 delegate 并在 Dispose 时解除订阅。

6. 模板应返回 `RunAsync()` 的退出码，否则启动失败可能仍以进程码 0 结束。

7. `FileAssetProvider` 路径校验不应只使用字符串 `StartsWith`，需验证目录边界并处理 junction/symlink。

8. Strict 模式下 Capability 文件错误应 fail-fast，而不是记录 warning 后继续以部分授权状态运行。

## 6. 推荐演进顺序

### R0：安全和正确性止血

- 修复 Stronghold nonce。
- 修复 localhost Origin 绕过。
- Native IPC 强制覆盖 WindowId/Origin。
- Strict 拒绝无安全声明的命令。
- 禁止 `window.*` 无权限 fallback。
- 修复 Server Mode transport 启动顺序。

### R1：统一运行时事实源

- Application 使用 DI 构造注入。
- Binding/Event/Window 注册表唯一化。
- 移除 `Application.Get()` 在平台和插件中的新增使用。
- 建立真实平台 Ready 和 UI 线程 Shutdown。

### R2：统一调用与传输管线

- 建立 InvocationGateway。
- Binding 与 Plugin Command 共用授权、Scope、DI Scope和错误模型。
- WebSocket mailbox、控制通道和背压。
- 四平台 WebviewRequestPipeline 异步化。

### R3：完成自研 ACL 能力模型

- 全量迁移插件到 `Require/Public`。
- 权限清单随 NuGet 包分发。
- Capability Schema 和构建期校验。
- Scope 与 capability/window/origin grant 绑定。
- 完成后再将库默认从 Compatibility 翻转为 Strict。

### R4：清理与生态闭环

- PluginDescriptor 和完整 manifest。
- Channel 协议。
- Asset protocol scope。
- Options 单一来源。
- 结构化日志和可观测性。

## 7. 核心验收标准

- Host、Application、Server Transport 解析出的 BindingManager/EventProcessor 引用完全相同。
- 全仓库每个命令显式标记 `Require` 或 `Public`。
- `[Binding]` 和插件命令均经过同一 ACL。
- 低权限窗口无法伪造高权限 WindowId。
- `localhost.evil.example` 被拒绝。
- 未声明 remote 的 capability 拒绝远程页面。
- 单连接长调用期间 cancel 可立即生效。
- 同一 WebSocket 不发生并发 SendAsync。
- Host Stop 后 UI 线程在 shutdown timeout 内退出。
- 所有 `IpcOptions` 字段都有行为测试，或从公共 API 删除。
- 全仓库只保留一个逻辑窗口注册表。
- 四平台 Asset/CSP/nonce/runtime-ready 使用同一共享管线。

## 8. 文档治理

现有 [refactor-and-hardening-plan.md](../../docs/architecture/refactor-and-hardening-plan.md:140) 已出现状态漂移：文档仍将 WebviewWindow Impl 断链和 Strict Capability 加载列为未修复，但当前代码已经完成修正。

后续建议所有架构问题统一维护状态：

```text
Proposed → Accepted → In Progress → Implemented → Verified
```

并要求每个 Implemented 条目附对应测试。否则架构文档容易继续把“已修复问题”和“仍存在问题”混在一起。
