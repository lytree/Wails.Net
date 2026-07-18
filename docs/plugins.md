# Wails.Net 插件开发指南

本文档介绍如何开发和使用 Wails.Net 插件。Wails.Net 内置 **41 个插件**（37 桌面 + 4 移动端），覆盖系统、文件、网络、窗口、数据、更新、移动端等场景。

## 插件概念

插件是 Wails.Net 的扩展机制，借鉴 Tauri v2 的设计。每个插件可以：
- 注册 DI 服务
- 注册前端可调用的命令
- 订阅应用事件
- 提供配置选项

## 插件接口

```csharp
public interface IPlugin
{
    /// <summary>插件名称</summary>
    string Name { get; }

    /// <summary>注册 DI 服务</summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>配置插件（注册命令等）</summary>
    void Configure(IPluginContext context);
}
```

## 创建自定义插件

### 基本示例：计数器插件

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

public class CounterPlugin : IPlugin
{
    public string Name => "counter";

    public void ConfigureServices(IServiceCollection services)
    {
        // 注册计数器服务为单例
        services.AddSingleton<CounterService>();
    }

    public void Configure(IPluginContext context)
    {
        // 注册命令：counter.increment
        context.Commands.MapCommand("counter.increment", (Func<ICommandContext, int>)(ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            return counter.Increment();
        }));

        // 注册命令：counter.getValue
        context.Commands.MapCommand("counter.getValue", (Func<ICommandContext, int>)(ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            return counter.Value;
        }));
    }
}

public class CounterService
{
    private int _value;
    private readonly object _lock = new();

    public int Value { get { lock (_lock) return _value; } }

    public int Increment()
    {
        lock (_lock) { return ++_value; }
    }
}
```

### 注册插件

在 `Program.cs` 中注册：

```csharp
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 使用插件实例
builder.UsePlugin(new CounterPlugin());

// 或使用泛型方式（需要无参构造函数）
builder.UsePlugin<CounterPlugin>();

// 或从程序集自动发现
builder.UsePluginsFromAssembly();
```

### 前端调用

```javascript
// 调用插件命令
const value = await wails.call('counter.increment', []);
console.log('计数器值:', value);

const currentValue = await wails.call('counter.getValue', []);
```

## 命令注册方式

### 方式一：Lambda 表达式（推荐）

```csharp
context.Commands.MapCommand("myapp.getData", (Func<ICommandContext, string>)(ctx =>
{
    var service = ctx.Services.GetRequiredService<MyService>();
    return service.GetData();
}));
```

### 方式二：异步命令

```csharp
context.Commands.MapCommand("myapp.fetchData", (Func<ICommandContext, Task<string>>)(async ctx =>
{
    var service = ctx.Services.GetRequiredService<MyService>();
    return await service.FetchDataAsync(ctx.CancellationToken);
}));
```

### 方式三：带参数的命令

```csharp
context.Commands.MapCommand("myapp.findById", (Func<ICommandContext, int, Item?>)((ctx, id) =>
{
    var service = ctx.Services.GetRequiredService<MyService>();
    return service.FindById(id);
}));
```

### 方式四：使用属性标记

在服务方法上标记 `[Command]` 特性：

```csharp
public class MyService
{
    [Command("myapp.process")]
    public string ProcessData(string input)
    {
        return $"处理结果: {input}";
    }
}
```

## 特殊参数注入

命令方法可以声明以下特殊参数，框架会自动注入：

| 参数类型 | 说明 |
|----------|------|
| `ICommandContext` | 命令上下文（包含 Services、WindowId、CancellationToken） |
| `CancellationToken` | 取消令牌，支持超时取消 |
| `IServiceProvider` | DI 服务容器 |

```csharp
context.Commands.MapCommand("myapp.work", (Func<ICommandContext, CancellationToken, Task<string>>)(
    async (ctx, token) =>
    {
        var service = ctx.Services.GetRequiredService<MyService>();
        return await service.DoWorkAsync(token);
    }));
