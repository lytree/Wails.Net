using System.Text.Json;
using System.Text.Json.Serialization;
using Wails.Net.Application.Security;
using Wails.Net.Cli.Config;

namespace Wails.Net.Cli.Serialization;

/// <summary>
/// CLI JSON 源生成序列化上下文（NativeAOT 兼容，审计 P2-4 落地）。
/// <para>
/// 替代 <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions)"/>
/// 的运行时反射路径：源生成器在编译期为 <see cref="ProjectConfig"/> 与
/// <see cref="Capability"/> 生成强类型序列化代码，使 CLI 可在
/// <c>PublishAot=true</c>（IL 裁剪 + AOT 编译）下发布，守护 AGENTS.md §3.4 的
/// "生产代码禁止运行时反射" 约束在 JSON 序列化层面的收益。
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectConfig))]
[JsonSerializable(typeof(Capability))]
internal sealed partial class CliJsonSerializerContext : JsonSerializerContext
{
}
