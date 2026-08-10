# Wails.Net 架构优化修改文档

> 本文档面向后续架构演进和实施排期，基于当前仓库静态代码结构梳理可优化内容，并将优化项拆解为可落地的修改任务。
>
> - 分析日期：2026-08-10
> - 适用范围：`src/`、`tests/`、`docs/`、`examples/` 中与 Wails.Net 运行时、宿主、插件、安全、IPC 和测试治理相关的代码与文档
> - 融合策略：
>   - **Host/DI/Config/Logging** → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
>   - **Runtime/Window/IPC** → 学 Wails v3（对象模型、IPC、多窗口、事件总线）
>   - **Plugin/Security/Capability** → 学 Tauri v2（插件能力、权限模型、安全设计）

---

## 1. 优化目标

### 1.1 总体目标

Wails.Net 当前已经具备完整的 .NET Host 宿主、平台抽象、窗口管理、IPC、事件总线、插件体系和权限模型。下一阶段优化不应以推翻现有架构为目标，而应围绕以下方向进行增量演进：

1. **让 Host 层更 ASP.NET Core 化**：配置入口更清晰、Options 校验更完整、生命周期语义更可靠、日志与诊断更标准。
2. **让 Runtime/Window/IPC 更 Wails v3 化**：保留 `Application` facade 的易用性，但将运行时职责进一步模块化，统一多传输层策略和窗口生命周期事件。
3. **让 Plugin/Security/Capability 更 Tauri v2 化**：默认安全姿态向最小权限迁移，补齐平台过滤、前端资产承载、权限清单生成和 Scope 治理。
4. **让文档、测试、模板和实现保持同步**：避免架构主文档落后于代码，避免模板生成的应用默认走不安全或过时配置。

### 1.2 非目标

以下内容不作为本文档优化项：

- 不改变 .NET 10 / Microsoft.Extensions.* / TUnit / System.CommandLine / GirCore / CsWin32 等已确定技术选型。
- 不引入 MAUI Controls、Avalonia、GtkSharp、Xamarin.Forms、PInvoke.* 或其他 AGENTS.md 明确禁止的依赖。
- 不恢复生产代码运行时反射绑定或 `MethodInfo.Invoke` 调用路径。
- 不在当前阶段实现 iOS。
- 不将 Server 模式替代为完整 Web 后端框架；Server 模式仍作为无 GUI 降级和测试友好运行时。

---

## 2. 当前架构概览

### 2.1 Host / DI / Config / Logging

当前宿主层以 `DesktopApplicationBuilder` 为入口，内部封装 `HostApplicationBuilder`，并通过 `ServiceCollectionExtensions` 注册核心服务、管理器、权限管理器和插件管理器。`DesktopHostedService` 将阻塞式 UI 主循环适配到 `IHostedService` 生命周期，`Application` 则通过 `InitializeFromServiceProvider` 接收 DI 中的日志、生命周期、插件和管理器实例。

现有设计优点：

- 使用 `Microsoft.Extensions.Hosting` 统一应用生命周期。
- 使用 `Microsoft.Extensions.DependencyInjection` 统一服务注册。
- 使用 `Microsoft.Extensions.Options` 绑定 `DesktopHostOptions`、`PermissionOptions` 等强类型配置。
- 使用 `Microsoft.Extensions.Logging` 统一后端日志，并可与 Wails 日志服务桥接。
- 插件可以通过 `ConfigureServices(IServiceCollection)` 参与 DI 注册。

当前主要优化空间：

- 配置模型存在兼容字段与推荐字段并行，用户容易误配。
- Options 校验不足，配置错误可能延迟到运行时才暴露。
- `DesktopHostedService.StartAsync` 的 ready 语义不够精确。
- `Application` 仍承担过多协调与状态管理职责。

### 2.2 Runtime / Window / IPC

当前运行时层以 `Application` 为聚合根，对外保持 Wails v3 风格 API，对内聚合窗口管理、事件处理、绑定系统、命令调度、资源服务、平台实现和传输层。窗口管理已经下沉到 `WindowManager`，事件系统、绑定系统和命令调度也均有独立模块。

