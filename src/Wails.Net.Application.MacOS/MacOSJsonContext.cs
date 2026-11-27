using System.Text.Json.Serialization;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台 JSON 源生成上下文。
/// 覆盖单实例参数（<see cref="string"/> 数组）与字符串字面量这两类简单负载，
/// 避免走反射式 <see cref="System.Text.Json.JsonSerializer"/> 而被裁剪分析报 IL2026
/// （仓库统一约定，参见 <c>ManifestsJsonSerializerContext</c>）。
/// </summary>
/// <remarks>
/// 复合对象（如 <c>ResponseMessage</c>）的 <c>Result</c> 是动态
/// <c>Dictionary&lt;string, object?&gt;</c>，源生成无法覆盖，仍走反射路径并就地抑制 IL2026。
/// </remarks>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class MacOSJsonContext : JsonSerializerContext;
