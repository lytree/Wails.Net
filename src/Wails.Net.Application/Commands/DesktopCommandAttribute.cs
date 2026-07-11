namespace Wails.Net.Application.Commands;

/// <summary>
/// 标记类为命令容器。
/// 类中的公共方法如果标记了 <see cref="CommandAttribute"/>，将自动注册为可调用命令。
/// 未标记 <see cref="CommandAttribute"/> 的公共非 void 方法也会自动注册，
/// 命令名为 "类名.方法名" 的小写形式。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DesktopCommandAttribute : Attribute
{
}