现有设计优点：

- `Application` facade 对 Wails 用户友好，迁移成本低。
- `WindowManager` / `DialogManager` / `ScreenManager` 等管理器结构清晰。
- IPC 传输层支持 Native IPC、HTTP、WebSocket、AssetServer、EventIPC fallback 等多种模式。
- 绑定和命令调用路径转向编译期生成或强类型委托，符合 NativeAOT 与禁反射要求。
- 多窗口、事件广播、sender window 传播和取消调用能力已具备。

当前主要优化空间：

- 多传输层选择和 fallback 逻辑较分散。
- HTTP/WebSocket fallback 的默认安全策略需要进一步收紧。
- Host 生命周期、窗口 ready、前端 runtime ready、IPC ready 四类状态需要更明确的事件模型。
- `Application` 中的 service route、自定义 scheme、shutdown hooks 等职责可继续拆分。

### 2.3 Plugin / Security / Capability

当前插件层以 `IPlugin`、`IPluginContext`、`PluginManager` 为核心。安全层包含 `Capability`、`PermissionSet`、`PermissionOptions`、`PermissionManager`、`Scope`、`IpcOriginValidator`、`UrlWhitelist` 等模型。命令调度前由 `CommandDispatcher` 执行权限、Scope 和来源校验。

现有设计优点：

- 插件可注册服务、命令、权限和生命周期逻辑。
- 权限系统支持 grant、deny、PermissionSet 展开、窗口级限制和 remote URL 限制。
- Scope 参数校验已经进入命令调度路径。
- 插件包拆分和插件文档已有基础。

当前主要优化空间：

- `PermissionOptions.Enabled` 默认关闭，安全模型尚未达到 Tauri v2 默认最小权限姿态。
- `Capability.platforms` 尚未落地。
- 插件前端资产、脚本、类型声明和后端命令尚未形成完整可安装插件闭环。
- 插件命令、权限和前端 API 之间缺少统一生成的清单。

---

## 3. 优化路线图总览

| 优先级 | 主题 | 核心收益 | 主要影响范围 |
|---|---|---|---|
| P0 | 刷新架构文档基线 | 避免决策依据过期 | `docs/` |
| P0 | 收敛配置模型 | 降低用户误配风险 | `Hosting`、模板、示例 |
| P0 | 增加 Options 校验 | 配置错误前置到启动期 | `Hosting`、`Security`、测试 |
| P0 | 收紧 IPC fallback 安全默认值 | 降低本地端口和 Origin 风险 | `Transport`、前端 runtime |
| P0 | 权限 strict 模式迁移 | 向 Tauri v2 最小权限靠拢 | `Security`、模板、插件 |
| P1 | 实现 `Capability.platforms` | 支持平台级权限隔离 | `Security`、平台抽象 |
| P1 | 统一 IPC 策略服务 | 降低传输层维护成本 | `Transport`、平台窗口 |
| P1 | 明确 Host/UI ready 语义 | 提升生命周期可靠性 | `Hosting`、`Application` |
| P1 | 插件前端资产承载 | 对齐 Wails v3 可安装插件 | `Plugins`、`AssetServer`、npm runtime |
| P2 | 瘦身 `Application` 聚合根 | 提升可测试性和边界清晰度 | `Application`、Managers |
| P2 | 生成插件命令权限清单 | 降低后端/前端/文档漂移 | `SourceGenerators`、插件 |
| P2 | 补齐架构级测试 | 保障禁反射和 IPC 全链路 | `tests/`、前端 runtime |

---

## 4. P0 优化项：必须优先处理

### 4.1 刷新融合策略文档基线

#### 现状

`docs/fusion-strategy-comparison.md` 是三方融合策略的重要文档，但它的基线和日期容易落后于快速演进的代码。当前仓库已经包含更多插件、Android 相关实现、插件双包拆分和更新后的 Wails v3 beta 差异文档，因此需要统一文档事实源。

#### 修改建议

