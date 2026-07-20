using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Android.Services;

/// <summary>
/// 问候服务，演示基本的绑定方法。
/// 所有公共方法将自动暴露给前端 JavaScript 调用。
/// 对应桌面版 Wails.Net.Demo 的 GreetingService。
/// </summary>
public class GreetingService
{
    /// <summary>
    /// 生成问候语。
    /// </summary>
    /// <param name="name">姓名。</param>
    /// <returns>问候字符串。</returns>
    [Binding]
    public string Greet(string name)
    {
        return $"你好，{name}！欢迎使用 Wails.Net (Android)";
    }

    /// <summary>
    /// 异步获取当前时间。
    /// </summary>
    /// <returns>格式化的时间字符串。</returns>
    [Binding]
    public async Task<string> GetCurrentTimeAsync()
    {
        await Task.Delay(100); // 模拟异步操作
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 计算两个数字之和。
    /// </summary>
    /// <param name="a">第一个数字。</param>
    /// <param name="b">第二个数字。</param>
    /// <returns>和。</returns>
    [Binding]
    public int Add(int a, int b)
    {
        return a + b;
    }

    /// <summary>
    /// 获取设备信息。
    /// </summary>
    /// <returns>包含应用和环境信息的字典。</returns>
    [Binding]
    public Dictionary<string, string> GetDeviceInfo()
    {
        return new Dictionary<string, string>
        {
            ["framework"] = "Wails.Net",
            ["runtime"] = Environment.Version.ToString(),
            ["osVersion"] = Environment.OSVersion.ToString(),
            ["machineName"] = Environment.MachineName,
            ["processorCount"] = Environment.ProcessorCount.ToString(),
        };
    }
}
