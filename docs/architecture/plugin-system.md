# 插件框架

## 1. 概述

Wails.Net 的插件框架借鉴 **Tauri v2 的"核心即插件"哲学**：系统原生操作（窗口、剪贴板、文件、通知、托盘等）不再是绑定对象上的散落方法，而是统一以插件命令（Command）形式注册到全局命令注册表，前端通过 `wails.call('<plugin>.<action>', args)` 路径访问。

这一设计带来三个关键收益：

1. **统一访问路径**：前端 API（如 `wails.window.setTitle`）和回退路径（`wails.call('window.setTitle', [{title:'...'}])`）走同一套 `CommandDispatcher`，无需双轨维护。
2. **强类型 + DI 集成**：插件利用 `Microsoft.Extensions.DependencyInjection` 注册服务，命令参数通过 `System.Text.Json` 反序列化为强类型 Options 对象，避免运行时反射。
3. **可裁剪可扩展**：内置 36 个插件按需通过 `UsePlugin<T>()` 启用，第三方插件遵循同一 `IPlugin` 契约即可热接入。

核心代码位于 `src/Wails.Net.Application/Plugins/`，内置插件位于 `BuiltIn/` 子目录。

## 2. 插件接口

### 2.1 IPlugin — 三方法契约

[IPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPlugin.cs) 定义了所有插件必须实现的最小契约：

```csharp
public interface IPlugin
{
    string Name { get; }
    void ConfigureServices(IServiceCollection services);
    void Configure(IPluginContext context);
}
```

三个方法构成插件生命周期：

| 方法 | 调用时机 | 职责 |
|------|---------|------|
| `Name` | 元数据读取 | 返回插件标识（如 `"window"`、`"filesystem"`），作为命令命名空间前缀 |
| `ConfigureServices` | Host 构建之前（立即调用） | 注册插件依赖的 DI 服务（如 `TrayPlugin` 注册 `TrayHolder` 单例） |
| `Configure` | `DesktopApplicationBuilder.Build()` 时统一调用 | 通过 `IPluginContext` 注册命令、订阅事件 |

`ConfigureServices` 与 `Configure` 分离的原因：前者必须在 `HostApplicationBuilder.Build()` 前完成，以便服务进入 DI 容器；后者需要已构建的 `IConfiguration`/`ILoggerFactory`，故推迟到 Build 阶段。

### 2.2 IPluginContext — 配置上下文

[IPluginContext.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPluginContext.cs) 在 `Configure` 阶段向插件提供四项核心依赖：

```csharp
public interface IPluginContext
{
    IServiceCollection Services { get; }      // DI 服务容器（用于追加注册）
    CommandRegistry Commands { get; }          // 命令注册表（核心）
    IConfiguration Configuration { get; }      // appsettings.json 配置
    ILoggerFactory LoggerFactory { get; }     // 日志工厂
}
```

`Commands` 是插件最常使用的成员——所有原生能力通过它暴露给前端。

### 2.3 PluginContext 实现

[PluginContext.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginContext.cs) 是 `IPluginContext` 的 `internal sealed` 实现，由 `PluginManager.InitializeAll` 在初始化阶段构造并传递给每个插件的 `Configure` 方法。其实现为不可变值对象——四个属性在构造时一次性赋值，插件读取后无法修改全局状态，保证配置阶段的安全性。

## 3. 插件管理

### 3.1 PluginManager — 注册与生命周期

[PluginManager.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginManager.cs) 是插件生命周期的核心协调器，提供四种注册入口：

```csharp
// 1. 直接注册实例
manager.Register(new MyPlugin());

// 2. 泛型注册（要求无参构造函数）
manager.Register<MyPlugin>();

// 3. 从 DI 容器收集所有 IPlugin 单例
manager.RegisterFromServices();

// 4. 从程序集自动发现（扫描 IPlugin 实现）
manager.DiscoverFromAssembly(Assembly.GetExecutingAssembly());
```

`InitializeAll` 方法按注册顺序依次调用每个插件的 `ConfigureServices` 与 `Configure`，整个过程在 try-catch 中执行，单个插件初始化失败会记录错误日志并向上抛出，阻止应用启动以避免部分能力缺失导致的隐性故障。