1. 将 `docs/fusion-strategy-comparison.md` 更新为当前日期和当前提交基线。
2. 增加“本文档事实来源”小节，引用：
   - `docs/development/wails-v3-beta-comparison.md`
   - `docs/development/plugin-packaging.md`
   - `docs/development/plugin-platform-split.md`
   - `docs/architecture/security-and-permissions.md`
3. 删除过期的阶段描述、插件数量描述、已完成/未完成判断。
4. 将每个差距项绑定到明确的后续任务编号，例如 `OPT-HOST-001`、`OPT-SEC-003`。
5. 增加文档维护规则：每次完成架构性功能后必须更新融合策略文档或在 PR 中说明无需更新。

#### 验收标准

- 文档中的更新时间、实现状态、插件数量、平台状态与当前代码一致。
- 所有“差距”都有优先级和对应任务编号。
- 不再出现旧本地路径格式链接，例如 Windows 绝对路径 `file:///f:/...`。

---

### 4.2 收敛 `DesktopHostOptions` 配置模型

#### 现状

`DesktopHostOptions` 同时暴露旧式快捷字段和新式分组字段，例如：

- `AssetsDirectory`
- `Assets.RootPath`
- `Permissions`
- `Wails:Permissions` 对应的 `PermissionOptions`
- `Window`
- `App.Windows`

这对兼容有帮助，但会让用户不清楚哪个字段优先，也会增加模板、示例和文档维护成本。

#### 修改建议

1. 明确推荐配置结构：

   ```json
   {
     "Wails": {
       "ApplicationName": "My App",
       "Assets": {
         "RootPath": "dist",
         "DefaultDocument": "index.html",
         "EnableSpaFallback": true
       },
       "Window": {
         "Title": "My App",
         "Width": 1280,
         "Height": 720,
         "Frameless": false
       },
       "Permissions": {
         "Enabled": true,
         "DenyByDefault": true,
         "CapabilitiesDirectory": "capabilities"
       }
     }
   }
   ```

2. 将 `AssetsDirectory` 标记为兼容字段，XML 注释中明确：
   - 推荐使用 `Assets.RootPath`。
   - 当 `Assets.RootPath` 为空时才回退 `AssetsDirectory`。
   - 未来主版本可能移除。
3. 梳理 `Permissions` 字段命名冲突：
   - 如果 `DesktopHostOptions.Permissions` 表示简单权限列表，应更名或标记废弃。
   - 推荐全部迁移到 `PermissionOptions`。
4. 模板和示例统一生成新格式配置。
5. 文档中保留兼容迁移表。

#### 验收标准

- `DesktopApplicationBuilder.Build()` 的配置读取优先级明确且有测试覆盖。
- 旧字段仍可工作，但会记录一次迁移提示日志。
- 新模板只使用推荐字段。

---

### 4.3 引入核心 Options 启动期校验

#### 现状

项目已经采用 `Microsoft.Extensions.Options`，但尚未充分利用 `IValidateOptions<T>` 和 `ValidateOnStart()`。配置错误如果延迟到窗口创建、AssetServer 启动或 IPC 初始化阶段才暴露，会让诊断成本上升。

#### 修改建议

新增以下 validator：

1. `DesktopHostOptionsValidator`
   - `ApplicationName` 非空。
   - `Window.Width > 0`。
   - `Window.Height > 0`。
   - `Assets.DefaultDocument` 非空。
   - `Assets.RootPath` 非空时路径格式有效。
   - `DevServerUrl` 非空时必须是合法 absolute HTTP/HTTPS URI。
2. `PermissionOptionsValidator`
   - `Permissions` 中不得包含空字符串。
   - `CapabilitiesDirectory` 非空时不得包含非法路径字符。
   - `Scopes` key 不得为空。
3. `IpcOptionsValidator`（见 4.4）
   - 端口范围合法。
   - 白名单 URL 格式合法。
   - 禁止公网监听，除非显式测试开关。

注册方式建议：

```csharp
services.AddOptions<DesktopHostOptions>()
    .Bind(configuration.GetSection("Wails"))
    .ValidateOnStart();

services.AddSingleton<IValidateOptions<DesktopHostOptions>, DesktopHostOptionsValidator>();
```

#### 验收标准

