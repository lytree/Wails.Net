using System.CommandLine;
using Wails.Net.Cli.Scaffolding;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// new 命令：创建新的 Wails.Net 项目脚手架。
/// 对应 Wails v3 Go 版本 cmd/wails3/cmd_new.go。
/// </summary>
internal sealed class NewCommand : CliCommandBase
{
    /// <summary>
    /// 创建 new 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var nameArgument = new Argument<string>("name");
        nameArgument.Description = "新项目的名称（同时用作目录名）";

        var templateOption = new Option<string>("--template");
        templateOption.Description = "前端模板名称";
        templateOption.DefaultValueFactory = _ => "vanilla-ts";

        var directoryOption = new Option<DirectoryInfo?>("--directory");
        directoryOption.Description = "项目创建目录（默认为当前目录）";

        var command = new Command("new", "创建新的 Wails.Net 项目");
        command.Arguments.Add(nameArgument);
        command.Options.Add(templateOption);
        command.Options.Add(directoryOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var name = parseResult.GetValue(nameArgument);
            var template = parseResult.GetValue(templateOption) ?? "vanilla-ts";
            var directory = parseResult.GetValue(directoryOption);

            var cmd = new NewCommand();
            return await cmd.ExecuteAsync(name!, template, directory);
        });

        return command;
    }

    /// <summary>
    /// 执行 new 命令，生成项目脚手架。
    /// </summary>
    /// <param name="name">项目名称。</param>
    /// <param name="template">前端模板名称。</param>
    /// <param name="directory">目标目录。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        string name,
        string template,
        DirectoryInfo? directory)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Error("项目名称不能为空");
            return 1;
        }

        if (!ProjectScaffolder.IsValidTemplateName(template))
        {
            Error($"不支持的前端模板：{template}");
            Info($"支持的模板：{string.Join(", ", ProjectScaffolder.GetSupportedTemplates())}");
            return 1;
        }

        directory ??= new DirectoryInfo(Directory.GetCurrentDirectory());
        if (!directory.Exists)
        {
            try
            {
                directory.Create();
            }
            catch (Exception ex)
            {
                Error($"无法创建目录 {directory.FullName}：{ex.Message}");
                return 1;
            }
        }

        Info($"创建项目：{name}");
        Info($"模板：{template}");
        Info($"目标目录：{directory.FullName}");

        var scaffolder = new ProjectScaffolder();
        var result = await scaffolder.ScaffoldAsync(name, template, directory);

        if (!result.Success)
        {
            Error($"脚手架失败：{result.ErrorMessage}");
            return 2;
        }

        Success("项目创建完成。生成的文件：");
        foreach (var file in result.CreatedFiles)
        {
            Info($"  - {file}");
        }

        Info(string.Empty);
        Info("后续步骤：");
        Info($"  cd {name}");
        Info("  dotnet restore");
        Info("  wails.net generate --assembly <你的程序集.dll> --output frontend/src/wails");
        Info("  dotnet run");

        return 0;
    }
}
