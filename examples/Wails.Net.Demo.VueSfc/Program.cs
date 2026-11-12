// Demo: Wails.Net.Demo.VueSfc
// 目的：演示 Wails.Net 与「标准 Vue 3 单文件组件（SFC）」的集成。
// 与 Wails.Net.Demo.Vue（vue-jsx-vapor / TSX）相对，本示例全部使用 .vue 单文件组件 +
// <script setup lang="ts"> + 组合式函数（composables），覆盖：
//   1. 绑定方法调用（GreetingService / TodoService）
//   2. 自定义插件命令（MyCustomPlugin / counter.*）
//   3. 内置插件命令（clipboard / notification / dialog / osinfo / appinfo）
//   4. 双向事件（后端定时广播 vuesfc:tick，前端 emit vuesfc:ping 由后端回显 vuesfc:pong）

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;

using Wails.Net.Demo.VueSfc.Plugins;
using Wails.Net.Demo.VueSfc.Services;

using Wails.Net.Plugins.AppInfo;
using Wails.Net.Plugins.Clipboard;
using Wails.Net.Plugins.Window;
using Wails.Net.Plugins.Dialog;
using Wails.Net.Plugins.Log;
using Wails.Net.Plugins.Notification;
using Wails.Net.Plugins.OsInfo;

// 通过 DebugMode 统一判定当前模式（优先级：WAILS_DEBUG 环境变量 > --debug 参数 > .NET 环境变量）。
var isDebugMode = DebugMode.IsEnabled(args);

// 创建桌面应用构建器（使用 Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Vue SFC Demo";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 前端构建产物目录（pnpm build 后生成，由 Wails.Net.Sdk 的 WailsNetBuildFrontend 自动构建）
    options.Assets.RootPath = "frontend/dist";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务到 DI 容器（DI 是唯一注册点，[Binding] 方法由源生成器产出的强类型调用器处理）
builder.Services.AddSingleton<GreetingService>();
builder.Services.AddSingleton<TodoService>();

// 使用内置插件（前端「系统信息」面板调用其命令）
builder.UsePlugin<WindowPlugin>();   // 窗口命令 window.setTitle / minimize / maximize / centre
builder.UsePlugin<LogPlugin>();
builder.UsePlugin<ClipboardPlugin>();
builder.UsePlugin<DialogPlugin>();
builder.UsePlugin<NotificationPlugin>();
builder.UsePlugin<OsInfoPlugin>();
builder.UsePlugin<AppInfoPlugin>();

// 使用自定义插件（提供 counter.* 命令）
builder.UsePlugin<MyCustomPlugin>();

// 配置日志级别（Debug 模式输出更详细日志，Release 模式仅 Information 以上）
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);

// 使用平台工厂自动检测并注册平台实现（Windows/Linux/Android）
builder.UseWailsPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 设置 ApplicationOptions
app.Options.EnableDefaultContextMenu = true;
app.Options.DragAndDrop = true;

// 注册绑定服务（对应 ASP.NET Core 风格：DI 负责实例化，BindingManager 负责暴露给前端）
app.RegisterBindings<GreetingService>();
app.RegisterBindings<TodoService>();

// 注册 CounterService 实例：其方法标记 [Command]，由源生成器生成强类型调用器，
// 需要把实例交给 BindingManager，调用时才能解析到目标对象。
app.RegisterService(desktopApp.Services.GetRequiredService<CounterService>());

// ---- 事件演示：后端订阅前端事件，收到后回显 ----
app.Events.On("vuesfc:ping", evt =>
{
    app.Events.Emit("vuesfc:pong", new
    {
        echo = evt.Data,
        receivedAt = DateTime.Now.ToString("HH:mm:ss.fff"),
    });
});

// 心跳定时器（在窗口创建后启动，关闭时释放）
Timer? tickTimer = null;
var tickSequence = 0;

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = isDebugMode
            ? "Wails.Net Vue SFC Demo (Debug) - <script setup> 示例"
            : "Wails.Net Vue SFC Demo - <script setup> 示例",
        Width = 1200,
        Height = 800,
        MinWidth = 900,
        MinHeight = 600,
        Resizable = true,
        Maximisable = true,
        Minimisable = true,
        Fullscreen = false,
    });

    // 每 2 秒广播一次心跳事件，供前端「事件通信」面板订阅演示
    tickTimer = new Timer(
        _ => app.Events.Emit("vuesfc:tick", new
        {
            seq = Interlocked.Increment(ref tickSequence),
            time = DateTime.Now.ToString("HH:mm:ss"),
        }),
        state: null,
        dueTime: TimeSpan.FromSeconds(2),
        period: TimeSpan.FromSeconds(2));

    // Debug 模式：窗口创建后自动打开 DevTools（延迟等待 WebView2 初始化完成）
    if (isDebugMode)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                mainWindow.OpenDevTools();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Vue SFC Demo] DevTools 打开失败：{ex.Message}");
            }
        });
    }
};

// 退出时释放定时器，避免关闭过程中继续广播事件
app.OnShutdown(() =>
{
    tickTimer?.Dispose();
    tickTimer = null;
});

// 构建并运行应用
try
{
    await desktopApp.RunAsync();
}
finally
{
    tickTimer?.Dispose();
}
