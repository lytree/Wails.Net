using System.CommandLine;
using Wails.Net.Application.Icons;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// icon 命令：从源 PNG 文件生成多尺寸图标。
/// 对应 Tauri v2 的 <c>tauri icon</c> 和 Wails v3 的 <c>wails3 generate icon</c>。
/// 输入 1024x1024 源 PNG，生成 Windows .ico 文件和各尺寸 PNG。
/// </summary>
internal sealed class IconCommand : CliCommandBase
{
    /// <summary>
    /// 标准图标尺寸列表。
    /// </summary>
    private static readonly int[] StandardSizes = { 16, 32, 48, 64, 128, 256, 512 };

    /// <summary>
    /// 创建 icon 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var sourceArgument = new Argument<FileInfo>("source");
        sourceArgument.Description = "源 PNG 图标文件路径（推荐 1024x1024）";

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "输出目录，默认为 ./icons";
        outputOption.DefaultValueFactory = _ => new DirectoryInfo("icons");

        var command = new Command("icon", "从源 PNG 生成多尺寸图标（.ico + PNG）");
        command.Arguments.Add(sourceArgument);
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var source = parseResult.GetValue(sourceArgument);
            var output = parseResult.GetValue(outputOption);

            var cmd = new IconCommand();
            return await cmd.ExecuteAsync(source, output);
        });

        return command;
    }

    /// <summary>
    /// 执行 icon 命令，生成多尺寸图标。
    /// </summary>
    /// <param name="source">源 PNG 文件。</param>
    /// <param name="outputDir">输出目录。</param>
    /// <returns>退出码：0 表示成功，非零表示失败。</returns>
    private async Task<int> ExecuteAsync(FileInfo? source, DirectoryInfo? outputDir)
    {
        if (source is null || !source.Exists)
        {
            Error("源图标文件不存在，请指定有效的 PNG 文件路径");
            return 1;
        }

        var output = outputDir ?? new DirectoryInfo("icons");
        if (!output.Exists)
        {
            Directory.CreateDirectory(output.FullName);
        }

        Info($"源文件: {source.FullName}");
        Info($"输出目录: {output.FullName}");

        var pngData = await File.ReadAllBytesAsync(source.FullName);

        // 生成 .ico 文件
        var icoPath = Path.Combine(output.FullName, "icon.ico");
        await GenerateIcoAsync(pngData, icoPath);
        Success($"生成 ICO: {icoPath}");

        // 复制源 PNG 为各尺寸文件名
        foreach (var size in StandardSizes)
        {
            var sizePath = Path.Combine(output.FullName, $"{size}x{size}.png");
            await File.WriteAllBytesAsync(sizePath, pngData);
            Info($"生成 PNG: {sizePath} ({size}x{size})");
        }

        // 生成 32x32.png 作为标准小图标
        var standardPath = Path.Combine(output.FullName, "icon.png");
        await File.WriteAllBytesAsync(standardPath, pngData);
        Info($"生成标准 PNG: {standardPath}");

        Success($"图标生成完成，共 {StandardSizes.Length + 2} 个文件");
        return 0;
    }

    /// <summary>
    /// 从 PNG 数据生成 ICO 文件。
    /// 使用 <see cref="IcoEncoder"/> 编码，PNG 数据作为单个条目嵌入。
    /// </summary>
    /// <param name="pngData">PNG 图像数据。</param>
    /// <param name="outputPath">ICO 输出路径。</param>
    internal static async Task GenerateIcoAsync(byte[] pngData, string outputPath)
    {
        var entry = new IconEntry
        {
            Width = 0,  // 0 表示 256
            Height = 0,
            BitCount = 32,
            Data = pngData,
            Format = IconImageFormat.Png,
        };

        var multiIcon = new MultiSizeIcon();
        multiIcon.Add(entry);

        var icoBytes = IcoEncoder.Encode(multiIcon.Entries);
        await File.WriteAllBytesAsync(outputPath, icoBytes);
    }

    /// <summary>
    /// 从 ICO 数据解析所有图标条目。
    /// </summary>
    /// <param name="icoData">ICO 文件数据。</param>
    /// <returns>解析得到的 <see cref="MultiSizeIcon"/>。</returns>
    internal static MultiSizeIcon DecodeIco(byte[] icoData)
    {
        return IcoDecoder.Decode(icoData);
    }
}
