# Wails.Net 快速入门

本文档将帮助你从零开始使用 Wails.Net 构建桌面应用。

## 环境要求

| 组件 | 版本 | 说明 |
|------|------|------|
| .NET SDK | 10.0+ | 必需 |
| WebView2 Runtime | 最新版 | Windows 10/11 已预装 |
| GTK4 + WebKitGTK 6.0 | 系统包 | 仅 Linux 需要 |
| .NET Android 工作负载 | `android` 36.1.43 | 仅 Android 需要 |
| Android SDK Platform | API Level 24+ | 仅 Android 需要 |

### 安装 .NET 10 SDK

从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) 下载并安装。

### 安装 Android 工作负载（可选）

```bash
# 安装 .NET Android 工作负载
dotnet workload install android

# 验证
dotnet workload list
```

### 验证安装

```bash
dotnet --version
# 应输出 10.x.x
```

## 创建第一个项目

### 方式一：使用 CLI 脚手架

```bash
# 安装 CLI 工具
dotnet tool install -g Wails.Net.Cli

# 创建新项目
wails.net new MyApp --template blank

# 进入项目目录
cd MyApp

# 运行
dotnet run
```

### 方式二：手动创建

#### 1. 创建控制台项目

```bash
dotnet new console -n MyApp
cd MyApp
```

#### 2. 添加 Wails.Net 引用

编辑 `MyApp.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="path/to/Wails.Net.Application.csproj" />
    <ProjectReference Include="path/to/Wails.Net.Application.Windows.csproj" />
  </ItemGroup>
</Project>
```

> 如需构建 Android 版本，请将 `TargetFramework` 改为 `net10.0-android36.0`，并设置 `SupportedOSPlatformVersion=24`。

#### 3. 编写入口代码

```csharp
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins.BuiltIn;

// 创建桌面应用构建器（Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "MyApp";
    options.SingleInstance = true;

    // 配置静态资源根路径
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务到 DI 容器（公共方法自动暴露给前端）
builder.Services.AddSingleton<GreetingService>();

// 使用内置插件（按需）
builder.UsePlugin<WindowPlugin>();
builder.UsePlugin<ClipboardPlugin>();
builder.UsePlugin<DialogPlugin>();
builder.UsePlugin<NotificationPlugin>();

// 自动检测并注册平台实现（Windows/Linux/Android）
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 设置 ApplicationOptions 中 DesktopHostOptions 未覆盖的选项
app.Options.EnableDefaultContextMenu = true;
app.Options.DragAndDrop = true;

// 从 DI 容器获取绑定服务并注册到 BindingManager（替代已废弃的 RegisterService(instance)）
app.RegisterBindings<GreetingService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "我的第一个 Wails.Net 应用",
        Width = 800,
        Height = 600,
    });
};

// 构建并运行应用
await desktopApp.RunAsync();

public class GreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}
```

#### 4. 创建前端页面

在项目中创建 `frontend/index.html`：

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>MyApp</title>
</head>
<body>
    <h1>Hello Wails.Net</h1>
    <input type="text" id="name" placeholder="输入名字">
    <button onclick="greet()">打招呼</button>
    <p id="result"></p>

    <script>
        async function greet() {
            const name = document.getElementById('name').value;
            const result = await wails.call('GreetingService.Greet', [name]);
            document.getElementById('result').textContent = result;
        }
    </script>
</body>
</html>
```

#### 5. 运行

```bash
dotnet run
```

## 项目结构

一个典型的 Wails.Net 应用结构：

```
MyApp/
├── Program.cs              # 应用入口
├── Services/               # 后端服务
│   └── GreetingService.cs
├── Plugins/                # 自定义插件
│   └── MyPlugin.cs
├── frontend/               # 前端资源
│   ├── index.html
│   ├── app.js
│   └── styles.css
├── appsettings.json        # 配置文件
└── MyApp.csproj            # 项目文件
```

## 核心概念

### 1. DesktopApplicationBuilder

入口点，使用 Generic Host 模式构建应用（与 ASP.NET Core 一致）：

```csharp
var builder = DesktopApplicationBuilder.CreateBuilder(args);
```

### 2. 配置选项

`DesktopHostOptions` 通过 `Configure` 设置，`ApplicationOptions` 在 `Build` 之后通过 `app.Options` 设置：

```csharp
// DesktopHostOptions（Build 之前）
builder.Configure(options =>
{
    options.ApplicationName = "MyApp";
    options.SingleInstance = true;
    options.Window.Frameless = false;
    options.Assets.RootPath = "frontend";
});

// ApplicationOptions（Build 之后）
var app = builder.Build().Application;
app.Options.EnableDefaultContextMenu = true;
app.Options.DragAndDrop = true;

// P1-7：Event Hooks
app.Options.PostShutdown = () => Console.WriteLine("所有清理完成");
app.Options.ShouldQuit = () => true;
```

### 3. 平台选择

推荐使用 `UseAutoPlatform` 自动检测（Windows 上注册 `WindowsPlatformApp`，Linux 上注册 `LinuxPlatformApp`，Android 上注册 `AndroidPlatformApp`）：

```csharp
// 自动检测（推荐）
builder.UseAutoPlatform();

