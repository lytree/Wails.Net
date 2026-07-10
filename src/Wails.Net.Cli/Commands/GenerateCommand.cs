using System.CommandLine;
using System.Reflection;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// generate 命令：从 C# 程序集生成 TypeScript 绑定文件。
/// 对应 Wails v3 Go 版本 cmd/wails3/generate.go。
/// </summary>
internal sealed class GenerateCommand : CliCommandBase
{
    /// <summary>
    /// 创建 generate 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var assemblyOption = new Option<FileInfo?>("--assembly");
        assemblyOption.Description = "要分析的 C# 程序集路径（.dll）";

        var outputOption = new Option<DirectoryInfo>("--output");
        outputOption.Description = "TypeScript 文件输出目录";
        outputOption.DefaultValueFactory = _ => new DirectoryInfo("bindings");

        var eventsAssemblyOption = new Option<FileInfo?>("--events-assembly");
        eventsAssemblyOption.Description = "包含事件枚举的程序集路径（默认与 --assembly 相同）";

        var command = new Command("generate", "从 C# 程序集生成 TypeScript 绑定文件");
        command.Options.Add(assemblyOption);
        command.Options.Add(outputOption);
        command.Options.Add(eventsAssemblyOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var assembly = parseResult.GetValue(assemblyOption);
            var output = parseResult.GetValue(outputOption);
            var eventsAssembly = parseResult.GetValue(eventsAssemblyOption);

            var cmd = new GenerateCommand();
            return await cmd.ExecuteAsync(assembly, output!, eventsAssembly);
        });

        return command;
    }

    /// <summary>
    /// 执行 generate 命令。
    /// </summary>
    /// <param name="assembly">要分析的主程序集。</param>
    /// <param name="outputDir">输出目录。</param>
    /// <param name="eventsAssembly">事件枚举程序集（可空）。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? assembly,
        DirectoryInfo outputDir,
        FileInfo? eventsAssembly)
    {
        if (assembly is null || !assembly.Exists)
        {
            Error($"程序集文件不存在：{assembly?.FullName ?? "(未指定)"}");
            return 1;
        }

        Info($"加载程序集：{assembly.FullName}");
        var loaded = Assembly.LoadFrom(assembly.FullName);
        var eventsLoaded = eventsAssembly is not null && eventsAssembly.Exists
            ? Assembly.LoadFrom(eventsAssembly.FullName)
            : loaded;

        var options = new BindingGenerationOptions
        {
            OutputDirectory = outputDir.FullName,
            GenerateDefinitions = true,
            GenerateCaller = true,
            GenerateIdMap = true,
            GenerateEvents = true,
        };

        Info($"输出目录：{outputDir.FullName}");
        var pipeline = new BindingGenerationPipeline();
        var result = pipeline.GenerateToDisk(loaded, eventsLoaded, options);

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

        return 0;
    }
}
