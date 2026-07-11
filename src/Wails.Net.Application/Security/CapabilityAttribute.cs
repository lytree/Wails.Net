namespace Wails.Net.Application.Security;

/// <summary>
/// 标记命令所需的能力。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireCapabilityAttribute : Attribute
{
    /// <summary>所需能力标识</summary>
    public string Capability { get; }

    /// <summary>
    /// 构造能力要求特性。
    /// </summary>
    /// <param name="capability">所需能力标识。</param>
    public RequireCapabilityAttribute(string capability) => Capability = capability;
}
