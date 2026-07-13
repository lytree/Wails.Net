# ADR-0002: 插件化架构决策

- 状态：已接受
- 日期：2026-07-13
- 决策者：Wails.Net 团队

## 背景

Wails v3 (Go) 的原生能力散布于各包中，缺乏统一的扩展点。移植到 .NET 时需要回答两个问题：

1. **如何组织系统原生能力**（窗口、文件、网络、剪贴板、对话框、托盘、菜单等），使其 API 风格一致、可测试、可裁剪？
2. **如何允许第三方扩展**？用户应能在不修改核心代码的前提下，注册自定义命令、事件和服务。

约束：

- 必须与 `Microsoft.Extensions.DependencyInjection` 原生集成，避免再造一套 DI。
- 必须支持运行时注册（从 DI 容器收集）和编译时发现（从程序集扫描）两种模式。
- 必须与绑定系统（`BindingManager`）和命令系统（`CommandDispatcher`）协同工作，统一前后端调用路径。
- 必须可单元测试：插件逻辑应可在不启动 GUI 的情况下被验证。
- 参考架构方向：Tauri v2 的"核心即插件"哲学（参考 [AGENTS.md](../../AGENTS.md) "1.1.1 架构融合策略"）。

## 决策

采用 **IPlugin 接口模式 + 双轨调用**，所有系统原生操作以插件命令形式注册。

### 1. IPlugin 三方法契约

参见 [IPlugin.cs](../../src/Wails.Net.Application/Plugins/IPlugin.cs)：

```csharp
public interface IPlugin
{
    string Name { get; }
    void ConfigureServices(IServiceCollection services);
    void Configure(IPluginContext context);
}
```

- `Name`：插件唯一标识，用于日志与诊断。
- `ConfigureServices`：注册插件依赖的服务到 DI 容器（ASP.NET Core 风格）。
- `Configure`：注册命令、事件、绑定等。`IPluginContext` 暴露 `CommandRegistry`、`IConfiguration`、`ILoggerFactory`。

### 2. PluginManager 管理生命周期

参见 [PluginManager.cs](../../src/Wails.Net.Application/Plugins/PluginManager.cs)：

- 维护 `_plugins` 列表，提供 `Register`、`Register<T>`、`RegisterFromServices`、`DiscoverFromAssembly` 四种注册入口。
- `InitializeAll` 按注册顺序调用每个插件的 `ConfigureServices` 与 `Configure`，单个插件初始化失败会抛出异常并中断流程（避免半启动状态）。
- 支持从 DI 容器收集所有 `IPlugin` 实例（`RegisterFromServices`），与 `Microsoft.Extensions.DependencyInjection` 原生集成。

### 3. 双轨调用（BindingManager + CommandDispatcher）

- **BindingManager**：处理 `[Binding]` 特性标记的方法，前端通过方法全名调用（`Namespace.ClassName.MethodName`），用于业务逻辑绑定。
- **CommandDispatcher**：处理 `[Command("name")]` 特性标记的方法，前端通过命令名调用，用于插件命令。

两套机制共享 `GeneratedBindingRegistry`（见 [ADR-0003](0003-source-generator-for-bindings.md)）和 `CommandRegistry`，前端调用入口统一为 `wails.Call(name, args)`。

### 4. 30+ 内置插件

`src/Wails.Net.Application/Plugins/BuiltIn/` 目录下包含 36 个内置插件，覆盖：

- 窗口/屏幕：`WindowPlugin`、`ScreenPlugin`、`WindowStatePlugin`、`WindowsPlugin`
- 文件/存储：`FileSystemPlugin`、`FsWatchPlugin`、`StorePlugin`、`SqlPlugin`、`StrongholdPlugin`、`UploadPlugin`
- 网络/通信：`HttpPlugin`、`WebSocketPlugin`、`LocalhostPlugin`、`CookiePlugin`、`DeepLinkPlugin`
- 系统/集成：`ClipboardPlugin`、`DialogPlugin`、`NotificationPlugin`、`ShellPlugin`、`OpenerPlugin`、`ProcessPlugin`、`GlobalShortcutPlugin`、`PowerManagementPlugin`、`AutostartPlugin`、`TrayPlugin`、`MenuPlugin`
- 应用/信息：`ApplicationPlugin`、`AppInfoPlugin`、`OsInfoPlugin`、`PathPlugin`、`LogPlugin`、`LocalizationPlugin`、`UpdaterPlugin`、`PositionerPlugin`、`FileAssociationPlugin`、`PersistedScopePlugin`

