using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Tests.Commands;

/// <summary>
/// <see cref="MapCommandExtensions"/> 的 <see cref="ICommandContext"/> 注入重载回归测试（TUnit）。
/// <para>
/// 对应 P1 缺陷修复：<c>Func&lt;ICommandContext, TResult&gt;</c> + <see cref="CommandSecurity"/> 的调用
/// 曾因缺少专用重载而被绑定到 <c>MapCommand&lt;T, TResult&gt;</c>，把上下文当作业务参数反序列化后传入
/// <c>null</c>，导致 mobile / camera / geolocation 等使用 <c>ctx.Services</c> 的插件在运行期抛
/// <see cref="NullReferenceException"/>。本测试确保上下文被正确注入且 <c>ctx.Services</c> 可用。
/// </para>
/// </summary>
[NotInParallel]
public sealed class MapCommandContextInjectionTests
{
    private static ICommandContext CreateContext(IServiceProvider services)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(services);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Test]
    public async Task MapCommand_ContextResult_WithSecurity_PassesNonNullContext()
    {
        // 安排：命令读取 ctx.Services —— 若上下文为 null 会抛 NullReferenceException
        var registry = new CommandRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        registry.MapCommand(
            "test.ctx",
            (Func<ICommandContext, string>)(ctx => ctx.Services is null ? "NO-SERVICES" : "OK"),
            CommandSecurity.Public());
        var ctx = CreateContext(services);

        // 操作
        var result = CommandTestHelper.Invoke(registry, "test.ctx", ctx);

        // 断言
        await Assert.That(result).IsEqualTo("OK");
    }

    [Test]
    public async Task MapCommand_ContextAsyncResult_WithSecurity_PassesNonNullContext()
    {
        // 安排
        var registry = new CommandRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        registry.MapCommand(
            "test.async",
            (Func<ICommandContext, Task<string>>)(ctx =>
                Task.FromResult(ctx.Services is null ? "NO-SERVICES" : "OK")),
            CommandSecurity.Require("test:allow-default"));
        var ctx = CreateContext(services);

        // 操作
        var task = (Task<string>)CommandTestHelper.Invoke(registry, "test.async", ctx)!;
        var result = await task;

        // 断言
        await Assert.That(result).IsEqualTo("OK");
    }

    [Test]
    public async Task MapCommand_ContextResult_WithSecurity_ReceivesSameContextInstance()
    {
        // 安排：验证注入的是调用方提供的上下文实例本身
        var registry = new CommandRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        ICommandContext? seen = null;
        registry.MapCommand(
            "test.same",
            (Func<ICommandContext, string>)(ctx =>
            {
                seen = ctx;
                return "ok";
            }),
            CommandSecurity.Public());
        var ctx = CreateContext(services);

        // 操作
        _ = CommandTestHelper.Invoke(registry, "test.same", ctx);

        // 断言
        await Assert.That(seen).IsSameReferenceAs(ctx);
    }
}
