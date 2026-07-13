using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Demo.Vue.Plugins;

/// <summary>
/// 自定义插件示例，演示如何创建自己的插件。
/// 该插件提供计数器功能：增加、减少、重置、获取当前值。
/// </summary>
public class MyCustomPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "my-counter";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<CounterService>();
    }

    /// <summary>
    /// 配置插件，注册计数器相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand<ICommandContext, int>("counter.increment", ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            return counter.Increment();
        });

        context.Commands.MapCommand<ICommandContext, int>("counter.decrement", ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            return counter.Decrement();
        });

        context.Commands.MapCommand<ICommandContext, bool>("counter.reset", ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            counter.Reset();
            return true;
        });

        context.Commands.MapCommand<ICommandContext, int>("counter.getValue", ctx =>
        {
            var counter = ctx.Services.GetRequiredService<CounterService>();
            return counter.Value;
        });
    }
}

/// <summary>
/// 计数器服务，由 <see cref="MyCustomPlugin"/> 注册到 DI 容器。
/// </summary>
public class CounterService
{
    private int _value;
    private readonly object _lock = new();

    /// <summary>
    /// 获取当前计数值。
    /// </summary>
    public int Value
    {
        get
        {
            lock (_lock)
            {
                return _value;
            }
        }
    }

    /// <summary>
    /// 增加计数。
    /// </summary>
    /// <returns>增加后的值。</returns>
    public int Increment()
    {
        lock (_lock)
        {
            return ++_value;
        }
    }

    /// <summary>
    /// 减少计数。
    /// </summary>
    /// <returns>减少后的值。</returns>
    public int Decrement()
    {
        lock (_lock)
        {
            return --_value;
        }
    }

    /// <summary>
    /// 重置计数。
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _value = 0;
        }
    }
}
