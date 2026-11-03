using System.CommandLine;
using System.Globalization;
using SkiaSharp;
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
    /// .ico 文件的默认尺寸列表（1–256 像素，与 Wails v3 的 <c>generate icons</c> 默认值一致）。
    /// </summary>
    private const string DefaultIcoSizes = "256,128,64,48,32,16";

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

        var sizesOption = new Option<string?>("--sizes");
        sizesOption.Description = "写入 .ico 的尺寸列表（逗号分隔，1–256 像素，0 表示忽略）";
        sizesOption.DefaultValueFactory = _ => DefaultIcoSizes;

        var command = new Command("icon", "从源 PNG 生成多尺寸图标（.ico + PNG）");
        command.Arguments.Add(sourceArgument);
        command.Options.Add(outputOption);
        command.Options.Add(sizesOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var source = parseResult.GetValue(sourceArgument);
            var output = parseResult.GetValue(outputOption);
            var sizes = parseResult.GetValue(sizesOption);

            var cmd = new IconCommand();
            return await cmd.ExecuteAsync(source, output, sizes);
        });

        return command;
    }

    /// <summary>
    /// 执行 icon 命令，生成多尺寸图标。
    /// </summary>
    /// <param name="source">源 PNG 文件。</param>
    /// <param name="outputDir">输出目录。</param>
    /// <param name="sizes">写入 .ico 的尺寸列表（逗号分隔，0 表示忽略）。</param>
    /// <returns>退出码：0 表示成功，非零表示失败。</returns>
    internal async Task<int> ExecuteAsync(FileInfo? source, DirectoryInfo? outputDir, string? sizes)
    {
        IReadOnlyList<int> icoSizes;
        try
        {
            // 先校验尺寸再读取输入文件：非法尺寸不会触达任何输出文件（对应 Wails v3 #6478）。
            icoSizes = ParseSizes(sizes);
        }
        catch (ArgumentException ex)
        {
            Error(ex.Message);
            return 1;
        }

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
        try
        {
            await GenerateIcoAsync(pngData, icoPath, icoSizes);
        }
        catch (InvalidOperationException ex)
        {
            Error(ex.Message);
            return 1;
        }

        Success($"生成 ICO: {icoPath}");

        // 使用 SkiaSharp 加载源图像并缩放为各尺寸 PNG
        using var sourceBitmap = SKBitmap.Decode(pngData);
        if (sourceBitmap is null)
        {
            Error("无法解析源 PNG 图像，文件可能已损坏或不是有效的 PNG");
            return 1;
        }

        foreach (var size in StandardSizes)
        {
            var sizePath = Path.Combine(output.FullName, $"{size}x{size}.png");
            using var resized = sourceBitmap.Resize(
                new SKImageInfo(size, size),
                new SKSamplingOptions(SKFilterMode.Linear));
            if (resized is null)
            {
                Error($"缩放至 {size}x{size} 失败");
                return 1;
            }

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded is null)
            {
                Error($"编码 {size}x{size} PNG 失败");
                return 1;
            }

            await using var fileStream = File.Create(sizePath);
            encoded.SaveTo(fileStream);
            Info($"生成 PNG: {sizePath} ({size}x{size})");
        }

        // 生成 icon.png 作为标准图标（使用源图像原始尺寸）
        var standardPath = Path.Combine(output.FullName, "icon.png");
        await File.WriteAllBytesAsync(standardPath, pngData);
        Info($"生成标准 PNG: {standardPath}");

        Success($"图标生成完成，共 {StandardSizes.Length + 2} 个文件");
        return 0;
    }

    /// <summary>
    /// 解析逗号分隔的 ICO 尺寸列表。
    /// 对应 Wails v3 #6478 中的 <c>parseSizes</c>：允许 1–256 像素，0 表示忽略，
    /// 且至少需要一个非零尺寸（ICO 目录项单个维度只有 1 字节，256 以 0 编码）。
    /// </summary>
    /// <param name="sizes">逗号分隔的尺寸字符串。</param>
    /// <returns>去零后的尺寸列表。</returns>
    /// <exception cref="ArgumentException">尺寸为空、非法或超出 1–256 范围。</exception>
    internal static IReadOnlyList<int> ParseSizes(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes))
        {
            throw new ArgumentException("至少需要一个非零图标尺寸", nameof(sizes));
        }

        var result = new List<int>();
        foreach (var part in sizes.Split(','))
        {
            var text = part.Trim();
            if (text.Length == 0
                || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                throw new ArgumentException($"无效的图标尺寸：\"{text}\"", nameof(sizes));
            }

            if (size == 0)
            {
                continue;
            }

            if (size < 1 || size > 256)
            {
                throw new ArgumentException($"图标尺寸必须在 1 到 256 像素之间：{size}", nameof(sizes));
            }

            result.Add(size);
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("至少需要一个非零图标尺寸", nameof(sizes));
        }

        return result;
    }

    /// <summary>
    /// 从 PNG 数据按指定尺寸生成多尺寸 ICO 文件（对应 Wails v3 #6478）。
    /// 每个尺寸由源图像缩放后编码为 PNG 帧嵌入 ICO。
    /// </summary>
    /// <param name="pngData">源 PNG 图像数据。</param>
    /// <param name="outputPath">ICO 输出路径。</param>
    /// <param name="sizes">ICO 尺寸列表（1–256 像素）。</param>
    internal static async Task GenerateIcoAsync(byte[] pngData, string outputPath, IReadOnlyList<int> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        using var sourceBitmap = SKBitmap.Decode(pngData)
            ?? throw new InvalidOperationException("无法解析源 PNG 图像，文件可能已损坏或不是有效的 PNG");

        var multiIcon = new MultiSizeIcon();
        foreach (var size in sizes)
        {
            using var resized = sourceBitmap.Resize(
                new SKImageInfo(size, size),
                new SKSamplingOptions(SKFilterMode.Linear))
                ?? throw new InvalidOperationException($"缩放至 {size}x{size} 失败");

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException($"编码 {size}x{size} PNG 失败");

            multiIcon.AddPng(size, encoded.ToArray());
        }

        await File.WriteAllBytesAsync(outputPath, multiIcon.ToIcoBytes());
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
