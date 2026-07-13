# Wails.Net 架构详解

本文档描述 Wails.Net 的整体架构设计、分层结构和核心模式。

## 架构概览

Wails.Net 采用**分层架构**，借鉴 ASP.NET Core 的 Generic Host 模式，融合 Wails v3 的窗口/IPC 对象模型和 Tauri v2 的插件/权限设计。

```
┌─────────────────────────────────────────────────────┐
│                    用户应用代码                       │
├─────────────────────────────────────────────────────┤
│  Hosting 层 (DesktopApplicationBuilder / Generic Host) │
├──────────┬──────────┬──────────┬───────────────────┤
│ Plugins  │ Commands │ Services │   Events          │
│ (30 个)  │ (调度器) │ (内置)   │ (事件总线)         │
├──────────┴──────────┴──────────┴───────────────────┤
│              Application 核心                        │
│  (Bindings / WindowManager / DialogManager)          │
├─────────────────────────────────────────────────────┤
│              Platform 抽象层                        │
│  (IPlatformApp / IWebviewWindowImpl)                │
├──────────────────────┬──────────────────────────────┤
│   Windows 实现       │        Linux 实现             │
│  (WebView2 + Win32) │  (GTK4 + WebKitGTK)          │
├──────────────────────┴──────────────────────────────┤
│              Transport 传输层                       │
│  (IPC / WebSocket / HTTP / AssetServer)             │
├─────────────────────────────────────────────────────┤
│              AssetServer 资源服务器                  │
│  (静态文件 + 中间件管道 + Runtime JS 注入)           │
└─────────────────────────────────────────────────────┘
```

## 核心模块

### 1. Hosting 层

使用 Microsoft.Extensions.Hosting Generic Host 管理应用生命周期。

```
DesktopApplicationBuilder
    │
    ├── 配置 (Configuration / Options)
    ├── 服务注册 (DI / IServiceCollection)
    ├── 插件注册 (UsePlugin<T>)
    └── Build() → DesktopApplication
                     │
                     └── RunAsync() → 阻塞运行
```

**关键类**：
- `DesktopApplicationBuilder` - Fluent API 构建器
- `DesktopApplication` - 应用实例，封装 IHost
- `DesktopHostedService` - 将 Application 适配为 IHostedService
- `DesktopHostOptions` - 强类型配置选项

### 2. Application 核心

对应 Wails v3 的 `Application` 结构，管理：
- **绑定管理器** (`BindingManager`) - 反射注册和调用 C# 方法
- **事件处理器** (`EventProcessor`) - 事件订阅/发布/钩子
- **窗口管理器** (`WindowManager`) - 窗口生命周期
- **对话框管理器** (`DialogManager`) - 原生对话框
- **屏幕管理器** (`ScreenManager`) - 显示器信息
- **传输层** (`ITransport`) - IPC 通信

### 3. Platform 抽象

通过接口抽象平台差异，实现 Windows 和 Linux 的统一编程模型：

| 接口 | Windows 实现 | Linux 实现 |
|------|-------------|-----------|
| `IPlatformApp` | `WindowsPlatformApp` | `LinuxPlatformApp` |
| `IWebviewWindowImpl` | `Win32WebviewWindow` | `LinuxWebviewWindow` |
| `IClipboardImpl` | `WindowsClipboard` | `LinuxClipboard` |
| `ISystemTrayImpl` | `Win32SystemTray` | `LinuxSystemTray` |
| `IMenuImpl` | `Win32Menu` | `LinuxMenu` |
| `IKeyBindingManager` | `Win32KeyBindingManager` | `LinuxKeyBindingManager` |

**Server 模式**：`ServerPlatformApp` / `ServerWebviewWindow` 提供无 GUI 的 no-op 实现，用于容器化部署和测试。

### 4. 绑定系统

自动将 C# 公共方法暴露给前端 JavaScript。

```
C# 服务实例
    │
    ├── BindingManager.Add(instance)
    │       │
    │       └── 反射扫描公共方法
    │           │
    │           ├── 排除 ServiceName/ServiceStartup/ServiceShutdown
    │           ├── 排除特殊方法 (get_/set_/op_)
    │           └── 生成 FNV-1a 32位哈希 ID
    │
    └── 前端调用时
        │
        ├── 按全限定名或 ID 查找方法
        ├── JSON 反序列化参数
        ├── 注入 CancellationToken / ICommandContext
        └── 反射调用 + 解包 TargetInvocationException
```

**FNV-1a 哈希一致性**：绑定方法 ID 使用 FNV-1a 32 位哈希，与 Go 版本 `fnv.New32a()` 一致：

```csharp
const uint offsetBasis = 2166136261u;
const uint prime = 16777619u;
```

### 5. 命令调度系统

Minimal API 风格的命令注册和调度：

```csharp
// 注册命令
context.Commands.MapCommand("myapp.getData", (Func<int, string>)(id =>
{
    return $"数据 {id}";
}));

// 调度流程
DispatchAsync(request)
    │
    ├── 权限校验 (PermissionManager)
    ├── 中间件管道 (ICommandMiddleware[])
    │     ├── 中间件 1 → next → ...
    │     └── 中间件 N → next → 终端
    └── 终端处理器
        ├── 参数绑定 (ICommandContext / CancellationToken / IServiceProvider / JSON)
        ├── 反射调用
        └── 异步返回值处理 (Task<T>)
```