```

## 内置插件列表

Wails.Net 内置 41 个插件，按用途分类如下：

### 系统类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `OsInfoPlugin` | `osinfo.*` | 操作系统信息（版本、架构、主机名） |
| `AppInfoPlugin` | `appinfo.*` | 应用信息（名称、版本、路径） |
| `ClipboardPlugin` | `clipboard.*` | 剪贴板读写（文本、文件、图片） |
| `NotificationPlugin` | `notification.*` | 系统通知发送 |
| `DpiScalePlugin` | `dpi.*` | DPI 缩放查询与设置 |

### 文件类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `FileSystemPlugin` | `fs.*` | 文件读写（带路径遍历保护） |
| `FsWatchPlugin` | `fswatch.*` | 文件系统监听 |
| `PathPlugin` | `path.*` | 路径操作 |
| `UploadPlugin` | `upload.*` | 文件上传 |

### 网络类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `HttpPlugin` | `http.*` | HTTP 请求代理 |
| `WebSocketPlugin` | `websocket.*` | WebSocket 连接 |
| `CookiePlugin` | `cookie.*` | Cookie 管理 |
| `LocalhostPlugin` | `localhost.*` | 本地主机服务 |

### 窗口类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `WindowPlugin` | `window.*` | 当前窗口操作（P0：CSS 变量拖拽） |
| `WindowsPlugin` | `windows.*` | 多窗口管理（getAll/setTitle 等） |
| `WindowStatePlugin` | `windowstate.*` | 窗口状态持久化 |
| `PositionerPlugin` | `positioner.*` | 窗口定位（9 种方位） |
| `DeepLinkPlugin` | `deeplink.*` | 深度链接注册 |
| `FileAssociationPlugin` | `fileassoc.*` | 文件关联 |

### 数据类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `StorePlugin` | `store.*` | 键值存储（持久化） |
| `SqlPlugin` | `sql.*` | SQLite 数据库 |
| `StrongholdPlugin` | `stronghold.*` | 加密存储（AES-GCM） |
| `PersistedScopePlugin` | `scope.*` | 持久化文件范围（Tauri v2 对齐） |

### 应用类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `ApplicationPlugin` | `application.*` | 应用级操作（quit/hide/show 等） |
| `MenuPlugin` | `menu.*` | 应用菜单与上下文菜单 |
| `TrayPlugin` | `tray.*` | 系统托盘 |
| `ScreenPlugin` | `screen.*` | 屏幕查询（所有显示器） |
| `LogPlugin` | `log.*` | 日志记录（P1-3：与 `ILogger` 双向桥接） |

### 其他插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `DialogPlugin` | `dialog.*` | 原生对话框（打开/保存/消息框） |
| `OpenerPlugin` | `opener.*` | 安全打开 URL/文件（带白名单） |
| `ShellPlugin` | `shell.*` | 命令执行（带白名单） |
| `ProcessPlugin` | `process.*` | 进程管理 |
| `GlobalShortcutPlugin` | `shortcut.*` | 全局快捷键 |
| `AutostartPlugin` | `autostart.*` | 开机自启动 |
| `PowerManagementPlugin` | `power.*` | 电源管理（阻止休眠） |
| `LocalizationPlugin` | `i18n.*` | 国际化（语言切换） |

### 更新类插件

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `UpdaterPlugin` | `updater.*` | 自动更新（P1-8：多 Provider — Http/GitHub/GitLab/自定义） |

### 移动端插件（仅 Android）

| 插件 | 命令前缀 | 功能 |
|------|----------|------|
| `BiometricPlugin` | `biometric.*` | 生物识别（指纹/面部）— `checkAvailability` / `authenticate` |
| `NfcPlugin` | `nfc.*` | NFC 读写 — `read` / `write` / `cancel` |
| `BarcodeScannerPlugin` | `barcode-scanner.*` | 条码/二维码扫描 — `scan` / `cancel` |
| `HapticsPlugin` | `haptics.*` | 触觉反馈 — `vibrate` / `cancel` / `notification` |

> 移动端插件位于 `Wails.Net.Application.Plugins.Mobile` 命名空间，仅在 `net10.0-android36.0` 目标下可用。Windows/Linux 上调用会返回 `PlatformNotSupportedException`。

## P1 新能力

### 1. Logger 双向桥接（P1-3）

通过 `LogPlugin` + `UseBrowserConsoleLogReceiver`/`UseBrowserConsoleLogForwarder` 实现前后端日志双向打通：

```csharp
builder.UseBrowserConsoleLogReceiver();   // 前端 console.log → 后端 ILogger
builder.UseBrowserConsoleLogForwarder();   // 后端 ILogger → 前端 DevTools console
builder.UsePlugin<LogPlugin>();
```

### 2. 多 Provider Updater（P1-8）

`UpdaterPlugin` 背后的 `UpdaterService` 支持多 Provider 链式检查，首个成功返回的 Provider 结果生效：

```csharp
builder.Services.AddSingleton<UpdaterService>(sp =>
{
    var service = new UpdaterService { CurrentVersion = "1.0.0" };
    service.AddProvider(new GitHubUpdateProvider("owner/repo"));
    service.AddProvider(new HttpUpdateProvider("https://example.com/update.json"));
    return service;
});
builder.UsePlugin<UpdaterPlugin>();
```

内置 Provider：
- `HttpUpdateProvider` — 标准 HTTP JSON 协议
- `GitHubUpdateProvider` — GitHub Releases API
- `GitLabUpdateProvider` — GitLab Releases API
- 自定义 `IUpdateProvider` 实现

### 3. Service Route 挂载（P1-6）

通过 `app.RegisterService(handler, options)` 将 `IHttpServiceHandler` 直挂 AssetServer 路由，无需独立启动 ASP.NET Core 管道：

```csharp
app.RegisterService(new HealthHandler(), new ServiceOptions { Route = "/api/health" });
```

适用于健康检查、版本接口、轻量 API。

### 4. 三平台 BrowserManager（P1-2）

`IBrowserManager` 在三平台提供统一的"打开外部 URL"实现：

| 平台 | 实现 | 底层 API |
|------|------|---------|
| Windows | `WindowsBrowserManager` | `ShellExecute` |
| Linux | `LinuxBrowserManager` | `gtk_show_uri` |
| Android | `AndroidBrowserManager` | `Intent.ACTION_VIEW` |
| Server | `ServerBrowserManager` | no-op |

### 5. Event Hooks（P1-7）

通过 `ApplicationOptions` 暴露应用生命周期回调：

```csharp
app.Options.PostShutdown = () => { /* 所有清理完成 */ };
app.Options.ShouldQuit   = () => true;  // 返回 false 可阻止退出
```

### 6. Frameless 拖拽 CSS 变量（P1-5）

无需绑定监听，通过 CSS 变量即可启用原生拖拽区域：

```css
.title-bar { --wails-draggable: drag; }
.button    { --wails-draggable: no-drag; }
```

### 7. ContextMenu 行为对齐（P1-4）

通过 `app.Options.EnableDefaultContextMenu` 控制默认右键菜单，与 Wails v3 行为对齐。

## 使用内置插件

### 剪贴板

```csharp
builder.UsePlugin<ClipboardPlugin>();
```

```javascript
// 复制文本
await wails.call('clipboard.setText', ['Hello']);

