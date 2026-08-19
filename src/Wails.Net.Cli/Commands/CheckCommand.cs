using System.CommandLine;
using Wails.Net.Cli.Check;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// check 命令：构建期校验 capabilities 目录（OPT-SEC-004，2026-08-15 新增）。
/// <para>
/// 对应 Tauri v2 构建期 ACL/capability 校验：校验 JSON schema、平台名、
/// 权限标识格式与存在性，使拼写错误在构建期暴露而非运行时静默失败。
/// </para>
/// </summary>
internal sealed class CheckCommand : CliCommandBase
{
    /// <summary>
    /// 创建 check 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var capabilitiesDirOption = new Option<DirectoryInfo?>("--capabilities-dir");
        capabilitiesDirOption.Description = "capabilities 目录路径（默认读 appsettings.json 配置或项目根/capabilities）";

        var permissionsManifestOption = new Option<DirectoryInfo?>("--permissions-manifest");
        permissionsManifestOption.Description = "插件权限清单目录（每插件一个 JSON 文件，可选；缺省时自动加载源生成器嵌入的插件清单）";

        var command = new Command("check", "校验 capabilities 目录（构建期权限检查）");
        command.Options.Add(projectOption);
        command.Options.Add(capabilitiesDirOption);
        command.Options.Add(permissionsManifestOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var capabilitiesDir = parseResult.GetValue(capabilitiesDirOption);
            var manifestDir = parseResult.GetValue(permissionsManifestOption);

            var cmd = new CheckCommand();
            return await cmd.ExecuteAsync(project, capabilitiesDir, manifestDir);
        });

        return command;
    }

    /// <summary>
    /// 执行 check 命令。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="capabilitiesDir">显式 capabilities 目录。</param>
    /// <param name="manifestDir">插件权限清单目录。</param>
    /// <returns>退出码：0 通过；1 校验失败。</returns>
    internal Task<int> ExecuteAsync(
        FileInfo? project,
        DirectoryInfo? capabilitiesDir,
        DirectoryInfo? manifestDir)
    {
        // 解析项目路径
        string projectPath;
        if (project is not null)
        {
            if (!project.Exists)
            {
                Error($"项目文件不存在：{project.FullName}");
                return Task.FromResult(1);
            }
            projectPath = project.FullName;
        }
        else
        {
            var csprojFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
            if (csprojFiles.Length != 1)
            {
                Error("未找到唯一项目文件，请通过 --project 指定，或在项目目录中运行");
                return Task.FromResult(1);
            }
            projectPath = csprojFiles[0];
        }

        Info($"校验项目：{projectPath}");

        // 加载已知权限集合（R3-D.A）：始终包含源生成器聚合的插件清单（OPT-PLUGIN-003 闭环），
        // 显式指定 --permissions-manifest 时再合并外部目录，去重后报告来源计数。
        // 显式目录不存在视为错误（用户显式声明的资源缺失）。
        if (manifestDir is not null && !manifestDir.Exists)
        {
            Error($"权限清单目录不存在：{manifestDir.FullName}");
            return Task.FromResult(1);
        }
        var (knownPermissions, generatedCount, manifestCount) =
            CapabilityChecker.LoadKnownPermissions(manifestDir?.FullName);
        Info($"已加载权限清单：源生成 {generatedCount} 个 + 外部目录 {manifestCount} 个 = 去重后 {knownPermissions.Count} 个已知权限标识");

        var result = CapabilityChecker.CheckProject(
            projectPath,
            capabilitiesDir?.FullName,
            knownPermissions);

        Info($"扫描能力文件：{result.FileCount} 个，声明权限：{result.PermissionCount} 个");

        foreach (var warning in result.Warnings)
        {
            Warn(warning);
        }

        if (!result.Success)
        {
            Error("capability 校验失败：");
            foreach (var error in result.Errors)
            {
                Error($"  - {error}");
            }
            Error("修复后重新运行 wails-net check 或 wails-net build");
            return Task.FromResult(1);
        }

        if (result.Errors.Count == 0 && result.Warnings.Count == 0)
        {
            Success("capability 校验通过");
        }
        else
        {
            Success("capability 校验通过（存在警告，请查阅上方输出）");
        }
        return Task.FromResult(0);
    }
}
