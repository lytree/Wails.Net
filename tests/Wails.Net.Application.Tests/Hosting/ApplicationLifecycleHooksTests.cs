using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="ApplicationLifecycleHooks"/> 的单元测试（TUnit）。
/// 对应 RH-HOST-02：关闭任务与启动前 hook 从 <see cref="Application"/> 抽出后，
/// 顺序与异常语义必须保持不变（关闭任务逐个隔离，启动 hook 异常向上抛出）。
/// </summary>
[NotInParallel]
public sealed class ApplicationLifecycleHooksTests
{
    [Test]
    public async Task RunBeforeStartHooks_ExecutesInRegistrationOrder_AndPassesApplication()
    {
        var hooks = new ApplicationLifecycleHooks();
        var order = new List<string>();
        var app = new Application(new ApplicationOptions());

        hooks.AddBeforeStartHook(a => order.Add($"first:{ReferenceEquals(a, app)}"));
        hooks.AddBeforeStartHook(_ => order.Add("second"));

        hooks.RunBeforeStartHooks(app);

        await Assert.That(string.Join(",", order)).IsEqualTo("first:True,second");
        await Assert.That(hooks.BeforeStartHookCount).IsEqualTo(2);
    }

    [Test]
    public async Task RunShutdownTasks_RunsAllEvenWhenOneThrows()
    {
        var hooks = new ApplicationLifecycleHooks();
        var executed = new List<string>();

        hooks.AddShutdownTask(() => executed.Add("a"));
        hooks.AddShutdownTask(() => throw new InvalidOperationException("boom"));
        hooks.AddShutdownTask(() => executed.Add("c"));

        hooks.RunShutdownTasks();

        await Assert.That(string.Join(",", executed)).IsEqualTo("a,c");
        await Assert.That(hooks.ShutdownTaskCount).IsEqualTo(3);
    }

    [Test]
    public async Task AddShutdownTasks_MergesOptionsList()
    {
        var hooks = new ApplicationLifecycleHooks();
        var count = 0;

        hooks.AddShutdownTasks([() => count++, () => count++]);
        hooks.AddShutdownTasks(null);

        hooks.RunShutdownTasks();

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(hooks.ShutdownTaskCount).IsEqualTo(2);
    }

    [Test]
    public async Task NullArguments_Throw()
    {
        var hooks = new ApplicationLifecycleHooks();

        await Assert.That(() => hooks.AddShutdownTask(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => hooks.AddBeforeStartHook(null!)).Throws<ArgumentNullException>();
    }
}
