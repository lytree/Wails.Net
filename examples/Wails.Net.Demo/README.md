# Wails.Net Demo

Wails.Net 框架的示例应用，演示了绑定服务、自定义插件、窗口管理和前端交互等核心功能。

## 功能演示

| 功能 | 说明 |
|------|------|
| 问候服务 | 演示 C# 方法绑定到前端 JS 调用（同步/异步/带参数） |
| 待办事项 | 演示 CRUD 操作和复杂数据类型的绑定 |
| 计数器插件 | 演示自定义插件的开发和命令注册 |
| 系统信息 | 演示内置插件（剪贴板、通知）的使用 |

## 项目结构

```
Wails.Net.Demo/
├── Program.cs              # 应用入口，配置和启动
├── Services/
│   ├── GreetingService.cs  # 问候服务（基本绑定示例）
│   └── TodoService.cs      # 待办事项服务（CRUD 示例）
├── Plugins/
│   └── MyCustomPlugin.cs   # 自定义插件示例（计数器）
├── frontend/
│   ├── index.html          # 前端页面
│   ├── app.js              # 前端逻辑
│   └── styles.css          # 样式表
├── appsettings.json        # 应用配置
├── app.manifest            # Windows 应用清单
└── Wails.Net.Demo.csproj   # 项目文件
```

## 运行

```bash
# 从仓库根目录运行
dotnet build examples/Wails.Net.Demo/Wails.Net.Demo.csproj
dotnet run --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj
```

## 关键概念

### 1. 绑定服务

后端的 C# 服务通过 `Application.RegisterService()` 注册后，其公共方法自动暴露给前端：

```csharp
// 后端：定义服务
public class GreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}

// 后端：注册服务
app.RegisterService(new GreetingService());

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

### 3. 内置插件

框架提供了 30 个内置插件，通过 `UsePlugin<T>()` 注册：

```csharp
builder.UsePlugin<ClipboardPlugin>();      // 剪贴板
builder.UsePlugin<NotificationPlugin>();   // 通知
builder.UsePlugin<DialogPlugin>();         // 对话框
builder.UsePlugin<LogPlugin>();             // 日志
builder.UsePlugin<StorePlugin>();          // 键值存储
```

## 配置

通过 `appsettings.json` 配置应用：

```json
{
  "Desktop": {
    "ApplicationName": "Wails.Net Demo",
    "SingleInstance": true,
    "Window": {
      "Frameless": false
    }
  }
}
```

或通过代码配置：

```csharp
builder.Configure(options =>
{
    options.ApplicationName = "My App";
    options.SingleInstance = true;
    options.Frameless = true;
});
```