- 错误配置在 Host 启动期失败，而不是运行中失败。
- TUnit 测试覆盖合法配置和每类非法配置。
- 错误消息能指出具体配置路径，例如 `Wails:Window:Width`。

---

### 4.4 收紧 HTTP/WebSocket fallback 安全默认值

#### 现状

HTTP/WebSocket fallback 对 Server 模式和调试很有价值，但它们使用本地端口作为 IPC 通道时需要格外谨慎。即使监听 localhost，也应防止本机其他网页或进程误调用 IPC 接口。

#### 修改建议

新增 `IpcOptions`：

```csharp
public sealed class IpcOptions
{
    public bool AllowHttpFallback { get; set; } = true;
    public bool AllowWebSocketFallback { get; set; } = true;
    public bool RequireOriginValidation { get; set; } = true;
    public bool RequireHandshakeToken { get; set; } = true;
    public bool UseRandomLoopbackPort { get; set; } = true;
    public int PortStart { get; set; } = 34115;
    public int PortEnd { get; set; } = 34215;
    public List<string> AllowedOrigins { get; set; } = new();
}
```

实现策略：

1. 所有 fallback IPC 默认只监听 loopback。
2. HTTP/WebSocket IPC 请求必须携带进程级随机 handshake token。
3. token 由后端在 runtime 注入阶段写入前端 bootstrap，不写入日志。
4. Origin 校验默认开启：
   - 本地 WebView 源允许。
   - 白名单远程源允许。
   - 非白名单远程源拒绝。
5. CORS 不再默认 `*`，而是回显已验证白名单 origin。
6. Server 模式可以显式配置为开发友好模式，但生产模板默认 strict。

#### 验收标准

- 非白名单 Origin 调用 IPC 被拒绝。
- 缺少 token 的 HTTP/WebSocket IPC 请求被拒绝。
- 正常 WebView 注入 runtime 后可调用 IPC。
- 相关失败路径有日志，但不泄漏 token。

---

### 4.5 权限 strict 模式迁移

#### 现状

`PermissionOptions.Enabled` 默认关闭，保证了向后兼容，但与 Tauri v2 默认最小权限姿态存在差距。建议以兼容迁移方式推进，而不是一次性破坏现有应用。

#### 修改建议

引入权限模式：

```csharp
public enum PermissionMode
{
    Compatibility,
    Strict
}
```

配置示例：

```json
{
  "Wails": {
    "Permissions": {
      "Mode": "Strict",
      "Enabled": true,
      "DenyByDefault": true,
      "CapabilitiesDirectory": "capabilities"
    }
  }
}
```

行为定义：

| 模式 | Enabled | DenyByDefault | 行为 |
|---|---|---|---|
| Compatibility | false | true | 默认放行，用于旧应用迁移 |
| Strict | true | true | 未授权拒绝，推荐生产模式 |

迁移策略：

1. 当前版本保留兼容默认值。
2. 模板项目默认生成 `Strict` 配置。
3. 当 `Enabled=false` 时启动日志输出一次 warning。
4. 文档明确说明兼容模式只建议开发/迁移期使用。
5. 后续主版本可考虑将默认模式改为 `Strict`。

#### 验收标准

- 兼容模式不破坏旧应用。
- Strict 模式下未授权命令拒绝。
- Capability 授权后命令放行。
- 插件默认权限集在 strict 模式下可通过 capabilities 文件显式授权。

---

## 5. P1 优化项：下一轮架构增强

### 5.1 实现 `Capability.platforms`

#### 现状

`Capability` 已支持权限、窗口和 remote URL 限制，但尚未支持平台过滤。对于 Windows/Linux/Android 多平台应用，这会导致权限声明无法按平台精细收敛。

#### 修改建议

扩展模型：

```csharp
public List<string> Platforms { get; set; } = new();
```

平台标识建议：

- `windows`
- `linux`
- `android`
- `macos`
- `server`

行为：

- `Platforms` 为空：所有平台生效。
- `Platforms` 非空：仅当前平台匹配时生效。
- 平台名大小写不敏感。
- 未知平台名在校验阶段给出 warning 或 validation error。

