using System.Text.Json.Nodes;

namespace Wails.Net.Cli.Serialization;

/// <summary>
/// <see cref="JsonArray"/> 追加节点的辅助方法（NativeAOT / 裁剪友好）。
/// <para>
/// .NET 10 的 <see cref="JsonArray"/> 新增了泛型重载 <c>Add&lt;T&gt;(T value)</c>，该重载带有
/// <c>RequiresUnreferencedCode</c> 与 <c>RequiresDynamicCode</c> 注解——它对非
/// <see cref="JsonNode"/> 的实参走 <c>JsonValue.Create</c>，需要在运行期生成序列化代码。
/// 由于泛型重载在重载决议中优于 <c>Add(JsonNode?)</c>（精确匹配胜过隐式引用转换），
/// 直接写 <c>array.Add(jsonObject)</c> 会被判定为命中有注解的重载，在
/// <c>PublishAot=true</c> 下产生 IL2026 / IL3050 并因 <c>TreatWarningsAsErrors</c> 构建失败
/// （守护目标见 <c>.github/workflows/nativeaot.yml</c>）。
/// </para>
/// <para>
/// 本方法把实参静态类型收窄为 <see cref="JsonNode"/>，确保命中无注解的 <c>Add(JsonNode?)</c>
/// 重载；元素本身已是 JSON 节点，不存在运行期反射序列化的需求。
/// </para>
/// </summary>
internal static class JsonArrayExtensions
{
    /// <summary>
    /// 以 <see cref="JsonNode"/> 静态类型向数组末尾追加节点。
    /// </summary>
    /// <param name="array">目标数组。</param>
    /// <param name="node">要追加的 JSON 节点。</param>
    public static void AddNode(this JsonArray array, JsonNode? node) => array.Add(node);
}