`PluginManager` 自身注册为 DI 单例（由 `PluginBuilderExtensions.EnsurePluginManagerRegistered` 保证），应用运行时可通过 `IReadOnlyList<IPlugin> Plugins` 属性枚举已加载插件。

### 3.2 PluginBuilderExtensions — Fluent API

[PluginBuilderExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginBuilderExtensions.cs) 提供 `UsePlugin<T>()` 扩展，作为应用启动时启用插件的标准入口：

```csharp
var builder = DesktopApplication.CreateBuilder(args);
builder
    .UsePlugin<WindowPlugin>()
    .UsePlugin<DialogPlugin>()
    .UsePlugin<ClipboardPlugin>()
    .UsePlugin(new FileSystemPlugin(sandboxRoot: @"C:\AppData"));
```

`UsePlugin<T>()` 内部完成四步关键操作：

1. **立即调用** `plugin.ConfigureServices(builder.Services)`——确保 DI 服务在 Host 构建前注册。
2. 调用 `builder.AddPlugin(plugin)`——将插件加入构建器跟踪列表，`Build()` 时统一调用 `Configure`。
3. `builder.Services.AddSingleton<IPlugin>(plugin)`——注册到 DI 容器，使 `RegisterFromServices()` 能收集到。
4. `EnsurePluginManagerRegistered`——幂等地注册 `PluginManager` 单例。

`UsePluginsFromAssembly(assembly?)` 扫描入口程序集中所有 `IPlugin` 实现并批量注册，适合插件自发现的场景。

## 4. 命令注册模式

### 4.1 MapCommand 扩展方法

[MapCommandExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/../../Commands/MapCommandExtensions.cs) 提供 Minimal API 风格的命令注册，支持多种委托签名：

```csharp
// 无参数无返回值（action）
commands.MapCommand("window.close", (Action<ICommandContext>)(ctx => ...));

// 带 Options 参数（action with options）
commands.MapCommand("window.setTitle",
    (Action<ICommandContext, WindowSetTitleOptions>)((ctx, opts) => ...));

// 查询命令（返回 TResult）
commands.MapCommand("window.getZoom",
    (Func<ICommandContext, float>)(ctx => ...));

// 异步命令
commands.MapCommand("fs.readAsync",
    (Func<string, Task<string>>)(async path => await File.ReadAllTextAsync(path)));
```

`CommandRegistry.Register` 在注册时通过 `CommandInvokerCompiler.Compile` 编译表达式树生成强类型调用器，运行时**零反射调用**——这是 Wails.Net 在性能上区别于传统反射绑定方案的关键设计。

### 4.2 Options 对象模式

带参数的命令采用强类型 Options 对象作为参数载体。前端传入的 JSON 对象（如 `{title:'标题'}`）由 `CommandDispatcher` 反序列化为对应 Options 类型，编译期即可捕获参数不匹配错误。以 [WindowPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowPlugin.cs) 为例：

```csharp
public sealed class WindowSetTitleOptions
{
    public string Title { get; set; } = string.Empty;
}

public sealed class WindowSizeOptions
{
    public int Width { get; set; }
    public int Height { get; set; }
}
```

每个 Options 类与一个命令一一对应，命名约定为 `<Plugin><Action>Options`（如 `NotificationOptions`、`TrayIconOptions`）。

### 4.3 命令分类

按签名可分三类：

| 类型 | 签名模式 | 用途 | 示例 |
|------|---------|------|------|
| action | `Action<ICommandContext>` 或 `Action<ICommandContext, TOptions>` | 触发副作用无返回值 | `window.close`、`tray.setIcon` |
| query | `Func<ICommandContext, TResult>` | 查询并返回结果 | `window.getZoom`、`application.isDarkMode` |
| 无参 | `Action` / `Func<TResult>` | 不依赖上下文的纯函数 | `fs.read(path)`、`shell.open(path)` |

### 4.4 双名注册（向后兼容）

[FileSystemPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/FileSystemPlugin.cs) 展示了双名注册模式——同一命令注册短名（向后兼容）和前端 API 名两套别名：

```csharp
// 短名（向后兼容）
context.Commands.MapCommand("fs.read", (Func<string, string>)(path => ...));
// 前端 wails.fs.* API 名
context.Commands.MapCommand("fs.readTextFile", (Func<string, string>)(path => ...));
```

[NotificationPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/NotificationPlugin.cs) 类似地为权限查询注册了 `notification.isPermissionGranted` 和 `notification.hasPermission` 两个别名，前者兼容历史调用，后者对齐前端 API 命名。

## 5. 内置插件清单

Wails.Net 在 `src/Wails.Net.Application/Plugins/BuiltIn/` 下提供 **36 个内置插件**，共注册 **271 个命令**。按功能分类如下：

### 5.1 系统 / 应用类

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `ApplicationPlugin` | `application` | 12 | 应用生命周期（quit/hide/show）、应用信息（getName/getVersion）、主题（isDarkMode/getAccentColor）、屏幕查询、关于对话框 |
| `AppInfoPlugin` | `app` | 4 | 应用元信息查询（精简版） |
| `OsInfoPlugin` | `os` | 13 | 操作系统信息（平台/版本/主机名/CPU/内存） |
| `ProcessPlugin` | `process` | 4 | 进程信息查询 |
| `PowerManagementPlugin` | `power-management` | 3 | 阻止休眠/恢复 |
| `AutostartPlugin` | `autostart` | 3 | 开机自启启用/禁用/查询 |
| `LogPlugin` | `log` | 7 | 结构化日志输出 |
| `LocalizationPlugin` | `localization` | 5 | 国际化字符串加载/切换语言 |
| `LocalhostPlugin` | `localhost` | 8 | 本地回环服务器配置 |

### 5.2 文件类

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `FileSystemPlugin` | `filesystem` | 21 | 文件读写、目录操作、文件元数据，**内置沙箱路径穿越防护** |
| `FsWatchPlugin` | `fs-watch` | 5 | 文件系统变更监听 |
| `PathPlugin` | `path` | 11 | 系统特殊目录（临时/文档/配置）查询 |
| `DialogPlugin` | `dialog` | 7 | 原生对话框（消息/警告/错误/提问/打开文件/保存文件） |
| `FileAssociationPlugin` | `file-association` | 3 | 文件扩展名关联注册 |

### 5.3 网络类

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `HttpPlugin` | `http` | 5 | `http.fetch`/`get`/`post`/`put`/`delete` 异步 HTTP 客户端 |
| `WebSocketPlugin` | `websocket` | 5 | WebSocket 连接管理 |
| `UploadPlugin` | `upload` | 4 | 文件上传 |
| `CookiePlugin` | `cookie` | 4 | WebView Cookie 管理 |
| `DeepLinkPlugin` | `deep-link` | 3 | 自定义协议 URL 处理 |

### 5.4 窗口类

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `WindowPlugin` | `window` | **54** | 窗口全操作（标题/尺寸/位置/状态/全屏/置顶/DevTools/缩放/导航/打印/JS 执行/CSS 注入/透明度/任务栏/角标/查询） |
| `WindowsPlugin` | `windows` | 5 | 多窗口管理（创建/列举） |
| `WindowStatePlugin` | `window-state` | 3 | 窗口状态持久化 |
| `ScreenPlugin` | `screen` | 2 | 屏幕枚举/主屏查询 |
| `PositionerPlugin` | `positioner` | 5 | 窗口定位（托盘附近/居中等预设位置） |

### 5.5 数据类

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `SqlPlugin` | `sqlite` | 10 | SQLite 数据库操作 |
| `StorePlugin` | `store` | 7 | 键值对持久化存储 |
| `StrongholdPlugin` | `stronghold` | 8 | 加密敏感数据存储 |
| `ClipboardPlugin` | `clipboard` | 7 | 剪贴板读写（文本/HTML/图像） |

### 5.6 快捷方式 / UI 类

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `GlobalShortcutPlugin` | `globalshortcut` | 4 | 全局快捷键注册/注销 |
| `MenuPlugin` | `menu` | 5 | 应用菜单/上下文菜单/弹出菜单 |
| `TrayPlugin` | `tray` | 8 | 系统托盘（图标/标签/菜单/提示/显示状态） |
| `NotificationPlugin` | `notification` | 6 | 系统通知（显示/取消/权限） |
| `OpenerPlugin` | `opener` | 5 | 用默认程序打开文件/URL |

### 5.7 更新 / 其他