#### 验收标准

- capability 文件可以声明 `platforms`。
- 当前平台不匹配时不会授权其中权限。
- 测试覆盖 Windows/Linux/Android/Server 模拟平台。

---

### 5.2 引入统一 IPC 传输策略服务

#### 现状

Native IPC、HTTP fallback、WebSocket fallback、AssetServer transport、EventIPC fallback 的选择逻辑分散在不同类型和平台实现中。能力丰富但治理复杂。

#### 修改建议

新增：

```csharp
public interface IIpcTransportStrategy
{
    ITransport CreatePrimaryTransport(IpcTransportContext context);
    IReadOnlyList<ITransport> CreateFallbackTransports(IpcTransportContext context);
}
```

策略服务职责：

1. 根据平台能力选择首选传输。
2. 按统一顺序构造 fallback。
3. 统一注入 `MessageProcessor`、`IpcOriginValidator`、`CorsOptions`、`IpcOptions`。
4. 统一注册事件监听器。
5. 输出诊断日志，说明最终选中的传输路径。

建议默认顺序：

1. Native IPC
2. AssetServer transport
3. EventIPC fallback
4. WebSocket fallback
5. HTTP fallback

实际顺序可按平台修正，但必须集中在策略服务中。

#### 验收标准

- 平台窗口实现不再直接拼装 fallback 链。
- 传输层测试可以通过 mock platform capability 验证选择结果。
- 文档中有统一 IPC 决策图。

---

### 5.3 明确 Host/UI/Window/Runtime ready 语义

#### 现状

`IHostedService.StartAsync` 的成功返回与 UI 主循环、首窗口创建、前端 runtime 注入、IPC 可用并不完全等价。建议显式建模多个 ready 阶段，避免调用方误用 Host lifecycle。

#### 修改建议

定义生命周期阶段：

| 阶段 | 含义 |
|---|---|
| HostStarted | Generic Host 已启动 |
| PlatformStarted | 平台消息循环已启动 |
| FirstWindowCreated | 首窗口对象已创建 |
| RuntimeInjected | 前端 runtime 已注入 |
| IpcReady | IPC 可接收调用 |
| AppReady | 用户可感知应用 ready |

实现方式：

1. 新增 `IApplicationLifecycleState` 或 `ApplicationLifecycleNotifier`。
2. `Application`、平台窗口和传输层在关键节点发出状态变更。
3. `DesktopHostedService.StartAsync` 至少等待 `PlatformStarted` 或可配置等待 `AppReady`。
4. 对外提供 `Application.WaitUntilReadyAsync()`。

#### 验收标准

- Host 启动失败能正确传播异常。
- 测试能等待明确 ready 阶段，而不是 sleep。
- 用户可订阅生命周期事件做初始化。

---

### 5.4 补齐插件前端资产承载

#### 现状

后端插件体系已经成型，但 Wails v3 beta 风格的“插件携带前端资源和脚本”仍需补齐。当前插件更偏后端命令包，前端 runtime 和插件前端 API 之间缺少统一安装体验。

#### 修改建议

新增插件资产接口：

```csharp
public interface IPluginAssetRegistrar
{
    void MapAssets(string pluginName, PluginAssetOptions options);
    void MapEmbeddedAssets(string pluginName, Assembly assembly, string baseNamespace);
}
```

`IPluginContext` 增加：

```csharp
IPluginAssetRegistrar Assets { get; }
```

AssetServer 路由建议：

```text
/__wails_plugins/{pluginName}/manifest.json
/__wails_plugins/{pluginName}/index.js
/__wails_plugins/{pluginName}/assets/*
```

插件前端 manifest 示例：

```json
{
  "name": "store",
  "version": "1.0.0",
  "scripts": ["index.js"],
  "autoInject": true,
  "commands": ["store.get", "store.set", "store.delete"]
}
```

#### 验收标准

- 一个示例插件能以后端包形式提供前端脚本。
- AssetServer 能正确隔离不同插件资源路径。
- 插件名冲突会在启动期失败。
- 前端 runtime 能按 manifest 加载或暴露插件 API。

---

