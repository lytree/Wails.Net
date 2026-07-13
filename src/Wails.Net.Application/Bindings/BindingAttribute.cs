namespace Wails.Net.Application.Bindings;

/// <summary>
/// 标记方法暴露给前端 JS 调用。
/// 源代码生成器会扫描此特性，生成强类型调用代码，
/// 替代运行时反射（<see cref="System.Reflection.MethodInfo.Invoke(object?, object?[])"/>），
/// 提升性能并支持 AOT 裁剪。
/// </summary>
/// <remarks>
/// 使用方式：
/// <code>
/// public class GreetingService
/// {
///     [Binding]
///     public string Greet(string name) => $"Hello, {name}!";
///
///     [Binding(Name = "getTime")]
///     public Task&lt;DateTime&gt; GetCurrentTimeAsync(CancellationToken ct) => ...;
/// }
/// </code>
/// 若不指定 <see cref="Name"/>，使用方法名本身作为绑定名。
/// 与 <see cref="Commands.CommandAttribute"/> 不同，<see cref="BindingAttribute"/>
/// 自动使用 "ClassName.MethodName" 作为绑定名，适用于服务类。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BindingAttribute : Attribute
{
    /// <summary>
    /// 可选的自定义方法名。若不指定，使用方法名本身。
    /// 前端通过 <c>wails.call("ClassName.MethodName")</c> 或 <c>wails.call("Name")</c> 调用。
    /// </summary>
    public string? Name { get; set; }
}