每个插件通过 `[Command("plugin.action")]` 暴露能力，前端可按需调用。第三方插件遵循相同契约，可通过 `AddPlugin<T>()` 或 DI 注册。

## 后果

**正面影响**：

- **统一 API 风格**：所有系统原生能力以 `[Command]` 形式暴露，前端调用方式一致。
- **可测试性**：插件是无 UI 的纯 C# 类，可在 `tests/Wails.Net.Application.Tests/` 下直接单元测试，无需启动 WebView。`BuiltInPluginsTests.cs` 与 `NewPluginsTests.cs` 已覆盖 36 个插件的核心路径。
- **可裁剪性**：用户可仅注册需要的插件，避免引入不必要的依赖；Server 模式下可完全不注册 GUI 插件。
- **DI 友好**：插件服务通过 `ConfigureServices` 注册，与 ASP.NET Core 心智模型一致，开发者学习成本低。
- **扩展性**：第三方插件无需继承基类，仅需实现 `IPlugin` 三方法契约，降低耦合。

**负面影响**：

- **复杂度**：双轨调用（BindingManager + CommandDispatcher）需要前端开发者理解两套命名约定；虽然入口统一，但元数据维护成本翻倍。
- **插件初始化顺序敏感**：`InitializeAll` 按注册顺序执行，若插件 A 依赖插件 B 注册的服务，必须保证 B 先注册；目前依赖开发者显式管理。
- **`DiscoverFromAssembly` 使用反射**：扫描程序集时使用 `Assembly.GetTypes()`，与 AOT 兼容性目标存在张力，未来需要替换为源生成器发现机制。
- **`IPluginContext` 是具体类而非接口**：`PluginContext` 未抽离接口，单元测试时需要构造具体实例，灵活性略低。

## 考虑过的替代方案

1. **纯绑定模式（仅 BindingManager）**：放弃。所有能力以方法绑定形式暴露，命名空间扁平化，难以按能力域分组；权限模型难以附加（绑定方法无法表达"此方法需要 `clipboard:write` 权限"）。
2. **MEF（Managed Extensibility Framework）**：放弃。MEF 自带 DI 容器，与 `Microsoft.Extensions.DependencyInjection` 并存会引入两套生命周期管理，心智负担重；且 MEF 在 AOT 下支持不佳。
3. **纯命令模式（仅 CommandDispatcher）**：放弃。命令模式无法表达"绑定整个服务实例的所有公共方法"这种批量场景，对业务逻辑绑定不友好。
4. **插件基类继承而非接口**：放弃。基类强制实现者继承特定类型，限制灵活性；C# 单继承模型下，若插件需要复用其他基类将无法实现。接口模式允许插件同时实现多个接口。
5. **Tauri v2 的 Permission Manifest 直接照搬**：部分采纳。`Wails.Net.Application/Security/PermissionManager.cs` 已实现权限模型，但与插件解耦——权限是命令调用时的横切关注点，由 `ICommandMiddleware` 处理，而非插件契约的一部分。
6. **运行时插件加载（Assembly.LoadFrom 动态加载 .dll）**：放弃。引入动态加载会破坏 AOT 与裁剪，且带来安全风险（未签名的第三方程序集）。当前设计要求插件在编译时被引用。

## 相关文件

- [IPlugin.cs](../../src/Wails.Net.Application/Plugins/IPlugin.cs)
- [PluginManager.cs](../../src/Wails.Net.Application/Plugins/PluginManager.cs)
- [IPluginContext.cs](../../src/Wails.Net.Application/Plugins/IPluginContext.cs)
- [PluginContext.cs](../../src/Wails.Net.Application/Plugins/PluginContext.cs)
- [BuiltIn 插件目录](../../src/Wails.Net.Application/Plugins/BuiltIn)
- [docs/architecture/plugin-system.md](../architecture/plugin-system.md)
- [docs/plugins.md](../plugins.md)