// 读取文本
const text = await wails.call('clipboard.getText', []);

// 复制文件
await wails.call('clipboard.setFiles', [['/path/to/file1', '/path/to/file2']]);
```

### 通知

```csharp
builder.UsePlugin<NotificationPlugin>();
```

```javascript
// 发送通知
await wails.call('notification.show', [{
    title: '我的应用',
    body: '操作已完成！'
}]);

// 请求权限
const granted = await wails.call('notification.requestPermission', []);
```

### 对话框

```csharp
builder.UsePlugin<DialogPlugin>();
```

```javascript
// 打开文件
const filePath = await wails.call('dialog.openFile', [{
    title: '选择文件',
    filters: [{ name: '图片', extensions: ['png', 'jpg', 'jpeg'] }]
}]);

// 保存文件
const savePath = await wails.call('dialog.saveFile', [{
    title: '保存文件',
    defaultPath: 'untitled.txt'
}]);

// 消息框
await wails.call('dialog.message', [{
    title: '提示',
    body: '确定要删除吗？',
    type: 'warning'
}]);
```

### 键值存储

```csharp
builder.UsePlugin<StorePlugin>();
```

```javascript
// 设置值
await wails.call('store.set', ['username', '张三']);

// 获取值
const username = await wails.call('store.get', ['username']);

