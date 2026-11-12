using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wails.Net.Application.Security;

/// <summary>
/// 能力文件 JSON 源生成序列化上下文（NativeAOT 兼容）。
/// <para>
/// <see cref="CapabilityFileLoader"/> 从 <c>capabilities/*.json</c> 反序列化 <see cref="Capability"/>。
/// 反射式的 <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions)"/> 带
/// <c>RequiresUnreferencedCode</c> / <c>RequiresDynamicCode</c> 注解，ILC 在 NativeAOT 裁剪发布时
/// 会对可达调用点报 IL2026 / IL3050（CLI 的 <c>PublishAot=true</c> 即因此失败）。
/// 本上下文为其生成编译期强类型元数据，与 <c>ManifestsJsonSerializerContext</c> 同一思路。
/// </para>
/// <para>
/// 选项与能力文件的手写习惯保持一致：属性名大小写不敏感、允许注释与尾随逗号。
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Capability))]
public sealed partial class CapabilityJsonSerializerContext : JsonSerializerContext
{
}