## 6. P2 优化项：长期治理

### 6.1 瘦身 `Application` 聚合根

#### 现状

`Application` 对外作为 Wails v3 风格 facade 是合理的，但内部字段和职责过多。后续应保持 public API 稳定，逐步将内部状态下沉到专门服务。

#### 建议拆分方向

| 当前职责 | 建议服务 |
|---|---|
| 自定义 scheme 注册 | `ICustomSchemeRegistry` |
| pending service route | `IServiceRouteRegistry` |
| shutdown hooks | `IApplicationLifecycleHooks` |
| 单实例回调 | `ISingleInstanceCoordinator` |
| 平台 manager 注入 | `IPlatformManagerRegistry` |
| 全局 Application 静态引用 | 逐步收敛为 `IApplicationAccessor` |

#### 验收标准

- `Application` public API 不破坏。
- 内部字段数量逐步下降。
- Manager 和 registry 可单独单元测试。

---

### 6.2 生成插件命令与权限清单

#### 现状

插件命令、权限、Scope 和前端 API 当前分布在插件代码、文档、前端包和 capability 文件中。长期看需要生成式治理，避免漂移。

#### 修改建议

源生成器输出插件清单：

```json
{
  "plugin": "filesystem",
  "commands": [
    {
      "name": "fs.readTextFile",
      "parameters": [
        { "name": "path", "type": "string", "scope": "fs:allow-read" }
      ],
      "returns": "string",
      "capabilities": ["fs:allow-read"]
    }
  ]
}
```

用途：

1. 生成 TypeScript API。
2. 生成 capability 模板。
3. 生成插件文档表格。
4. 测试中验证每个命令都有权限策略。

#### 验收标准

- 后端命令名和前端 API 命令名来自同一事实源。
- 每个插件包发布时附带 manifest。
- CI 能发现命令/权限/文档漂移。

---

### 6.3 补齐架构级测试

#### 现状

项目已有大量 TUnit 测试，但架构关键路径仍应继续补强：源生成器、前端 runtime、IPC 全链路、安全拒绝路径和并发路径。

#### 修改建议

测试矩阵：

| 测试类型 | 目标 |
|---|---|
| SourceGenerator snapshot | 保障生成代码稳定 |
| Command dispatch full path | 覆盖 JSON → dispatcher → permission → invoker → response |
| IPC no-head test host | 无 GUI 验证前后端协议 |
| Security negative tests | 非授权、非白名单、Scope 越界必须拒绝 |
| Frontend runtime unit tests | 覆盖 invoke、cancel、events、channels、fallback |
| Concurrency stress | 覆盖窗口、事件、channel、permission 并发安全 |

#### 验收标准

- 禁反射核心链路有源生成器测试兜底。
- IPC 协议变更能被测试及时发现。
- 安全默认拒绝路径覆盖率不低于放行路径。

---

## 7. 建议实施顺序

### 7.1 第一批：文档和配置治理

1. 刷新 `docs/fusion-strategy-comparison.md`。
2. 增加本文档到 `docs/README.md` 导航。
3. 收敛 `DesktopHostOptions` 字段优先级。
4. 增加 Options validators。
5. 更新模板和示例配置。

### 7.2 第二批：安全默认值

1. 新增 `IpcOptions`。
2. HTTP/WebSocket fallback 接入 handshake token。
3. Origin/CORS 默认 strict。
4. 权限模式引入 `Compatibility` / `Strict`。
5. 模板默认 strict。

### 7.3 第三批：Tauri Capability 对齐

1. 实现 `Capability.platforms`。
2. Capability 文件 loader 支持平台过滤。
3. PermissionManager 授权展开时执行平台过滤。
4. 插件权限文档更新。

### 7.4 第四批：Wails v3 插件体验对齐

1. 插件资产 manifest。
2. AssetServer 插件资源路由。
3. 前端 runtime 插件加载。
4. 示例插件端到端验证。

### 7.5 第五批：运行时模块化和测试闭环

1. `Application` 内部职责拆分。
2. 统一 IPC strategy。
3. ready 生命周期建模。
4. 源生成器和 IPC 全链路测试补齐。

