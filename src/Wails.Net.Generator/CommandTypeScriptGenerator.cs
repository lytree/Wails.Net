using System.Text;
using System.Text.Json;
using Wails.Net.Application.Manifests;

namespace Wails.Net.Generator;

/// <summary>
/// 命令 TypeScript wrapper 生成器（审计 P2-1）。
/// <para>
/// 消费 <see cref="CommandsManifest"/>（由 <see cref="ManifestGenerator"/> 从
/// <see cref="Wails.Net.Application.Commands.CommandRegistry"/> 生成），输出前端调用封装
/// <c>commands.ts</c>：
/// <list type="bullet">
/// <item>每个命令生成一个强类型调用函数（基于 <c>@wails-net/runtime</c> 的 <c>wails.call</c>）；</item>
/// <item>参数类型取自 manifest 的 <c>parameters</c>（TS 类型声明，由命令签名元数据 P2-1 映射）；</item>
/// <item>返回值统一 <c>Promise&lt;unknown&gt;</c>（命令返回值类型暂不在 manifest 中，后续可扩展）。</item>
/// </list>
/// 以命令 manifest 为唯一来源，消除前端 runtime API 与后端命令注册的漂移。
/// </para>
/// </summary>
public class CommandTypeScriptGenerator
{
    /// <summary>
    /// 生成命令 TypeScript 调用封装文件内容（commands.ts）。
    /// </summary>
    /// <param name="manifest">命令清单（由 <see cref="ManifestGenerator.GenerateCommandsManifest"/> 生成）。</param>
    /// <returns>TypeScript 封装文件内容。</returns>
    public string Generate(CommandsManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var sb = new StringBuilder();

        // 文件头注释
        sb.AppendLine("// 自动生成的 Wails.Net 命令调用封装（commands.ts）");
        sb.AppendLine("// 请勿手动修改，此文件由 commands.manifest.json 生成（ManifestGenerator）");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine($"// 命令数量: {manifest.Commands.Count}");
        sb.AppendLine();
        sb.AppendLine("import { wails } from '@wails-net/runtime';");
        sb.AppendLine();

        if (manifest.Commands.Count == 0)
        {
            sb.AppendLine("// 未发现命令");
            return sb.ToString();
        }

        foreach (var command in manifest.Commands.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var fnName = ToFunctionName(command.Name);
            var parameters = command.Parameters.ToArray(); // "arg0: string"
            var argsDecl = string.Join(", ", parameters);
            var argsCall = parameters.Length == 0
                ? "[]"
                : $"[{string.Join(", ", command.Parameters.Select(p => p[..p.IndexOf(':')].Trim()))}]";

            sb.AppendLine($"export function {fnName}({argsDecl}): Promise<unknown> {{");
            sb.AppendLine($"  return wails.call('{command.Name}', {argsCall});");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将命令名转换为 TypeScript 函数名。
    /// <para>
    /// 规则：按 <c>.</c> 分段，首段原样，后续段首字母大写后拼接。
    /// 例如 <c>fs.readFile</c> → <c>fsReadFile</c>、<c>window.setTitle</c> → <c>windowSetTitle</c>。
    /// </para>
    /// </summary>
    /// <param name="commandName">命令名称（如 <c>fs.readFile</c>）。</param>
    /// <returns>合法的 TypeScript 标识符。</returns>
    public static string ToFunctionName(string commandName)
    {
        ArgumentNullException.ThrowIfNull(commandName);

        var segments = commandName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return "command";
        }

        var sb = new StringBuilder(segments[0]);
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(segment[0]));
            sb.Append(segment, 1, segment.Length - 1);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成命令清单 JSON 文件名（<c>commands.manifest.json</c>）。
    /// 引用 <see cref="ManifestGenerator.ManifestFileName"/> 保证前后端一致。
    /// </summary>
    public const string ManifestFileName = ManifestGenerator.ManifestFileName;

    /// <summary>
    /// 生成命令 TypeScript 封装文件名（<c>commands.ts</c>）。
    /// </summary>
    public const string TypeScriptFileName = "commands.ts";

    /// <summary>
    /// 从 JSON 文件加载命令清单（P2-1 收尾：CLI 消费已落盘的 manifest）。
    /// 兼容 <see cref="ManifestGenerator.GenerateCommandsManifestJson"/> 输出的 camelCase 格式。
    /// 使用源生成序列化器（NativeAOT 兼容，P2-4，避免 IL2026/IL3050）。
    /// </summary>
    /// <param name="manifestPath">commands.manifest.json 文件路径。</param>
    /// <returns>反序列化后的命令清单。</returns>
    /// <exception cref="FileNotFoundException">文件不存在时抛出。</exception>
    /// <exception cref="JsonException">JSON 解析失败时抛出。</exception>
    public static CommandsManifest LoadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(manifestPath);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"命令清单文件不存在: {manifestPath}");
        }

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize(json, ManifestsJsonSerializerContext.Default.CommandsManifest)
            ?? throw new JsonException($"命令清单解析失败: {manifestPath}");
    }

    /// <summary>
    /// 将命令清单生成 TypeScript 封装并写入磁盘（P2-1 收尾）。
    /// 输出文件名为 <see cref="TypeScriptFileName"/>（commands.ts）。
    /// </summary>
    /// <param name="manifest">命令清单。</param>
    /// <param name="outputDirectory">输出目录；不存在时自动创建。</param>
    /// <returns>生成的文件完整路径。</returns>
    public static string GenerateToDisk(CommandsManifest manifest, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        var content = new CommandTypeScriptGenerator().Generate(manifest);
        var filePath = Path.Combine(outputDirectory, TypeScriptFileName);
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return filePath;
    }
}
