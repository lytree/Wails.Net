namespace Wails.Net.Application.Commands;

/// <summary>
/// 标记方法为可调用的命令。
/// 需配合 <see cref="DesktopCommandAttribute"/> 一起使用，或通过 <see cref="CommandRegistry"/> 手动注册。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CommandAttribute : Attribute
{
    /// <summary>
    /// 命令名称，前端通过此名称调用命令。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 构造命令特性。
    /// </summary>
    /// <param name="name">命令名称。</param>
    public CommandAttribute(string name)
    {
        Name = name;
    }
}
