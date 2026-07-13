using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;

namespace Company.AppName.Services;

/// <summary>
/// 示例绑定服务。
/// 公共方法将自动通过源代码生成器暴露给前端 JavaScript。
/// </summary>
public sealed class GreetingService
{
    private int _counter;

    /// <summary>
    /// 问候方法，返回问候字符串。
    /// 对应前端调用：await wails.GreetingService.Greet("World");
    /// </summary>
    [Binding]
    public string Greet(string name) => $"Hello, {name}!";

    /// <summary>
    /// 增加计数器并返回当前值。
    /// </summary>
    [Binding]
    public int Increment()
    {
        _counter++;
        return _counter;
    }

    /// <summary>
    /// 减少计数器并返回当前值。
    /// </summary>
    [Binding]
    public int Decrement()
    {
        _counter--;
        return _counter;
    }

    /// <summary>
    /// 获取当前计数器值。
    /// </summary>
    [Binding]
    public int GetCount() => _counter;
}
