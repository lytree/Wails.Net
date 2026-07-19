using System.Text.Json;
using System.Threading.Tasks;

namespace Wails.Net.Application.Commands;

/// <summary>
/// 统一的命令调用器委托，由 <see cref="MapCommandExtensions"/> 在编译期通过强类型泛型重载构建闭包，
/// 替代运行时 <c>MethodInfo.Invoke</c> 反射调用，遵循 AGENTS.md §3.4 禁令。
/// </summary>
/// <param name="instance">命令所属实例。MapCommand 闭包路径下为 null（已捕获目标）。</param>
/// <param name="parameters">前端传入的 JSON 参数（整个 payload 或单参数 JsonElement）。</param>
/// <param name="ctx">命令上下文，提供 CancellationToken、Services 等。可为 null。</param>
/// <returns>
/// 异步返回的方法结果：
/// <list type="bullet">
/// <item>同步方法：用 <see cref="Task.FromResult{TResult}(TResult)"/> 包装；</item>
/// <item>异步方法：闭包内部 <c>await</c>，将 <c>Task&lt;T&gt;</c> 的 Result 装箱为 <see cref="object"/>；</item>
/// <item>无返回值方法：返回 <c>null</c>。</item>
/// </list>
/// 调用方仅需 <c>await</c> 此委托返回值，无需运行时反射提取 <c>Task.Result</c>，遵循 AGENTS.md §3.4 禁令。
/// </returns>
public delegate Task<object?> CompiledCommandInvoker(object? instance, JsonElement parameters, ICommandContext? ctx);
