using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Binding.Services;

/// <summary>
/// 用户信息记录，演示复杂对象返回。
/// </summary>
/// <param name="Id">用户 ID。</param>
/// <param name="Name">用户姓名。</param>
/// <param name="Email">用户邮箱。</param>
/// <param name="CreatedAt">账户创建时间。</param>
public sealed record UserInfo(int Id, string Name, string Email, DateTime CreatedAt);

/// <summary>
/// 绑定服务，演示 Wails.Net 绑定系统的各类方法签名。
/// 所有公共方法通过 <see cref="BindingAttribute"/> 标记，由源代码生成器暴露给前端。
/// </summary>
public sealed class BindingService
{
    /// <summary>
    /// 同步方法绑定：生成问候语。
    /// </summary>
    /// <param name="name">姓名。</param>
    /// <returns>问候字符串。</returns>
    [Binding]
    public string Greet(string name) => $"你好，{name}！欢迎使用 Wails.Net 绑定系统。";

    /// <summary>
    /// 异步方法绑定：获取当前时间。
    /// </summary>
    /// <returns>格式化的时间字符串。</returns>
    [Binding]
    public async Task<string> GetCurrentTimeAsync()
    {
        await Task.Delay(100);
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 重载方法绑定（整数版本）：计算两个整数之和。
    /// </summary>
    /// <param name="a">第一个整数。</param>
    /// <param name="b">第二个整数。</param>
    /// <returns>整数和。</returns>
    [Binding]
    public int Add(int a, int b) => a + b;

    /// <summary>
    /// 重载方法绑定（浮点数版本）：计算两个浮点数之和。
    /// </summary>
    /// <param name="a">第一个浮点数。</param>
    /// <param name="b">第二个浮点数。</param>
    /// <returns>浮点数和。</returns>
    [Binding]
    public double Add(double a, double b) => a + b;

    /// <summary>
    /// 复杂对象返回：根据 ID 获取用户信息。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <returns>用户信息记录。</returns>
    [Binding]
    public UserInfo GetUser(int id) =>
        new(id, $"用户{id}", $"user{id}@example.com", DateTime.UtcNow.AddDays(-id));

    /// <summary>
    /// 集合返回：获取字符串列表。
    /// </summary>
    /// <returns>字符串列表。</returns>
    [Binding]
    public List<string> GetItems() =>
        ["苹果", "香蕉", "橙子", "葡萄", "西瓜"];

    /// <summary>
    /// 异常处理：抛出异常供前端 catch 捕获。
    /// </summary>
    [Binding]
    public void ThrowError() => throw new InvalidOperationException("这是一个演示异常，用于测试前端错误处理。");

    /// <summary>
    /// CancellationToken 异步：模拟可取消的长任务。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务完成后的结果字符串。</returns>
    [Binding]
    public async Task<string> LongTask(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);
        }
        return "长任务已完成";
    }
}
