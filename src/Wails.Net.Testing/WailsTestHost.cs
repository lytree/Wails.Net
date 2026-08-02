using System.Text.Json;
using Wails.Net.Application;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Transport;
using Wails.Net.Errors;
using Wails.Net.Testing.Platform;
using Wails.Net.Testing.Recording;

// Application 类型（Wails.Net.Application.Application）与命名空间 Wails.Net.Application 末段同名，
// 用别名 App 消除歧义，使本文件中类型引用指向具体的 Application 类。
using App = Wails.Net.Application.Application;

namespace Wails.Net.Testing;

/// <summary>
/// 无头测试宿主门面，对标 Tauri v2 的 <c>tauri::test::mock_builder()</c> 与 <c>get_ipc_response()</c>。
/// <para>
/// 在没有任何 GUI 的 CI 环境中，通过 <see cref="MockPlatformApp"/> 驱动完整的应用生命周期，
/// 并以"前端调用"的视角执行绑定方法、插件命令、窗口操作并读取结构化响应：
/// <list type="bullet">
/// <item>经完整 IPC 管线（<see cref="Application.HandleMessageFromFrontend"/> → MessageProcessor → BindingManager / CommandDispatcher）调用绑定与命令，验证禁反射架构的端到端行为；</item>
/// <item>可断言平台级调用与状态（<see cref="MockPlatform"/>）、窗口内存态（<see cref="CreateWindow"/> 返回的 <see cref="MockWebviewWindow"/>）、剪贴板往返（<see cref="Clipboard"/>）；</item>
/// <item>构建后无需调用 <see cref="Application.Run"/>（避免阻塞在 Mock 主循环上），即可直接调用 <see cref="InvokeAsync{T}"/>。</item>
/// </list>
/// </para>
/// </summary>
public sealed class WailsTestHost : IDisposable, IAsyncDisposable
{
    private readonly DesktopApplication _desktopApp;
    private readonly CallRecorder _recorder;
    private bool _disposed;

    /// <summary>
    /// 由 <see cref="WailsTestHostBuilder.Build"/> 构造。
    /// </summary>
    /// <param name="desktopApp">已构建并注入 Mock 平台的桌面应用。</param>
    /// <param name="mockPlatform">Mock 平台应用实例。</param>
    /// <param name="clipboard">Mock 剪贴板实例（若启用剪贴板接线），可为 null。</param>
    /// <param name="recorder">跨平台与剪贴板共享的调用记录器。</param>
    internal WailsTestHost(
        DesktopApplication desktopApp,
        MockPlatformApp mockPlatform,
        MockClipboard? clipboard,
        CallRecorder recorder)
    {
        _desktopApp = desktopApp;
        _recorder = recorder;
        Application = desktopApp.Application;
        MockPlatform = mockPlatform;
        Clipboard = clipboard;
    }

    /// <summary>
    /// 底层 <see cref="App"/> 实例，已注入 Mock 平台并完成 DI 初始化。
    /// 可直接调用其 <c>RegisterService</c> / <c>RegisterBindings&lt;T&gt;()</c> / 事件 API 等。
    /// </summary>
    public App Application { get; }

    /// <summary>
    /// DI 服务容器。
    /// </summary>
    public IServiceProvider Services => _desktopApp.Services;

    /// <summary>
    /// Mock 平台应用实例，用于断言平台级调用与状态（如 <see cref="MockPlatformApp.Calls"/>）。
    /// </summary>
    public MockPlatformApp MockPlatform { get; }

    /// <summary>
    /// Mock 剪贴板实例（仅当 <see cref="WailsTestHostBuilder.EnableClipboard"/> 为 true 时非 null），
    /// 用于断言剪贴板往返与调用记录。
    /// </summary>
    public MockClipboard? Clipboard { get; }

    /// <summary>
    /// 跨平台与剪贴板共享的调用记录器，便于统一断言"调用了什么 / 调了几次 / 参数是什么"。
    /// </summary>
    public CallRecorder Recorder => _recorder;

    // ---------------------------------------------------------------------
    // 绑定 / 命令调用
    // ---------------------------------------------------------------------

