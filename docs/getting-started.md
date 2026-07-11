# Wails.Net 快速入门

本文档将帮助你从零开始使用 Wails.Net 构建桌面应用。

## 环境要求

| 组件 | 版本 | 说明 |
|------|------|------|
| .NET SDK | 10.0+ | 必需 |
| WebView2 Runtime | 最新版 | Windows 10/11 已预装 |
| GTK4 + WebKitGTK 6.0 | 系统包 | 仅 Linux 需要 |

### 安装 .NET 10 SDK

从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) 下载并安装。

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

#### 3. 编写入口代码

```csharp
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;

var builder = DesktopApplicationBuilder.CreateBuilder(args);

builder.Configure(options =>
{
    options.ApplicationName = "MyApp";
});

builder.UsePlatform<WindowsPlatformApp>();
builder.ConfigurePlatform(platformApp =>
{
    platformApp.OnAfterStart = () =>
    {
        var app = platformApp.Application;
        app.CreateWebviewWindow(new WebviewWindowOptions
        {
            Title = "我的第一个 Wails.Net 应用",
            Width = 800,
            Height = 600,
        });

        // 注册绑定服务
        app.RegisterService(new GreetingService());
    };
});

var app = builder.Build();
await app.RunAsync();

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

入口点，使用 Generic Host 模式构建应用：

```csharp
var builder = DesktopApplicationBuilder.CreateBuilder(args);
```

### 2. 配置选项

通过 `Configure` 方法设置应用选项：

```csharp
builder.Configure(options =>
{
    options.ApplicationName = "MyApp";
    options.SingleInstance = true;          // 单实例模式
    options.Frameless = false;               // 无边框窗口
    options.EnableDefaultContextMenu = true; // 右键菜单
    options.DragAndDrop = true;              // 文件拖放
});
```

### 3. 平台选择

```csharp
// Windows 平台
builder.UsePlatform<WindowsPlatformApp>();

// Linux 平台
builder.UsePlatform<LinuxPlatformApp>();
```

### 4. 绑定服务

C# 服务的公共方法自动暴露给前端：

```csharp
// 定义服务
public class CalculatorService
{
    public int Add(int a, int b) => a + b;
    public async Task<string> GetDataAsync() => await FetchData();
    public List<Item> GetItems() => _items;
}

// 注册服务
app.RegisterService(new CalculatorService());

// 前端调用
const sum = await wails.call('CalculatorService.Add', [10, 20]);
const data = await wails.call('CalculatorService.GetDataAsync', []);
const items = await wails.call('CalculatorService.GetItems', []);
```

### 5. 窗口管理

```csharp
// 创建窗口
var window = app.CreateWebviewWindow(new WebviewWindowOptions
{
    Title = "主窗口",
    Width = 1024,
    Height = 768,
});

// 窗口操作
window.SetTitle("新标题");
window.SetSize(800, 600);
window.Maximise();
window.Minimise();
window.Close();
```

### 6. 事件系统

```csharp
// 后端发布事件
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

## 下一步

- [架构详解](architecture.md) - 了解框架的分层设计
- [插件开发指南](plugins.md) - 学习如何创建自定义插件
- [Demo 项目](../examples/Wails.Net.Demo/) - 完整的示例应用
