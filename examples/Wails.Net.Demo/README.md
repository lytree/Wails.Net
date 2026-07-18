# Wails.Net Demo

Wails.Net 框架的示例应用，演示了绑定服务、自定义插件、窗口管理、**P1 阶段 8 项新能力**和前端交互等核心功能。

## 功能演示

| 功能 | 说明 |
|------|------|
| 问候服务 | 演示 C# 方法绑定到前端 JS 调用（同步/异步/带参数） |
| 待办事项 | 演示 CRUD 操作和复杂数据类型的绑定 |
| 计数器插件 | 演示自定义插件的开发和命令注册 |
| 系统信息 | 演示内置插件（剪贴板、通知）的使用 |
| **P1 新能力** | 演示 P1 阶段 8 项对齐能力（见下表） |

### P1 阶段新能力演示

| 能力 | Demo 中的演示 |
|------|--------------|
| P1-1 BrowserManager | 打开外部 URL（三平台统一 API） |
| P1-2 事件 senderWindowId | 后端日志中可见来源窗口 |
| P1-3 Logger 双向桥接 | 后端写日志 → 前端 DevTools console 可见 |
| P1-4 ContextMenu | 右键页面显示原生上下文菜单 |
| P1-5 Frameless 拖拽 | `--wails-drag-region` CSS 变量（Frameless=true 时） |
| P1-6 Service Route | `fetch('/api/health')` 和 `fetch('/api/version')` |
| P1-7 Event Hooks | 获取应用状态、触发 Shutdown（执行 PostShutdown） |
| P1-8 多 Provider Updater | 查看已注册 Provider、检查更新 |

## 项目结构

```
Wails.Net.Demo/
├── Program.cs              # 应用入口，配置和启动（含 P1 能力配置）
├── P1DemoHelpers.cs       # P1 演示辅助类（MockProvider、HttpHandlers）
├── Services/
│   ├── GreetingService.cs  # 问候服务（基本绑定示例）
│   ├── TodoService.cs      # 待办事项服务（CRUD 示例）
│   └── P1FeaturesService.cs # P1 新能力演示服务
├── Plugins/
│   └── MyCustomPlugin.cs   # 自定义插件示例（计数器）
├── frontend/
│   ├── index.html          # 前端页面（含 P1 新能力面板）
│   ├── app.js              # 前端逻辑（含 P1 面板交互）
│   └── styles.css          # 样式表
├── appsettings.json        # 应用配置
├── wails.json              # Wails.Net CLI 配置（构建/打包）
├── app.manifest            # Windows 应用清单
└── Wails.Net.Demo.csproj   # 项目文件
```

## 运行

```bash
# 从仓库根目录构建
dotnet build examples/Wails.Net.Demo/Wails.Net.Demo.csproj

# 运行 Demo
dotnet run --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj
```

## 使用 Wails.Net CLI 构建和打包

```bash
# 构建（Release 配置）
dotnet run --project src/Wails.Net.Cli -- build \
  --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj

# 打包为 ZIP
dotnet run --project src/Wails.Net.Cli -- pack \
  --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj \
  --format zip \
  --output dist/

# 自包含打包（推荐分发）
dotnet run --project src/Wails.Net.Cli -- pack \
  --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj \
  --runtime win-x64 \
  --self-contained \
  --format zip
```

详细构建打包流程参见 [构建与打包指南](../../docs/development/build-and-pack.md)。

## 关键概念

### 1. 绑定服务

后端的 C# 服务通过 `Application.RegisterBindings<T>()` 注册后，其公共方法自动暴露给前端：

```csharp
// 后端：定义服务
public class GreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}

// 后端：注册服务
app.RegisterBindings<GreetingService>();

// 前端：调用方法
const result = await wails.call('GreetingService.Greet', ['世界']);
```

### 2. 自定义插件

实现 `IPlugin` 接口，在 `Configure` 中注册命令：

```csharp
public class MyCustomPlugin : IPlugin
{
    public string Name => "my-counter";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<CounterService>();
    }

    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("counter.increment", (Func<ICommandContext, int>)(ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            return counter.Increment();
        }));
    }
}
```

### 3. Logger 双向桥接（P1-3）

```csharp
// 启用双向桥接
builder.UseBrowserConsoleLogReceiver();    // 前端 console.log → 后端 ILogger
builder.UseBrowserConsoleLogForwarder();   // 后端 ILogger → 前端 DevTools console
```

### 4. 多 Provider Updater（P1-8）

```csharp
var service = new UpdaterService { CurrentVersion = "1.0.0" };
service.AddProvider(new GitHubUpdateProvider(httpClient, "owner", "repo", token: "ghp_xxx"));
service.AddProvider(new HttpUpdateProvider(httpClient, "https://example.com/manifest.json"));
// 按注册顺序尝试，首个返回非 null 的胜出
```

### 5. Service Route 挂载（P1-6）

```csharp
app.RegisterService(new MyApiHandler(), new ServiceOptions { Route = "/api/myapi" });

// 前端可直接 fetch
const response = await fetch('/api/myapi');
```

### 6. Event Hooks（P1-7）

```csharp
builder.Configure(options =>
{
    options.PostShutdown = () => Console.WriteLine("应用已完全关闭");
    options.ShouldQuit = () => true; // 返回 false 可阻止退出
});
```

## 配置

通过 `appsettings.json` 配置应用：

```json
{
  "Wails": {
    "ApplicationName": "Wails.Net Demo",
    "SingleInstance": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

通过 `wails.json` 配置构建和打包：

```json
{
  "name": "Wails.Net.Demo",
  "version": "1.0.0",
  "bundle": {
    "identifier": "io.wailsnet.demo",
    "category": "Developer Tools"
  }
}
```