| 插件 | Name | 命令数 | 核心功能 |
|------|------|--------|---------|
| `UpdaterPlugin` | `updater` | 4 | 应用自动更新检查/下载 |
| `PersistedScopePlugin` | `persisted-scope` | 7 | 持久化作用域管理 |

### 5.8 重点插件详解

#### WindowPlugin（54 命令）

[WindowPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowPlugin.cs) 是命令量最大的插件，对应 Wails v3 前端的 `wails.window.*` API。命令通过 `ICommandContext.WindowId` 定位目标 `WebviewWindow` 实例：

```csharp
private static WebviewWindow GetWindowOrThrow(ICommandContext ctx)
{
    if (ctx.WindowId is not uint windowId)
        throw new InvalidOperationException("窗口消息未指定目标窗口 ID");
    var window = Application.Get()?.GetWindow(windowId);
    return window ?? throw new InvalidOperationException($"找不到 ID 为 {windowId} 的窗口");
}
```

54 个命令覆盖：标题/尺寸/位置、显示/隐藏/状态（minimize/maximize/restore/focus）、全屏与置顶、DevTools、缩放、导航（goBack/goForward/reload/setURL/setHTML）、打印与 PDF 导出、JS 执行与 CSS 注入、透明度、可调整大小、自定义协议、任务栏（skip/ignoreCursorEvents/badge）、查询类（getSize/getPosition/getURL/getZoom/isFullscreen/isMaximised/isMinimised/isVisible/isFocused）。

#### ApplicationPlugin

[ApplicationPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/ApplicationPlugin.cs) 通过 `Application.Get()` 静态单例定位应用实例，暴露 12 个应用级命令（quit/hide/show/getName/getVersion/getDescription/setIcon/isDarkMode/getAccentColor/getPrimaryScreen/getScreens/showAboutDialog）。

#### TrayPlugin

[TrayPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/TrayPlugin.cs) 是**唯一在 `ConfigureServices` 中注册 DI 服务**的示例插件——注册 `TrayHolder` 单例持有当前活动托盘实例：

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<TrayHolder>();
}
```

托盘命令通过 `ctx.Services.GetRequiredService<TrayHolder>()` 获取 `ActiveTray`，再委托到 `ISystemTrayManager` 的平台实现。

#### FileSystemPlugin（沙箱防护）

[FileSystemPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/FileSystemPlugin.cs) 演示了**安全沙箱模式**——构造时传入 `sandboxRoot`，所有路径解析经 `GetSafePath` 校验是否在沙箱范围内：

```csharp
if (_sandboxRoot is not null)
{
    var rootWithSep = _sandboxRoot.EndsWith(Path.DirectorySeparatorChar)
        ? _sandboxRoot
        : _sandboxRoot + Path.DirectorySeparatorChar;
    if (!fullPath.StartsWith(rootWithSep, comparison) && fullPath != _sandboxRoot)
        throw new UnauthorizedAccessException($"路径超出沙箱范围: {path}");
}
```

此插件同时注册短名（`fs.read`）和前端 API 名（`fs.readTextFile`）两套命令共 21 个。

## 6. 与 Tauri v2 的对比

### 6.1 相似点

| 维度 | Tauri v2 | Wails.Net |
|------|---------|-----------|
| 核心哲学 | 核心能力即插件（窗口/剪贴板/对话框/通知等都是插件） | 完全一致——内置 36 插件覆盖系统原生能力 |
| 命令注册 | `app.invoke_handler` + Rust 函数 | `CommandRegistry.MapCommand` + C# 委托 |
| 命令路径 | `plugin://command` 路由 | `<plugin>.<action>` 命名约定 |
| Options 模式 | Rust 结构体反序列化 | C# POCO + `System.Text.Json` 反序列化 |
| 权限模型 | Capability 配置文件 | （已规划）`persisted-scope`/`persisted-scope` 插件雏形 |

### 6.2 差异点

| 维度 | Tauri v2 | Wails.Net |
|------|---------|-----------|
| DI 容器 | Rust 无 DI，命令在 `Builder` 上链式注册 | `Microsoft.Extensions.DependencyInjection` 全栈集成 |
| 服务生命周期 | 无显式生命周期管理 | `ConfigureServices`（注册）→ `Configure`（命令注册）两阶段 |
| 类型系统 | Rust 编译期保证 | C# 编译期 + 表达式树编译的强类型调用器，运行时零反射 |
| 命令发现 | 显式注册 | `DiscoverFromAssembly` 程序集扫描自动发现 |
| 双名兼容 | 不支持 | 同一命令可注册多别名，向后兼容旧前端 API |
| 沙箱安全 | 通过 Capability 文件配置 | `FileSystemPlugin` 构造参数注入沙箱根，运行时校验 |