**特性**：
- 支持同步/异步方法
- 自动参数绑定（JSON → C# 类型）
- 特殊参数注入（`ICommandContext`、`CancellationToken`、`IServiceProvider`）
- 命令超时机制
- 中间件管道

### 6. 插件框架

借鉴 Tauri v2 的插件设计：

```csharp
public interface IPlugin
{
    string Name { get; }
    void ConfigureServices(IServiceCollection services);
    void Configure(IPluginContext context);
}
```

**插件上下文** (`IPluginContext`)：
- `Services` - DI 服务集合
- `Commands` - 命令注册表
- `Configuration` - 应用配置
- `LoggerFactory` - 日志工厂

**内置插件**（30 个）：

| 类别 | 插件 |
|------|------|
| 系统 | OsInfoPlugin, AppInfoPlugin, ClipboardPlugin, NotificationPlugin |
| 文件 | FileSystemPlugin, FsWatchPlugin, PathPlugin, UploadPlugin |
| 网络 | HttpPlugin, WebSocketPlugin, CookiePlugin, LocalhostPlugin |
| 窗口 | WindowStatePlugin, PositionerPlugin, DeepLinkPlugin |
| 数据 | StorePlugin, SqlPlugin, StrongholdPlugin |
| 系统 | GlobalShortcutPlugin, AutostartPlugin, PowerManagementPlugin |
| 其他 | LogPlugin, DialogPlugin, OpenerPlugin, ShellPlugin, ProcessPlugin |
| 更新 | UpdaterPlugin, FileAssociationPlugin, LocalizationPlugin, PersistedScopePlugin |

### 7. 传输层

前端与后端之间的通信机制：

| 传输方式 | 用途 | 实现 |
|----------|------|------|
| IPC | WebView ↔ C# | AssetServerTransport |
| WebSocket | 多窗口广播 | WebSocketTransport / WebSocketBroadcaster |
| HTTP | Server 模式 | HttpTransport |

### 8. 资源服务器

内置 HTTP 服务器提供静态文件服务，运行时 JS 通过 WebView2 的
`AddScriptToExecuteOnDocumentCreatedAsync` 在页面脚本执行前注入：

```
HTTP 请求 (http://wails.localhost/*)
    │
    ├── 中间件管道 (IMiddleware)
    │     ├── 日志
    │     ├── CORS
    │     └── 缓存
    │
    ├── 静态文件读取 (FileAssetServer / BundledAssetServer)
    │     └── /* → 前端静态资源（含 SPA 路由回退）
    │
    └── 响应

运行时 JS 注入（不经过 HTTP）：
    Win32WebviewWindow.InjectRuntimeJs()
        └── Application.GenerateRuntimeJs()
            └── RuntimeGenerator.Generate(options)
                └── AddScriptToExecuteOnDocumentCreatedAsync(js)
                    （在页面任何脚本执行前注入 window.wails API）
```

### 9. 安全与权限

借鉴 Tauri v2 的权限模型：

- **CSP** (`CspOptions`) - 内容安全策略
- **URL 白名单** (`UrlWhitelist`) - 允许的外部链接
- **IPC 来源验证** (`IpcOriginValidator`) - IPC 请求来源校验
- **能力声明** (`Capability`) - 权限粒度控制
- **权限管理器** (`PermissionManager`) - 运行时权限校验

## 设计模式

### 管理器模式

`Application` 委托给聚焦的管理器：

```
Application
    ├── WindowManager (窗口生命周期)
    ├── DialogManager (对话框)
    ├── ScreenManager (显示器)
    ├── EventProcessor (事件)
    └── BindingManager (绑定)
```

### 服务生命周期

```
IServiceStartup → 启动顺序（注册顺序）
IServiceShutdown → 关闭顺序（逆序）
```

### 工厂模式

`PlatformFactory` 根据操作系统创建对应的平台实现。

### 选项模式

使用 `IOptions<T>` 强类型配置：

```csharp
// appsettings.json → DesktopHostOptions
Services.AddOptions<DesktopHostOptions>()
    .Bind(Configuration.GetSection("Desktop"));
```

## 命名空间结构

```
Wails.Net.Application              # 核心应用
Wails.Net.Application.Bindings     # 绑定系统
Wails.Net.Application.Commands     # 命令调度
Wails.Net.Application.Events       # 事件系统
Wails.Net.Application.Hosting      # Generic Host 集成
Wails.Net.Application.Icons        # ICO 编解码
Wails.Net.Application.Managers     # 管理器
Wails.Net.Application.Menus       # 菜单系统
Wails.Net.Application.Options      # 配置选项
Wails.Net.Application.Platform     # 平台抽象
Wails.Net.Application.Plugins      # 插件框架
Wails.Net.Application.Screens      # 屏幕信息
Wails.Net.Application.Security     # 安全与权限
Wails.Net.Application.Services     # 内置服务
Wails.Net.Application.SystemTray   # 系统托盘
Wails.Net.Application.Transport    # 传输层
Wails.Net.Application.WebView     # WebView 抽象
Wails.Net.Application.Windows      # 窗口 API
Wails.Net.Application.Windows      # Windows 实现
Wails.Net.Application.Linux        # Linux 实现
Wails.Net.AssetServer             # 资源服务器
Wails.Net.Runtime.Js             # JS 运行时
Wails.Net.Generator              # 代码生成器
Wails.Net.Events                 # 事件类型
Wails.Net.Errors                 # 错误类型
```