    /// <summary>
    /// 以"前端 call 消息"的视角调用指定的绑定方法或插件命令，返回强类型结果。
    /// 调用失败（error 字段非空）时抛出 <see cref="WailsInvocationException"/>。
    /// </summary>
    /// <typeparam name="T">期望的结果类型。</typeparam>
    /// <param name="methodName">绑定方法全名或插件命令名（如 <c>GreetingService.Hello</c>）。</param>
    /// <param name="args">调用参数，将按 <see cref="JsonOptions.DefaultSerializerOptions"/> 序列化后写入消息载荷。</param>
    /// <returns>反序列化后的强类型结果。</returns>
    public async Task<T> InvokeAsync<T>(string methodName, params object?[] args)
        => (await InvokeResponseAsync(methodName, args, null).ConfigureAwait(false)).GetResult<T>();

    /// <summary>
    /// 同上，但显式指定发起调用的来源窗口 ID（用于窗口级命令与权限隔离断言）。
    /// <para>
    /// 注意 <paramref name="args"/> 必须为<b>显式数组</b>（非 <c>params</c> 展开），
    /// 否则数值类型的实参会与 windowId 重载产生歧义而被误解析为窗口 ID（详见下方说明）。
    /// 若需无窗口上下文的便捷调用，请使用 <see cref="InvokeAsync{T}(string, object?[])"/>。
    /// </para>
    /// </summary>
    /// <typeparam name="T">期望的结果类型。</typeparam>
    /// <param name="methodName">绑定方法全名或插件命令名。</param>
    /// <param name="args">调用参数（显式数组）。</param>
    /// <param name="windowId">来源窗口 ID；为 null 表示不携带窗口上下文。</param>
    /// <returns>反序列化后的强类型结果。</returns>
    public async Task<T> InvokeAsync<T>(string methodName, object?[] args, uint? windowId = null)
        => (await InvokeResponseAsync(methodName, args, windowId).ConfigureAwait(false)).GetResult<T>();

    /// <summary>
    /// 以"前端 call 消息"的视角调用指定的绑定方法或插件命令，返回原始结果对象（不反序列化）。
    /// 调用失败时抛出 <see cref="WailsInvocationException"/>。
    /// </summary>
    /// <param name="methodName">绑定方法全名或插件命令名。</param>
    /// <param name="args">调用参数。</param>
    /// <returns>响应中的 <c>result</c> 原始对象（可能为 <see cref="JsonElement"/> 或 CLR 对象）。</returns>
    public async Task<object?> InvokeAsync(string methodName, params object?[] args)
        => (await InvokeResponseAsync(methodName, args, null).ConfigureAwait(false)).RawResult;

    /// <summary>
    /// 同上，但显式指定来源窗口 ID。<paramref name="args"/> 必须为显式数组（非 <c>params</c> 展开）。
    /// </summary>
    public async Task<object?> InvokeAsync(string methodName, object?[] args, uint? windowId = null)
        => (await InvokeResponseAsync(methodName, args, windowId).ConfigureAwait(false)).RawResult;

    /// <summary>
    /// 结构化调用：构建一条前端 call JSON 消息，经完整 IPC 管线处理并返回 <see cref="WailsInvokeResponse"/>。
    /// <para>
    /// 与 <see cref="InvokeAsync{T}"/> 不同，调用失败（<c>error</c> 字段非空）不会抛异常，
    /// 由调用方通过 <see cref="WailsInvokeResponse.IsSuccess"/> 判断，便于断言错误路径
    /// （如绑定未找到、参数类型错误、运行时异常、命令拒绝等）。
    /// </para>
    /// </summary>
    /// <param name="methodName">绑定方法全名或插件命令名。</param>
    /// <param name="args">调用参数。</param>
    /// <param name="windowId">来源窗口 ID；为 null 表示不携带窗口上下文。</param>
    /// <returns>结构化调用响应。</returns>
    public async Task<WailsInvokeResponse> InvokeResponseAsync(string methodName, object?[] args, uint? windowId = null)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(args);

        var callId = Guid.NewGuid().ToString("N");

        var argElements = new JsonElement[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            argElements[i] = JsonSerializer.SerializeToElement(args[i], JsonOptions.DefaultSerializerOptions);
        }

        var message = new Message
        {
            Id = callId,
            Type = MessageProcessor.MessageTypes.Call,
            WindowId = windowId,
            Payload = JsonSerializer.SerializeToElement(
                new CallPayload { Name = methodName, Args = argElements },
                JsonOptions.DefaultSerializerOptions)
        };

