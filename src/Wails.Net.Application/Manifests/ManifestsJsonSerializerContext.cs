using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wails.Net.Application.Manifests;

/// <summary>
/// 命令/能力清单 JSON 源生成序列化上下文（NativeAOT 兼容，审计 P2-4 落地）。
/// <para>
/// 为 <see cref="CommandsManifest"/> 与 <see cref="CapabilitiesManifest"/> 及其条目
/// 生成编译期强类型序列化代码，替代 <see cref="JsonSerializer"/> 的运行时反射路径
/// （IL2026/IL3050），使清单生成/消费链路（ManifestGenerator、CLI generate）可在
/// <c>PublishAot=true</c> 下发布。属性名使用 camelCase，与
/// <see cref="ManifestGenerator"/> 的输出格式一致。
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CommandsManifest))]
[JsonSerializable(typeof(CommandManifestEntry))]
[JsonSerializable(typeof(CapabilitiesManifest))]
[JsonSerializable(typeof(CapabilityManifestEntry))]
public sealed partial class ManifestsJsonSerializerContext : JsonSerializerContext
{
}
