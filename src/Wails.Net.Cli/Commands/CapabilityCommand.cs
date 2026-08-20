using System.CommandLine;
using System.Text.Json;
using Wails.Net.Application.Security;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// capability 命令：管理 Wails.Net 项目能力文件（R3-D.B）。
/// <para>
/// 对应 Tauri v2 的 <c>cargo tauri capability</c> 体验：生成 / 列出 / 校验能力 JSON。
/// 子命令：
/// <list type="bullet">
/// <item><c>capability init &lt;plugin&gt;</c>：为指定插件生成默认 capability JSON 到 stdout 或文件；</item>
/// <item><c>capability list</c>：列出所有已注册插件 + 其默认 capability 标识符；</item>
/// <item><c>capability validate &lt;file&gt;</c>：校验单个 capability JSON 文件。</item>
/// </list>
/// </para>
/// <para>
/// 数据源：<see cref="PluginPermissionManifestRegistry"/>（源生成器嵌入的插件清单）。
/// CLI 与运行期共享同一份注册表，确保 <c>init</c> 产出的 capability 与运行时声明的权限一致。
/// </para>
/// </summary>
internal sealed class CapabilityCommand : CliCommandBase
{
    /// <summary>
    /// 创建 capability 命令实例（含全部子命令）。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("capability", "管理 Wails.Net 项目能力文件（生成 / 列出 / 校验）");

        command.Subcommands.Add(CreateInitCommand());
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateValidateCommand());

        return command;
    }

    /// <summary>
    /// 创建 <c>capability init &lt;plugin&gt;</c> 子命令。
    /// </summary>
    private static Command CreateInitCommand()
    {
        var pluginArg = new Argument<string>("plugin");
        pluginArg.Description = "插件名（如 window / fs / dialog）";

        var outOption = new Option<FileInfo?>("--out");
        outOption.Description = "输出文件路径；缺省时打印到 stdout";

        var command = new Command("init", "为指定插件生成默认 capability JSON");
        command.Arguments.Add(pluginArg);
        command.Options.Add(outOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var plugin = parseResult.GetValue(pluginArg);
            var outFile = parseResult.GetValue(outOption);
            var cmd = new CapabilityCommand();
            return await cmd.ExecuteInitAsync(plugin!, outFile);
        });

        return command;
    }

    /// <summary>
    /// 创建 <c>capability list</c> 子命令。
    /// </summary>
    private static Command CreateListCommand()
    {
        var command = new Command("list", "列出所有已注册插件及其默认 capability 标识符");

        command.Action = AsyncAction.Create(async (_, _) =>
        {
            var cmd = new CapabilityCommand();
            return await cmd.ExecuteListAsync();
        });

        return command;
    }

    /// <summary>
    /// 创建 <c>capability validate &lt;file&gt;</c> 子命令。
    /// </summary>
    private static Command CreateValidateCommand()
    {
        var fileArg = new Argument<FileInfo>("file");
        fileArg.Description = "capability JSON 文件路径";

        var command = new Command("validate", "校验单个 capability JSON 文件");
        command.Arguments.Add(fileArg);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg);
            var cmd = new CapabilityCommand();
            return await cmd.ExecuteValidateAsync(file!);
        });

        return command;
    }

    /// <summary>
    /// 执行 init 子命令。
    /// </summary>
    /// <param name="plugin">插件名。</param>
    /// <param name="outFile">输出文件；为 null 则写到 stdout。</param>
    /// <returns>退出码。</returns>
    internal Task<int> ExecuteInitAsync(string plugin, FileInfo? outFile)
    {
        if (string.IsNullOrWhiteSpace(plugin))
        {
            Error("插件名不能为空");
            return Task.FromResult(1);
        }

        var manifestJson = PluginPermissionManifestRegistry.Get(plugin);
        if (manifestJson is null)
        {
            Error($"插件 '{plugin}' 未在已注册清单中找到（源生成器未为该插件产出权限清单？）");
            Error("已注册插件：" + string.Join(", ",
                PluginPermissionManifestRegistry.GetAll().Keys.OrderBy(k => k, StringComparer.Ordinal)));
            return Task.FromResult(1);
        }

        var json = DefaultCapabilityGenerator.GenerateJson(plugin, manifestJson);

        if (outFile is not null)
        {
            try
            {
                File.WriteAllText(outFile.FullName, json);
                Success($"已生成 capability 文件：{outFile.FullName}");
                return Task.FromResult(0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error($"写入文件失败：{ex.Message}");
                return Task.FromResult(1);
            }
        }
        else
        {
            Console.WriteLine(json);
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// 执行 list 子命令。
    /// </summary>
    internal Task<int> ExecuteListAsync()
    {
        var all = PluginPermissionManifestRegistry.GetAll();
        if (all.Count == 0)
        {
            Info("当前进程未注册任何插件清单（源生成器未触发 / 模块未加载）");
            return Task.FromResult(0);
        }

        Info($"已注册插件清单：{all.Count} 个");
        foreach (var kv in all.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var perms = DefaultCapabilityGenerator.ExtractPermissions(kv.Value);
            var defaultId = DefaultCapabilityGenerator.DefaultIdentifier(kv.Key);
            Info($"  - {kv.Key}  ({perms.Count} 个权限)  default capability: {defaultId}");
        }
        return Task.FromResult(0);
    }

    /// <summary>
    /// 执行 validate 子命令。
    /// </summary>
    internal Task<int> ExecuteValidateAsync(FileInfo file)
    {
        if (!file.Exists)
        {
            Error($"文件不存在：{file.FullName}");
            return Task.FromResult(1);
        }

        var capability = CapabilityFileLoader.LoadFromFile(file.FullName);
        if (capability is null)
        {
            Error($"文件解析失败：{file.FullName}（不是合法 capability JSON）");
            return Task.FromResult(1);
        }

        var issues = CapabilityManifestValidator.ValidateShape(new[] { capability });
        if (issues.Count == 0)
        {
            Success($"capability 校验通过：{capability.Identifier}");
            return Task.FromResult(0);
        }

        var errors = issues.Where(i => i.Severity == CapabilityValidationSeverity.Error).ToList();
        var warnings = issues.Where(i => i.Severity == CapabilityValidationSeverity.Warning).ToList();

        foreach (var w in warnings)
        {
            Warn($"[{w.Code}] {w.Message}");
        }
        foreach (var e in errors)
        {
            Error($"[{e.Code}] {e.Message}");
        }

        if (errors.Count > 0)
        {
            Error("capability 校验失败");
            return Task.FromResult(1);
        }

        Success($"capability 校验通过（含警告）");
        return Task.FromResult(0);
    }
}