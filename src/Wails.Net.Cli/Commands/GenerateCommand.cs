using System.CommandLine;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// generate 命令：从源生成器元数据生成 TypeScript 绑定文件。
/// 对应 Wails v3 Go 版本 cmd/wails3/generate.go。
/// </summary>
/// <remarks>
/// 此实现使用 <see cref="GeneratedBindingsMetadata"/> 和 <see cref="Wails.Net.Events.GeneratedEventsMetadata"/>
/// 中的编译期元数据，不再通过 <see cref="System.Reflection.Assembly.LoadFrom"/> 加载程序集进行运行时反射分析。
/// 调用方需确保目标程序集已加载到当前进程（通过 <c>[ModuleInitializer]</c> 自动注册元数据）。
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

        var command = new Command("generate", "从源生成器元数据生成 TypeScript 绑定文件");
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var output = parseResult.GetValue(outputOption);

            var cmd = new GenerateCommand();
            return await cmd.ExecuteAsync(output!);
        });

        return command;
    }

    /// <summary>
    /// 执行 generate 命令。
    /// </summary>
    /// <param name="outputDir">输出目录。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(DirectoryInfo outputDir)
    {
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
}
