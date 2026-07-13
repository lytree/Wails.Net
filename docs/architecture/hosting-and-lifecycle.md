# 宿主层与应用生命周期

## 概述

Wails.Net 的宿主层（Hosting）基于 [Microsoft.Extensions.Hosting](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 的 **Generic Host** 模式构建，将桌面应用的启动、依赖注入（DI）、配置、日志和生命周期管理统一纳入 .NET 通用主机的标准范式。该设计借鉴了 ASP.NET Core 的成熟模式，同时将 [Application.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs)（对应 Wails v3 Go 版本的 `Application` 结构）适配为 `IHostedService`，使原有窗口/IPC/绑定对象模型无需重写即可在 Generic Host 中运行。

宿主层位于命名空间 `Wails.Net.Application.Hosting`，核心类型包括：

- [DesktopApplicationBuilder](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs) — Fluent API 构建器，封装 `HostApplicationBuilder`
- [DesktopApplication](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplication.cs) — 应用实例，封装 `IHost`，对外暴露 `RunAsync`/`Stop`
- [DesktopHostedService](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostedService.cs) — `IHostedService` 适配器，将 `Application` 桥接到 Host 生命周期
- [DesktopHostOptions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostOptions.cs) — 强类型配置选项，绑定 `appsettings.json` 的 `Desktop` 节

## 架构图

```
┌──────────────────────────────────────────────────────────┐
│                  用户 Program.cs                          │
│   DesktopApplicationBuilder.CreateBuilder(args)          │
└────────────────────────┬─────────────────────────────────┘
                         │ Fluent API
                         ▼
┌──────────────────────────────────────────────────────────┐
│            DesktopApplicationBuilder                     │
│   ┌────────────────────────────────────────────────┐     │
│   │  HostApplicationBuilder (内部封装)              │     │
│   │   ├── Configuration (appsettings.json)        │     │
│   │   ├── IServiceCollection (DI)                 │     │
│   │   └── ILoggingBuilder                         │     │
│   └────────────────────────────────────────────────┘     │
│   ┌────────────────────────────────────────────────┐     │
│   │  Plugins (List<IPlugin>)  →  InitializePlugins │     │
│   │  CommandRegistry          →  MapCommand       │     │
│   └────────────────────────────────────────────────┘     │
│                    Build()                                │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│                  DesktopApplication                       │
│   ┌────────────────────────────────────────────────┐     │
│   │  IHost (已构建)                                │     │
│   │   └── DesktopHostedService : IHostedService    │     │
│   │         ├── StartAsync  → 启动 STA UI 线程      │     │
│   │         │               → Application.Run()     │     │
│   │         └── StopAsync   → Application.Shutdown()│     │
│   └────────────────────────────────────────────────┘     │
│   Application (兼容层)        RunAsync / Stop            │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │  IPlatformApp.Run()  │  ← 平台主循环阻塞
              │  (Win32 / Linux)     │
              └──────────────────────┘
```

## DesktopApplicationBuilder

[DesktopApplicationBuilder.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs) 是 Fluent API 构建器，内部封装 `HostApplicationBuilder`，对外暴露 `Services`、`Configuration`、`Logging` 三大入口，使 Wails.Net 应用与 ASP.NET Core 应用拥有相同的配置体验。

### 创建方式

构造函数为 `internal`，仅能通过静态工厂 `CreateBuilder` 创建：

```csharp
var builder = DesktopApplicationBuilder.CreateBuilder(args);
```

构造时设置 `HostApplicationBuilderSettings.ContentRootPath = AppContext.BaseDirectory`，确保桌面应用从可执行文件目录而非工作目录定位 `appsettings.json` 等配置文件。

### 默认服务注册

构造函数立即调用 `ConfigureServices()`，完成以下注册：

1. **`DesktopHostOptions` 绑定** — 通过 `Services.AddOptions<DesktopHostOptions>().Bind(Configuration.GetSection("Desktop"))` 绑定到配置节
2. **核心管理器与服务** — 调用 `Services.AddWailsCore()`（详见 [ServiceCollectionExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/ServiceCollectionExtensions.cs)），注册 `EventProcessor`、`BindingManager`、`WindowManager`、`DialogManager`、`ScreenManager` 以及内置服务（`FileServerService`、`KvStoreService`、`LogService`、`NotificationService`、`SqliteService`、`UpdaterService`）
3. **`ApplicationOptions` 工厂单例** — 从 `DesktopHostOptions` 映射 `Name`、`SingleInstance`、`Frameless` 字段
4. **`Application` 工厂单例** — 复用已注册的 `ApplicationOptions` 创建实例
5. **`DesktopHostedService`** — 通过 `AddHostedService<DesktopHostedService>()` 注册，将 `Application` 适配为 `IHostedService`

### Fluent API

| 方法 | 用途 |
|------|------|
| `Configure(Action<DesktopHostOptions>)` | 追加选项配置回调，在 `Build()` 时通过 `Services.Configure` 注入 |
| `ConfigurePlatform(Action<IPlatformApp>)` | 平台应用创建后的配置回调 |
| `UsePlatform<TPlatform>()` | 注册 `IPlatformApp` 实现到 DI（`AddSingleton<IPlatformApp, TPlatform>`） |
| `AddPlugin(IPlugin)` | 由 `PluginBuilderExtensions.UsePlugin` 调用，将插件加入跟踪列表 |

### Build 流程

`Build()` 方法的关键步骤顺序非常重要：

1. 应用 `Configure` 回调到 `IServiceCollection`
2. **`InitializePlugins()`** — 在 Host 构建前调用每个插件的 `IPlugin.Configure(IPluginContext)`，此时插件可向 `IServiceCollection` 注册 DI 服务、向 `CommandRegistry` 注册命令。为获取 `ILoggerFactory`，临时调用 `BuildServiceProvider()`，并以 `NullLoggerFactory.Instance` 兜底
3. 将 `CommandRegistry` 注册为单例
4. 调用 `_hostBuilder.Build()` 构建 `IHost`
5. 从 DI 获取 `DesktopHostOptions.Value` 和 `Application` 单例
6. 若 DI 中存在 `IPlatformApp`，则通过 `application.SetPlatformApp(platformApp)` 注入，并执行 `ConfigurePlatform` 回调
7. **`application.InitializeFromServiceProvider(host.Services)`** — 用 DI 容器中已注册的实例替换 `Application` 内部各管理器字段
8. 创建 `CommandDispatcher` 并赋值给 `application.CommandDispatcher`
9. 当 `Assets.RootPath` 非空且目录存在时，创建 `FileAssetServer` 并设置到 `application.AssetServer`

## DesktopApplication

[DesktopApplication.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplication.cs) 实现 [IDesktopApplication](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/IDesktopApplication.cs) 接口，是用户最终调用 `RunAsync` 启动应用的入口。它持有：

- `_host` — 已构建的 `IHost` 实例
- `_application` — 兼容层 `Application` 实例
- `_cts` — 内部 `CancellationTokenSource`，由 `Stop()` 触发取消

### RunAsync 机制

```csharp
public async Task<int> RunAsync(CancellationToken cancellationToken = default)
{
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
    try
    {
        await _host.RunAsync(linkedCts.Token).ConfigureAwait(false);
        return 0;
    }
    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
    {
        return 0;
    }
    catch (Exception ex)
    {
        var logger = _host.Services.GetService<ILogger<DesktopApplication>>();
        logger?.LogError(ex, "应用运行时发生未处理异常");
        return 1;
    }
}
```

关键点：

- 通过 `CreateLinkedTokenSource` 链接内部取消令牌和外部传入的令牌，任一触发都会停止 Host
- 正常退出返回 `0`；`OperationCanceledException` 且确实由内部 `Stop()` 触发时也返回 `0`
- 其他异常返回 `1` 并通过 `ILogger<DesktopApplication>` 记录错误日志

### Stop 与 Dispose

- `Stop()` 同时调用 `_cts.Cancel()` 和 `_application.Shutdown()`，使 Host 收到取消信号并触发 `DesktopHostedService.StopAsync`
- `DisposeAsync()` 释放 `_cts` 并异步释放 `_host`（实现 `IAsyncDisposable`）

## DesktopHostedService

[DesktopHostedService.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostedService.cs) 是连接 Generic Host 与 `Application` 生命周期的关键适配器，实现了 `IHostedService` 和 `IDisposable`。DI 通过构造函数注入 `ILogger<DesktopHostedService>`、`Application` 和 `IHostApplicationLifetime`。

### StartAsync

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("桌面应用启动中...");
    RegisterGlobalExceptionHandlers();

    _uiThread = new Thread(() =>
    {
        try { _application.Run(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application.Run() 发生异常");
            _startedTcs.TrySetException(ex);
        }
        finally
        {
            _startedTcs.TrySetResult();
            try { _lifetime.StopApplication(); }
            catch { /* 忽略，避免掩盖原始异常 */ }
        }
    })
    {
        Name = "Wails.Net UI Thread",
        IsBackground = true,
    };

    if (OperatingSystem.IsWindows())
    {
        _uiThread.SetApartmentState(ApartmentState.STA);
    }
    _uiThread.Start();
    return Task.CompletedTask;
}
```

关键设计：

1. **专用 STA 线程**：Win32 消息循环和 WebView2 强制要求 STA 线程，因此不能使用线程池线程（MTA）。Linux/GTK 不要求 STA，但设置也无副作用，使用 `OperatingSystem.IsWindows()` 条件编译确保跨平台兼容
2. **后台线程**：`IsBackground = true`，命名 `"Wails.Net UI Thread"` 便于调试
3. **`_lifetime.StopApplication()`**：UI 线程退出（窗口关闭按钮触发 `PostQuitMessage(0)` → 消息循环退出 → `Application.Run()` 返回）后必须通知 Host 停止，否则 Host 会一直等待导致进程不退出
4. **`TaskCompletionSource`**：用于异常传播，但 `StartAsync` 直接返回 `Task.CompletedTask`，不阻塞 Host 启动其他 `IHostedService`

### StopAsync

```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("桌面应用停止中...");
    _application.Shutdown();

    if (_uiThread is not null && _uiThread.IsAlive)
    {
        try { _uiThread.Join(TimeSpan.FromSeconds(5)); }
        catch { /* 超时后放弃等待 */ }
    }
    await Task.CompletedTask;
}
```

`StopAsync` 调用 `Application.Shutdown()` 并等待 UI 线程退出，超时为 5 秒，避免平台主循环卡死时无限阻塞 Host 关闭。

## 服务生命周期

[ServiceRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/ServiceRegistry.cs) 是一个简单的 `List<object>` 容器，提供 `Register`/`Unregister`/`GetService<T>`/`GetServices<T>`/`Clear`/`CopyTo` 方法。`CopyTo` 以单例方式将已注册实例迁移到 `IServiceCollection`（使用运行时类型作为服务类型）。

### IServiceStartup — 顺序启动

```csharp
public interface IServiceStartup
{
    Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken);
}
```

在 [Application.Run](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 中按注册顺序同步等待（`GetAwaiter().GetResult()`）依次调用：

```csharp
foreach (var service in _serviceRegistry.Services)
{
    if (service is IServiceStartup startup)
    {
        startup.ServiceStartup(_options, _cts.Token).GetAwaiter().GetResult();
    }
}
```

### IServiceShutdown — 逆序关闭

```csharp
public interface IServiceShutdown
{
    Task ServiceShutdown(CancellationToken cancellationToken);
}
```

在 `Application.Shutdown` 中按 **逆序** 调用：

```csharp
var services = _serviceRegistry.Services.ToList();
for (var i = services.Count - 1; i >= 0; i--)
{
    if (services[i] is IServiceShutdown shutdown)
    {
        shutdown.ServiceShutdown(CancellationToken.None).GetAwaiter().GetResult();
    }
}
```

逆序关闭确保后注册的服务（通常依赖先注册的服务）先释放，避免使用已被释放的依赖。`AssetServer` 也按 `IServiceStartup`/`IServiceShutdown` 协议参与生命周期。

### Run / Shutdown 完整流程

`Application.Run()` 顺序：

1. `SetupSingleInstance()` — 单实例检查
2. 触发 `OnBeforeStart` 回调和 hook 列表
3. **顺序启动** `IServiceStartup` 服务
4. 启动 `AssetServer`（若实现 `IServiceStartup`）
5. 启动 `ITransport`
6. 触发 `OnStartup` / `OnAfterStart` 回调
7. 调用 `_platformApp.Run()` **阻塞**主循环
8. 平台主循环退出后调用 `Shutdown()`

`Application.Shutdown()` 顺序：

1. 取消 `_cts`（应用级取消令牌）
2. 执行 `_shutdownTasks`（含 `ApplicationOptions.ShutdownTasks` 和 `OnShutdown(Action)` 注册的任务），每个任务异常被吞掉，避免中断关闭流程
3. 触发 `OnShutdown` 回调
4. 关闭所有窗口（`window.Close()`，异常吞掉）
5. 停止 `ITransport`
6. 停止 `AssetServer`（若实现 `IServiceShutdown`）
7. **逆序停止** `IServiceShutdown` 服务
8. 调用 `_platformApp.Destroy()` 并设置 `_isRunning = false`

## 配置选项

[DesktopHostOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostOptions.cs) 对应 `appsettings.json` 的 `Desktop` 节，使用标准 `IOptions<T>` 模式绑定：

```json
{
  "Desktop": {
    "ApplicationName": "MyApp",
    "SingleInstance": true,
    "Window": {
      "Width": 1280,
      "Height": 720,
      "Title": "MyApp",
      "Frameless": false
    },
    "Assets": {
      "RootPath": "dist",
      "DefaultDocument": "index.html",
      "EnableSpaFallback": true
    },
    "Permissions": [ "fs:read", "shell:open" ]
  }
}
```

| 属性 | 类型 | 默认值 | 用途 |
|------|------|--------|------|
| `ApplicationName` | `string` | `"Wails.Net Application"` | 应用名称，映射到 `ApplicationOptions.Name` |
| `Window` | `WindowOptions` | 1280×720，标题 `"Wails.Net"` | 窗口尺寸/标题/边框 |
| `AssetsDirectory` | `string` | `"dist"` | 资源目录（传统字段） |
| `DevServerUrl` | `string?` | `null` | 开发服务器 URL |
| `SingleInstance` | `bool` | `false` | 单实例模式 |
| `Permissions` | `List<string>` | 空 | 权限列表（Tauri v2 风格） |
| `Assets` | `AssetsOptions` | 见下 | 静态资源配置 |

`AssetsOptions` 子选项：

- `RootPath` — 静态资源根路径（支持相对路径，相对于 `AppContext.BaseDirectory`）。设置有效路径后，`Build()` 自动创建 `FileAssetServer`，前端通过 `http://wails.localhost/` 加载资源，避免 `file://` 协议权限问题
- `DefaultDocument` — 默认文档名（默认 `"index.html"`）
- `EnableSpaFallback` — SPA 路由回退（默认 `true`），适用于 Vue/React/Angular 客户端路由

