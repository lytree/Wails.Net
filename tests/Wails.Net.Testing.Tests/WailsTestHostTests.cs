using System.Linq;
using System.Threading;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Testing;
using Wails.Net.Testing.Platform;

namespace Wails.Net.Testing.Tests;

/// <summary>
/// <see cref="WailsTestHost"/> 门面的端到端验证（任务 A2）。
/// <para>
/// 这些测试经完整禁反射 IPC 管线（<c>HandleMessageFromFrontend</c> → <c>MessageProcessor</c> →
/// <c>BindingManager</c> 源生成器调用器）调用 [Binding] 方法，验证：
/// <list type="bullet">
/// <item>绑定方法经源生成器强类型调用器正确执行并返回结果；</item>
/// <item>未知绑定 / 抛出异常分别走错误响应与 <see cref="WailsInvocationException"/> 路径；</item>
/// <item>Mock 剪贴板可往返并写入调用记录；</item>
/// <item>窗口经完整创建链路返回内存态 <see cref="MockWebviewWindow"/>；</item>
/// <item>事件经完整管线广播并被订阅者收到。</item>
/// </list>
/// 全程无需任何 GUI 环境，可在 CI 中直接运行。
/// </para>
/// </summary>
[NotInParallel]
public sealed class WailsTestHostTests
{
    /// <summary>
    /// 测试用绑定服务。方法标记 [Binding]，由源生成器在编译期生成强类型调用器（零反射）。
    /// 必须为 public 以便源生成器生成调用器代码。
    /// </summary>
    public sealed class SampleBindingService
    {
        /// <summary>简单字符串绑定。</summary>
        [Binding]
        public string Greet(string name) => $"Hello, {name}!";

        /// <summary>多参数整型绑定。</summary>
        [Binding]
        public int Add(int a, int b) => a + b;

        /// <summary>异步绑定，CancellationToken 由调用器从运行时注入（前端不传）。</summary>
        [Binding]
        public Task<int> SlowDoubleAsync(int x, CancellationToken cancellationToken)
            => Task.FromResult(x * 2);

        /// <summary>总会抛出异常的绑定，用于验证错误路径。</summary>
        [Binding]
        public string ThrowError() => throw new InvalidOperationException("boom");
    }

    /// <summary>绑定方法全限定名前缀（Namespace.ClassName）。</summary>
    private const string ServiceName = "Wails.Net.Testing.Tests.SampleBindingService";

    /// <summary>
    /// 经完整 IPC 管线按全名调用绑定方法，返回强类型结果。
    /// </summary>
    [Test]
    public async Task InvokeAsync_ByFullName_ReturnsBindingResult()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();
        host.RegisterService(new SampleBindingService());

        var result = await host.InvokeAsync<string>($"{ServiceName}.Greet", "World");

        await Assert.That(result).IsEqualTo("Hello, World!");
    }

    /// <summary>
    /// 多参数整型绑定：JSON 参数按默认选项反序列化后由源生成器调用器执行。
    /// </summary>
    [Test]
    public async Task InvokeAsync_IntArgs_ComputesSum()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();
        host.RegisterService(new SampleBindingService());

        var sum = await host.InvokeAsync<int>($"{ServiceName}.Add", 2, 3);

        await Assert.That(sum).IsEqualTo(5);
    }

    /// <summary>
    /// 异步绑定：业务参数（21）经管线传入，CancellationToken 由调用器运行时注入，
    /// 验证禁反射架构下异步 + CancellationToken 绑定可正常执行。
    /// </summary>
    [Test]
    public async Task InvokeAsync_AsyncMethod_WithCancellationToken_PassesThrough()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();
        host.RegisterService(new SampleBindingService());

        var doubled = await host.InvokeAsync<int>($"{ServiceName}.SlowDoubleAsync", 21);

        await Assert.That(doubled).IsEqualTo(42);
    }

    /// <summary>
    /// 调用不存在的绑定方法：结构化响应应标记失败，且错误类型为 ReferenceError（不抛异常）。
    /// </summary>
    [Test]
    public async Task InvokeResponseAsync_UnknownBinding_ReturnsReferenceError()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();

        var response = await host.InvokeResponseAsync($"{ServiceName}.NoSuchMethod", []);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Error).IsNotNull();
        await Assert.That(response.Error!.Kind).Contains("ReferenceError");
    }

    /// <summary>
    /// 绑定方法抛出异常：<see cref="WailsTestHost.InvokeAsync{T}"/> 应包装为
    /// <see cref="WailsInvocationException"/> 抛出（错误路径断言）。
    /// </summary>
    [Test]
    public async Task InvokeAsync_BindingThrows_WrapsInWailsInvocationException()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();
        host.RegisterService(new SampleBindingService());

        // 非泛型 InvokeAsync 仅返回 RawResult（不抛异常）；需经泛型 InvokeAsync<T> 的
        // GetResult<T>() 路径将 error 响应包装为 WailsInvocationException 抛出。
        await Assert.That(async () => await host.InvokeAsync<string>($"{ServiceName}.ThrowError"))
            .Throws<WailsInvocationException>();
    }

    /// <summary>
    /// Mock 剪贴板：SetText → GetText 往返一致，且调用记入共享记录器。
    /// </summary>
    [Test]
    public async Task Clipboard_SetText_RoundTripsAndIsRecorded()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();
        await Assert.That(host.Clipboard).IsNotNull();

        host.Clipboard!.SetText("clipboard-value");
        var text = host.Clipboard.GetText();

        await Assert.That(text).IsEqualTo("clipboard-value");
        await Assert.That(host.Recorder.CountOf("SetText")).IsEqualTo(1);
    }

    /// <summary>
    /// 创建窗口经完整链路（Application.CreateWebviewWindow → MockPlatformApp.CreateWebviewWindow →
    /// MockWebviewWindow）返回内存态窗口，可断言标题等状态，且平台调用被记录。
    /// </summary>
    [Test]
    public async Task CreateWindow_ReturnsMockWindow_WithExpectedState()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();

        var window = host.CreateWindow("main", "My Window");

        await Assert.That(window.Title).IsEqualTo("My Window");
        await Assert.That(window.Id).IsEqualTo(1u);
        await Assert.That(host.MockPlatform.Calls.Any(c => c.Member == "CreateWebviewWindow")).IsTrue();
    }

    /// <summary>
    /// 经完整 IPC 管线发布事件，订阅者能收到该事件（事件广播链路验证）。
    /// </summary>
    [Test]
    public async Task EmitEventAsync_DeliveredToSubscriber()
    {
        await using var host = WailsTestHostBuilder.Create("WailsTestHostTests").Build();

        CustomEvent? received = null;
        host.Application.Events.On("wails.test.event", e => received = e);

        await host.EmitEventAsync("wails.test.event", new { Hello = "World" });

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.Name).IsEqualTo("wails.test.event");
    }
}