        var json = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
        var response = await Application.HandleMessageFromFrontend(json, windowId).ConfigureAwait(false);

        if (response is null)
        {
            throw new WailsInvocationException(
                new CallErrorInfo("IPC 管线未返回响应（消息解析失败）", null, CallErrorKind.RuntimeError.ToString()),
                callId);
        }

        return WailsInvokeResponse.From(response, callId);
    }

    // ---------------------------------------------------------------------
    // 注册绑定 / 服务
    // ---------------------------------------------------------------------

    /// <summary>
    /// 将服务实例注册到绑定管理器，使其公共方法可作为绑定被前端调用。
    /// 等价于 <see cref="Application.RegisterService(object)"/>，需在 <see cref="InvokeAsync{T}"/> 之前调用。
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    public void RegisterService(object service) => Application.RegisterService(service);

    /// <summary>
    /// 从 DI 容器获取指定类型的服务实例并注册到绑定管理器。
    /// 等价于 <see cref="Application.RegisterBindings{T}"/>。
    /// </summary>
    /// <typeparam name="T">已注册到 DI 的服务类型。</typeparam>
    /// <returns>从 DI 容器获取的服务实例。</returns>
    public T RegisterBindings<T>() where T : class => Application.RegisterBindings<T>();

    // ---------------------------------------------------------------------
    // 窗口
    // ---------------------------------------------------------------------

    /// <summary>
    /// 创建一个 Mock 窗口并返回其 <see cref="MockWebviewWindow"/> 以便断言内存态。
    /// 走完整创建链路：<see cref="Application.CreateWebviewWindow"/> → <see cref="MockPlatformApp.CreateWebviewWindow"/> → <see cref="MockWebviewWindow"/>。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>新创建的 Mock 窗口，可读回标题、尺寸、可见性、JS 注入等全部状态。</returns>
    public MockWebviewWindow CreateWindow(WebviewWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Application.CreateWebviewWindow(options);
        if (MockPlatform.LastWindow is { } window)
        {
            return window;
        }

        throw new InvalidOperationException("窗口已创建但无法从 MockPlatform 取到 MockWebviewWindow。");
    }

    /// <summary>
    /// 以名称与标题创建一个默认尺寸的 Mock 窗口。
    /// </summary>
    /// <param name="name">窗口名称（对应前端窗口标识）。</param>
    /// <param name="title">窗口标题。</param>
    /// <returns>新创建的 Mock 窗口。</returns>
    public MockWebviewWindow CreateWindow(string name = "", string title = "Wails.Net")
        => CreateWindow(new WebviewWindowOptions { Name = name, Title = title });

    // ---------------------------------------------------------------------
    // 事件
    // ---------------------------------------------------------------------

    /// <summary>
    /// 经完整 IPC 管线发布一个事件（等效于前端 emit）。
    /// 用于验证事件广播链路（事件处理器订阅者能收到该事件）。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    /// <param name="senderWindowId">发送窗口 ID，可为 null。</param>
    public async Task EmitEventAsync(string eventName, object? data = null, uint? senderWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(eventName);

        var message = new Message
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = MessageProcessor.MessageTypes.Event,
            WindowId = senderWindowId,
            Payload = JsonSerializer.SerializeToElement(
                new EventPayload { Name = eventName, Data = data, SenderWindowID = senderWindowId },
                JsonOptions.DefaultSerializerOptions)
        };

        var json = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
        await Application.HandleMessageFromFrontend(json, senderWindowId).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------
    // 释放
    // ---------------------------------------------------------------------

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeCore();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
    }

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            MockPlatform.Dispose();
        }
        catch
        {
            // 释放窗口时的异常不应中断清理流程
        }

        try
        {
            MockPlatformRegistrar.Reset();
        }
        catch
        {
            // 重置全局注册器状态失败不应中断清理
        }
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            MockPlatform.Dispose();
        }
        catch
        {
            // 释放窗口时的异常不应中断清理流程
        }

        try
        {
            await _desktopApp.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // 释放宿主时的异常不应中断清理流程
        }

        try
        {
            MockPlatformRegistrar.Reset();
        }
        catch
        {
            // 重置全局注册器状态失败不应中断清理
        }
    }
}