代码侧通过 `IOptions<DesktopHostOptions>` 注入：

```csharp
var desktopOpts = host.Services.GetRequiredService<IOptions<DesktopHostOptions>>().Value;
```

`Build()` 中根据 `Assets.RootPath` 自动创建 `FileAssetServer` 并赋值给 `Application.AssetServer`，前端窗口自动导航到 `http://wails.localhost/`。

## 全局异常处理

`DesktopHostedService.RegisterGlobalExceptionHandlers()` 在 `StartAsync` 中注册两类全局异常处理器：

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    _logger.LogError(ex, "全局未处理异常 (IsTerminating={IsTerminating})", e.IsTerminating);
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    _logger.LogError(e.Exception, "未观察的 Task 异常");
    e.SetObserved(); // 标记已观察，防止进程被强制终止
};
```

| 事件 | 触发场景 | 处理方式 |
|------|---------|---------|
| `AppDomain.UnhandledException` | CLR 任意线程抛出未捕获异常 | 记录 `IsTerminating` 标志，便于区分致命异常 |
| `TaskScheduler.UnobservedTaskException` | Task 抛出异常但未被 `await`/检查 | 记录日志后调用 `e.SetObserved()` 阻止进程被强制终止 |

UI 线程内 `Application.Run()` 抛出的异常也会被 `catch` 块捕获，记录后通过 `TaskCompletionSource.TrySetException` 传播。

## 与 ASP.NET Core 的对比

| 方面 | ASP.NET Core | Wails.Net | 说明 |
|------|-------------|-----------|------|
| 构建器 | `WebApplicationBuilder` | `DesktopApplicationBuilder` | 同样封装 `HostApplicationBuilder` |
| 宿主实例 | `WebApplication` | `DesktopApplication` | 都实现 `IAsyncDisposable` |
| 入口 | `app.RunAsync()` | `app.RunAsync()` | 都返回 `int` 退出码 |
| IHostedService | Kestrel / 后台服务 | `DesktopHostedService` | Wails.Net 仅有一个，桥接 `Application` |
| 主线程 | 线程池 | 专用 STA 线程 | Win32 消息循环和 WebView2 要求 STA |
| 配置 | `appsettings.json` + `IOptions<T>` | 相同 | Wails.Net 绑定 `Desktop` 节 |
| 关闭信号 | `IC Lifetime.ApplicationStopping` | `IHostApplicationLifetime.StopApplication()` | UI 线程退出后反向通知 Host |
| 异常处理 | 中间件管道 | `AppDomain` + `TaskScheduler` 全局处理器 | 桌面应用无 HTTP 管道 |
| 平台抽象 | IServer（Kestrel / IIS / HTTP.sys） | `IPlatformApp`（Windows / Linux） | 共用接口驱动模式 |

### 相似点

- 都使用 `HostApplicationBuilder` 作为底层构建器，统一 `Configuration`/`Services`/`Logging` 三件套
- 都遵循 `IHostedService` 启动/停止契约
- 都通过 `IHostApplicationLifetime` 协调生命周期事件
- 都使用 `IOptions<T>` 强类型配置绑定

### 差异点

- **线程模型**：ASP.NET Core 完全异步、无 STA 要求；Wails.Net 必须在专用 STA 线程运行 Win32 消息循环，因此 `StartAsync` 通过 `new Thread(...)` 启动阻塞主循环
- **关闭方向**：ASP.NET Core 由 Host 触发 `StopApplication` → 各 `IHostedService.StopAsync`；Wails.Net 的关闭可能由 UI 线程（窗口关闭）触发，反向通过 `_lifetime.StopApplication()` 通知 Host
- **应用层生命周期**：Wails.Net 在 `Application` 内部还维护一套独立的 `IServiceStartup`/`IServiceShutdown` 服务生命周期，与 `IHostedService` 并存，对应 Wails v3 Go 版本的服务模型
- **服务容器**：`Application` 同时持有自己的 `ServiceRegistry`（`List<object>`）和 DI 注入的 `IServiceProvider`，`InitializeFromServiceProvider` 在 Host 构建后将 DI 中的实例同步到 `Application` 字段

这种"双层生命周期"设计使 Wails.Net 既能复用 Wails v3 的对象模型，又能享受 Generic Host 提供的标准化配置、日志和 DI 体验。