---

## 8. 风险与缓解

### 8.1 兼容性风险

风险：权限 strict、配置字段迁移、IPC token 校验可能破坏旧应用。

缓解：

- 分阶段引入 compatibility mode。
- 旧字段至少保留一个小版本周期。
- 变更默认值前先更新模板、文档和迁移指南。
- 对 breaking change 使用明确 release note。

### 8.2 平台差异风险

风险：Windows、Linux、Android 在 IPC、窗口 ready、权限能力上行为不同。

缓解：

- 所有策略增加 platform capability abstraction。
- 增加 GuiContract 和平台 mock 测试。
- 文档中明确每个平台支持矩阵。

### 8.3 安全误配置风险

风险：用户关闭权限、CORS 放开或将 IPC 端口暴露给非本地来源。

缓解：

- 生产模板默认 strict。
- 启动时对危险配置输出 warning。
- Options validator 阻止明显危险配置，除非显式开发模式。
- 安全文档提供最小权限示例。

### 8.4 文档漂移风险

风险：架构文档和实现再次不同步。

缓解：

- PR 模板增加“是否更新架构文档”检查项。
- 文档中的每个路线图条目绑定 issue/任务编号。
- 生成式命令/权限清单减少手写表格。

---

## 9. 验收清单

完成全部优化后，项目应满足：

- [ ] `docs/fusion-strategy-comparison.md` 与当前代码状态一致。
- [ ] 模板项目使用推荐配置结构。
- [ ] 核心 Options 在启动期校验。
- [ ] IPC fallback 默认要求 Origin 校验和 handshake token。
- [ ] 权限系统支持 strict 模式。
- [ ] `Capability.platforms` 生效并有测试。
- [ ] 插件可声明并承载前端资产。
- [ ] 插件命令、权限和前端 API 有统一清单。
- [ ] `Application` 内部职责进一步拆分。
- [ ] Host/UI/Window/IPC ready 状态明确可等待。
- [ ] 源生成器、前端 runtime、安全拒绝路径和 IPC 全链路有测试覆盖。

---

## 10. 推荐任务编号

| 编号 | 任务 |
|---|---|
| OPT-DOC-001 | 刷新融合策略文档 |
| OPT-CONFIG-001 | 收敛 `DesktopHostOptions` 配置入口 |
| OPT-CONFIG-002 | 增加 Options validators |
| OPT-IPC-001 | 新增 `IpcOptions` |
| OPT-IPC-002 | IPC fallback handshake token |
| OPT-IPC-003 | 统一 IPC strategy |
| OPT-SEC-001 | 权限 strict 模式 |
| OPT-SEC-002 | `Capability.platforms` |
| OPT-SEC-003 | 安全模板与迁移指南 |
| OPT-PLUGIN-001 | 插件资产 manifest |
| OPT-PLUGIN-002 | AssetServer 插件资源路由 |
| OPT-PLUGIN-003 | 插件命令权限清单生成 |
| OPT-RUNTIME-001 | `Application` 职责拆分 |
| OPT-RUNTIME-002 | 生命周期 ready 状态建模 |
| OPT-TEST-001 | 源生成器测试补齐 |
| OPT-TEST-002 | 前端 runtime 测试补齐 |
| OPT-TEST-003 | IPC 全链路无头测试 |

---

## 11. 总结

Wails.Net 当前架构方向已经清晰：以 ASP.NET Core 的 Host/DI/Config/Logging 为基础设施，以 Wails v3 的运行时对象模型、多窗口和 IPC 体验为核心，以 Tauri v2 的插件、安全和能力模型作为安全边界。后续优化的重点不是重构掉现有设计，而是让这三条主线更加一致：

- Host 层：更标准、更可验证、更可诊断。
- Runtime 层：更模块化、更可测试、更清晰地表达 ready 和 IPC 策略。
- Security/Plugin 层：更默认安全、更可安装、更少手写漂移。

按本文档路线推进后，项目可以在保持 Wails 风格易用性的同时，获得 ASP.NET Core 生态级别的工程治理能力，以及 Tauri 风格的默认安全边界。