// 或显式指定（用于测试或特殊场景）
// 注意：显式 API 已废弃，建议仅在自动检测不适用时使用
```

### 4. 绑定服务

通过 `RegisterBindings<T>()` 注册 DI 中的服务，其公共方法将自动暴露给前端（由源生成器在编译期生成注册表，禁止反射）：

```csharp
// 定义服务
public class CalculatorService
{
    public int Add(int a, int b) => a + b;
    public async Task<string> GetDataAsync() => await FetchData();
    public List<Item> GetItems() => _items;
}

// 注册到 DI
builder.Services.AddSingleton<CalculatorService>();

// 注册到 BindingManager（必须，替代已废弃的 RegisterService(instance)）
app.RegisterBindings<CalculatorService>();
```

```javascript
// 前端调用
const sum = await wails.call('CalculatorService.Add', [10, 20]);
const data = await wails.call('CalculatorService.GetDataAsync', []);
const items = await wails.call('CalculatorService.GetItems', []);
```

### 5. 窗口管理

```csharp
// 创建窗口（在 OnAfterStart 回调中）
app.Options.OnAfterStart = () =>
{
    var window = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Title = "主窗口",
        Width = 1024,
        Height = 768,
    });
};

// 窗口操作（C# 端）
window.SetTitle("新标题");
window.SetSize(800, 600);
window.Maximise();
window.Minimise();
window.Close();
```

```javascript
// 窗口操作（前端，需启用 WindowPlugin）
await wails.window.setTitle("新标题");
await wails.window.setSize(800, 600);
await wails.window.maximize();
```

### 6. 事件系统

```csharp
// 后端发布事件（自动跨窗口广播，携带 senderWindowId）
app.Events.Emit("my-event", new { message = "Hello" });

// 后端订阅事件
app.Events.On("frontend-event", (evt) =>
{
    Console.WriteLine($"收到前端事件: {evt.Name}");
});
```

```javascript
// 前端发布事件
wails.events.emit('frontend-event', { data: 'hello' });

// 前端订阅事件
wails.events.on('my-event', (data) => {
    console.log('收到后端事件:', data);
});
```

## P1 新能力示例

### 1. Logger 双向桥接（P1-3）

前端 `console.log` 自动转发到后端 `ILogger`，后端日志也会出现在前端 DevTools：

```csharp
// 前端 console.log → 后端 ILogger
builder.UseBrowserConsoleLogReceiver();

// 后端 ILogger → 前端 DevTools console
builder.UseBrowserConsoleLogForwarder();
```

### 2. Service Route 挂载（P1-6）

将轻量 HTTP 路由直挂 AssetServer，无需独立启动 ASP.NET Core 管道：

```csharp
// 实现 IHttpServiceHandler
public class HealthHandler : IHttpServiceHandler
{
    public Task HandleAsync(HttpContext ctx)
    {
        return ctx.Response.WriteAsync("ok");
    }
}

// 挂载到 /api/health
app.RegisterService(new HealthHandler(), new ServiceOptions { Route = "/api/health" });
```

### 3. 多 Provider Updater（P1-8）

支持 Http / GitHub / GitLab / 自定义多 Provider 更新检查：

```csharp
builder.Services.AddSingleton<UpdaterService>(sp =>
{
    var service = new UpdaterService { CurrentVersion = "1.0.0" };

    // 内置 Provider
    service.AddProvider(new GitHubUpdateProvider("owner/repo"));
    service.AddProvider(new GitLabUpdateProvider(projectId: 123));
    service.AddProvider(new HttpUpdateProvider("https://example.com/update.json"));

    return service;
});

builder.UsePlugin<UpdaterPlugin>();
```

### 4. Event Hooks（P1-7）

```csharp
app.Options.PostShutdown = () =>
{
    Console.WriteLine("所有清理完成");
};

app.Options.ShouldQuit = () =>
{
    // 返回 false 可阻止退出
    return true;
};
```

### 5. Frameless 拖拽（P1-5）

无需绑定监听，通过 CSS 变量即可启用原生拖拽区域：

```css
.title-bar {
    /* 启用窗口拖拽 */
    --wails-draggable: drag;
}

.no-drag-region {
    /* 在拖拽区域内禁用某些元素 */
    --wails-draggable: no-drag;
}
```

## 下一步

- [架构详解](architecture.md) - 了解框架的分层设计
- [插件开发指南](plugins.md) - 学习如何创建自定义插件
- [前端 API 参考](api/frontend-api.md) - 完整的 JavaScript API 文档
- [构建与打包](development/build-and-pack.md) - 三平台构建与分发指南
- [功能对比](comparison-with-tauri2-wails3.md) - 与 Tauri v2 / Wails v3 的对比
- [Demo 项目](../examples/Wails.Net.Demo/) - 完整的示例应用
