using System.CommandLine;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// generate 命令：从源生成器元数据生成 TypeScript 绑定文件，
/// 或从命令清单（commands.manifest.json）生成 TypeScript 命令封装。
/// 对应 Wails v3 Go 版本 cmd/wails3/generate.go。
/// </summary>
/// <remarks>
/// 此实现使用 <see cref="GeneratedBindingsMetadata"/> 和 <see cref="Wails.Net.Events.GeneratedEventsMetadata"/>
/// 中的编译期元数据，不再通过 <see cref="System.Reflection.Assembly.LoadFrom"/> 加载程序集进行运行时反射分析。
/// 调用方需确保目标程序集已加载到当前进程（通过 <c>[ModuleInitializer]</c> 自动注册元数据）。
/// <para>
/// P2-1 收尾：新增 <c>--commands-manifest</c> 选项，从应用导出的命令清单生成
/// <c>commands.ts</c> 强类型调用封装（见 <see cref="CommandTypeScriptGenerator"/>），
/// 以清单为唯一来源消除前后端 API 漂移。
/// </para>
/// </remarks>
internal sealed class GenerateCommand : CliCommandBase
{
    /// <summary>
    /// 创建 generate 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var outputOption = new Option<DirectoryInfo>("--output");
        outputOption.Description = "TypeScript 文件输出目录";
        outputOption.DefaultValueFactory = _ => new DirectoryInfo("bindings");

        var commandsManifestOption = new Option<FileInfo?>("--commands-manifest");
        commandsManifestOption.Description =
            "命令清单文件（commands.manifest.json）路径；指定时从清单生成 commands.ts 命令封装（P2-1）";

        var command = new Command("generate", "从源生成器元数据生成 TypeScript 绑定文件");
        command.Options.Add(outputOption);
        command.Options.Add(commandsManifestOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var output = parseResult.GetValue(outputOption);
            var manifestPath = parseResult.GetValue(commandsManifestOption);

            var cmd = new GenerateCommand();
            return await cmd.ExecuteAsync(output!, manifestPath);
        });

        return command;
    }

    /// <summary>
    /// 执行 generate 命令。
    /// 指定 <paramref name="commandsManifest"/> 时仅生成命令封装；否则生成绑定文件。
    /// </summary>
    /// <param name="outputDir">输出目录。</param>
    /// <param name="commandsManifest">命令清单文件路径；null 时走绑定生成路径。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(DirectoryInfo outputDir, FileInfo? commandsManifest)
    {
        if (commandsManifest is not null)
        {
            return await GenerateCommandsAsync(outputDir, commandsManifest);
        }

        var options = new BindingGenerationOptions
        {
            OutputDirectory = outputDir.FullName,
            GenerateDefinitions = true,
            GenerateCaller = true,
            GenerateIdMap = true,
            GenerateEvents = true,
            GenerateKnownEvents = true,
        };

        Info($"输出目录：{outputDir.FullName}");
        var pipeline = new BindingGenerationPipeline();
        var result = pipeline.GenerateToDisk(options);

        if (!result.Success)
        {
            Error($"生成失败：{result.ErrorMessage}");
            return 2;
        }

        Success($"生成完成：{result.MethodCount} 个方法 / {result.ClassCount} 个类");
        foreach (var file in result.GeneratedFiles.Keys)
        {
            Info($"  - {file}");
        }

        return await Task.FromResult(0);
    }

    /// <summary>
    /// 从命令清单文件生成 TypeScript 命令封装（P2-1 收尾）。
    /// </summary>
    /// <param name="outputDir">输出目录。</param>
    /// <param name="manifestFile">命令清单文件。</param>
    /// <returns>退出码。</returns>
    private async Task<int> GenerateCommandsAsync(DirectoryInfo outputDir, FileInfo manifestFile)
    {
        Info($"命令清单：{manifestFile.FullName}");
        Info($"输出目录：{outputDir.FullName}");

        try
        {
            var manifest = CommandTypeScriptGenerator.LoadManifest(manifestFile.FullName);
            var filePath = CommandTypeScriptGenerator.GenerateToDisk(manifest, outputDir.FullName);
            Success($"命令封装生成完成：{manifest.Commands.Count} 个命令");
            Info($"  - {filePath}");
            return await Task.FromResult(0);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.Text.Json.JsonException)
        {
            Error($"命令清单解析失败：{ex.Message}");
            return 2;
        }
    }
}
