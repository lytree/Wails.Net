using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Demo.Vue.Plugins;

/// <summary>
/// 自定义插件示例，演示如何创建自己的插件。
/// 该插件提供计数器功能：增加、减少、重置、获取当前值。
/// 计数器方法通过 [Command] 特性标记，由源代码生成器生成强类型调用器，
/// 不使用运行时反射。
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
    /// 配置插件。
    /// 计数器命令通过 [Command] 特性标记，由源代码生成器处理，无需在此手动注册。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 计数器命令已通过 [Command] 特性标记到 CounterService 的方法上，
        // 由源代码生成器自动生成强类型调用器，无需通过 MapCommand 手动注册。
    }
}

/// <summary>
/// 计数器服务，由 <see cref="MyCustomPlugin"/> 注册到 DI 容器。
/// 方法标记 [Command] 特性，由源代码生成器生成调用器，前端通过命令名调用。
/// </summary>
public class CounterService
{
    private int _value;
    private readonly object _lock = new();

    /// <summary>
    /// 获取当前计数值。
    /// </summary>
    [Command("counter.getValue")]
    public int GetValue()
    {
        lock (_lock)
        {
            return _value;
        }
    }

    /// <summary>
    /// 增加计数。
    /// </summary>
    /// <returns>增加后的值。</returns>
    [Command("counter.increment")]
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
    [Command("counter.decrement")]
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
    [Command("counter.reset")]
    public void Reset()
    {
        lock (_lock)
        {
            _value = 0;
        }
    }
}