Wails.Net 在保留 Tauri 哲学的同时，借助 .NET 的 DI 与表达式树编译实现更强的类型安全与更优的运行时性能。

## 7. 自定义插件开发指南

### 7.1 步骤一：实现 IPlugin

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace MyApp.Plugins;

public sealed class WeatherPlugin : IPlugin
{
    public string Name => "weather";

    public void ConfigureServices(IServiceCollection services)
    {
        // 注册插件所需的 DI 服务
        services.AddSingleton<IWeatherService, OpenWeatherService>();
    }

    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // action 命令（带 Options）
        commands.MapCommand("weather.setCity",
            (Action<ICommandContext, WeatherCityOptions>)((ctx, opts) =>
            {
                var svc = ctx.Services.GetRequiredService<IWeatherService>();
                svc.SetCity(opts.City);
            }));

        // query 命令（返回 TResult）
        commands.MapCommand("weather.getTemperature",
            (Func<ICommandContext, string, double>)((ctx, city) =>
            {
                var svc = ctx.Services.GetRequiredService<IWeatherService>();
                return svc.GetTemperature(city);
            }));

        // 异步命令
        commands.MapCommandAsync("weather.fetchForecast",
            (Func<string, Task<WeatherForecast>>)(async city =>
                await ctx.Services.GetRequiredService<IWeatherService>()
                    .FetchForecastAsync(city)));
    }
}

public sealed class WeatherCityOptions
{
    public string City { get; set; } = string.Empty;
}
```

### 7.2 步骤二：注册 DI 服务

在 `ConfigureServices` 中通过 `IServiceCollection` 注册插件所需服务，遵循 ASP.NET Core 生命周期约定：

- `AddSingleton` — 跨请求共享的无状态服务（如 `TrayHolder`）
- `AddScoped` — 每个请求/会话独立的服务
- `AddTransient` — 轻量级、瞬态的服务

### 7.3 步骤三：注册命令

在 `Configure` 中通过 `IPluginContext.Commands.MapCommand` 注册命令，命名约定为 `<pluginName>.<action>`（如 `weather.getTemperature`）。命令委托的第一参数通常为 `ICommandContext`，提供：
- `Services` — DI 服务容器
- `WindowId` — 当前调用来源窗口 ID（用于窗口命令路由）

### 7.4 步骤四：使用 UsePlugin<T> 注册到应用

```csharp
var builder = DesktopApplication.CreateBuilder(args);

builder
    .UsePlugin<WindowPlugin>()
    .UsePlugin<DialogPlugin>()
    .UsePlugin<WeatherPlugin>();   // 自定义插件

var app = builder.Build();
await app.RunAsync();
```

如需注入构造参数（如 `FileSystemPlugin` 的沙箱根），使用实例重载：

```csharp
builder.UsePlugin(new FileSystemPlugin(sandboxRoot: appDataPath));
```

### 7.5 前端调用示例

```typescript
// 调用 action 命令
await wails.call('weather.setCity', [{ city: 'Shanghai' }]);

// 调用 query 命令
const temp = await wails.call('weather.getTemperature', ['Shanghai']);

// 调用异步命令
const forecast = await wails.call('weather.fetchForecast', ['Shanghai']);
```

自定义插件与内置插件走完全相同的命令调度路径（`MessageProcessor` → `CommandDispatcher` → `CommandRegistry.Find` → 编译后的强类型 `Invoker`），开发者无需关心底层传输细节。

---

**参考文件**：
- [IPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPlugin.cs)
- [IPluginContext.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPluginContext.cs)
- [PluginContext.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginContext.cs)
- [PluginManager.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginManager.cs)
- [PluginBuilderExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginBuilderExtensions.cs)
- [CommandRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandRegistry.cs)
- [MapCommandExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/MapCommandExtensions.cs)
- [BuiltIn 目录](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn)

**最后更新**：2026-07-13