// 删除值
await wails.call('store.delete', ['username']);

// 获取所有键
const keys = await wails.call('store.keys', []);
```

### 日志

```csharp
builder.UsePlugin<LogPlugin>();
```

```javascript
await wails.call('log.info', ['应用启动']);
await wails.call('log.warn', ['磁盘空间不足']);
await wails.call('log.error', ['操作失败: ' + error.message]);
await wails.call('log.debug', ['调试信息: ' + JSON.stringify(data)]);
```

## 插件配置

插件可以通过 `IPluginContext.Configuration` 读取配置：

### appsettings.json

```json
{
  "Plugins": {
    "MyPlugin": {
      "MaxRetries": 3,
      "Timeout": "00:00:30"
    }
  }
}
```

### 插件中读取配置

```csharp
public void Configure(IPluginContext context)
{
    var config = context.Configuration.GetSection("Plugins:MyPlugin");
    var maxRetries = config.GetValue<int>("MaxRetries");
    var timeout = config.GetValue<TimeSpan>("Timeout");

    // 使用配置值注册命令...
}
```

### 强类型选项

```csharp
public class MyPluginOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public void ConfigureServices(IServiceCollection services)
{
    services.AddOptions<MyPluginOptions>()
        .Bind(Configuration.GetSection("Plugins:MyPlugin"));
}
```

## 插件生命周期

```
应用启动
    │
    ├── ConfigureServices()  ← 注册 DI 服务
    │
    ├── Configure()          ← 注册命令、事件订阅
    │
    ├── 应用运行
    │     │
    │     └── 命令调用 ←── 前端请求
    │
    └── 应用关闭
          │
          └── IDisposable.Dispose() ← 清理资源（如实现）
```

## 最佳实践

### 1. 线程安全

共享状态必须使用锁保护：

```csharp
public class CounterService
{
    private int _value;
    private readonly object _lock = new();

    public int Increment()
    {
        lock (_lock) { return ++_value; }
    }
}
```

或使用 `Interlocked`：

```csharp
private int _value;
public int Increment() => Interlocked.Increment(ref _value);
```

### 2. 命名规范

- 插件名：小写中划线分隔（`my-counter`）
- 命令名：`插件名.方法名`（`counter.increment`）

### 3. 异步优先

所有可能阻塞的命令应使用异步方法：

```csharp
context.Commands.MapCommand("myapp.fetch", (Func<ICommandContext, CancellationToken, Task<string>>)(
    async (ctx, token) =>
    {
        var service = ctx.Services.GetRequiredService<MyService>();
        return await service.FetchAsync(token);
    }));
```

### 4. 错误处理

在命令中抛出异常会被框架捕获并返回给前端：

```csharp
context.Commands.MapCommand("myapp.divide", (Func<int, int, int>)((a, b) =>
{
    if (b == 0)
        throw new ArgumentException("除数不能为零");

    return a / b;
}));
```

### 5. 文档注释

所有公共 API 必须有中文 XML 文档注释：

```csharp
/// <summary>
/// 计数器插件，提供增加、减少和重置功能。
/// </summary>
public class CounterPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "counter";
}
```
