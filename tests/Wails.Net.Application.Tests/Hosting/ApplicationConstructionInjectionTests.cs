using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// Application DI 构造注入测试（审计 §3.1 / R1-1）。
/// <para>
/// 验证通过 <see cref="DesktopApplicationBuilder"/> 走 DI 装配后，
/// <see cref="Wails.Net.Application.Application.Bindings"/> 与
/// <see cref="Wails.Net.Application.Application.Events"/> 均为 DI 容器注册的
/// 单例实例，消除之前 "Application 与 DI 各持一套" 的双对象图问题。
/// </para>
/// </summary>
[NotInParallel]
public sealed class ApplicationConstructionInjectionTests
{
    /// <summary>
    /// R1-1：DI 模式下 Application.Bindings 与 sp.GetRequiredService&lt;BindingManager&gt;()
    /// 返回的是同一实例。
    /// </summary>
    [Test]
    public async Task DI_Bindings_SameInstanceAsServiceProvider()
    {
        // 测试项目未引用 Wails.Net.Application.Windows 平台包，PlatformFactory 找不到委托。
        // 直接在 DI 注册 ServerPlatformApp 作为 IPlatformApp，避免走 UseAutoPlatform()。
        var options = new ApplicationOptions { Name = "R1-1-Test" };
        var builder = DesktopApplicationBuilder.CreateBuilder(Array.Empty<string>());
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<Wails.Net.Application.Platform.IPlatformApp>(sp =>
            new ServerPlatformApp(sp.GetRequiredService<ApplicationOptions>()));
        var host = builder.Build();

        var app = host.Services.GetRequiredService<Wails.Net.Application.Application>();
        var diBindings = host.Services.GetRequiredService<BindingManager>();
        var diEvents = host.Services.GetRequiredService<EventProcessor>();

        await Assert.That(app.Bindings).IsSameReferenceAs(diBindings);
        await Assert.That(app.Events).IsSameReferenceAs(diEvents);
    }

    /// <summary>
    /// R1-1：旧构造器（测试/遗留路径）仍可用，构造期局部创建 BindingManager/EventProcessor。
    /// </summary>
    [Test]
    public async Task LegacyConstructor_CreatesLocalInstances()
    {
        var app = new Wails.Net.Application.Application(new ApplicationOptions { Name = "legacy" });
        await Assert.That(app.Bindings).IsNotNull();
        await Assert.That(app.Events).IsNotNull();
        await Assert.That(app.Bindings.BoundMethods).IsNotNull();
    }

    /// <summary>
    /// 测试用回显服务。
    /// <para>
    /// 类名须与本程序集内其他绑定类区分：源生成器会为 [Binding] 方法注册
    /// "ClassName.MethodName" 短名别名，同名类会导致别名冲突（WNET004）并使该别名不再注册。
    /// </para>
    /// </summary>
    public sealed class ConstructionEchoService
    {
        [Binding]
        public string Echo(string input) => input;
    }
}
