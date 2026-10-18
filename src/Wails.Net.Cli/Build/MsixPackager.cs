using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace Wails.Net.Cli.Build;

/// <summary>
/// MSIX 打包结果。
/// </summary>
public sealed class MsixPackageResult
{
    /// <summary>是否成功生成 .msix/.appx 包。</summary>
    public bool Success { get; init; }

    /// <summary>错误消息（失败或降级时非空）。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>生成的 .msix/.appx 路径（成功时非空）。</summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// 结构化目录（AppxManifest.xml + Assets + 可执行文件）。
    /// 打包工具缺失时的替代产物（不得静默失败）；成功打包后会被清理。
    /// </summary>
    public string? StructureDirectory { get; init; }

    /// <summary>是否降级（工具缺失：未生成 .msix，或已生成但未签名）。</summary>
    public bool Degraded { get; init; }
}

/// <summary>
/// MSIX 打包器：渲染 AppxManifest.xml 与包结构，调用 Windows SDK 工具产出 .msix/.appx。
/// 对应 Wails v3 Go 版本 internal/commands/msix.go（<c>ToolMSIX</c>）与
/// internal/commands/build_assets/windows/msix/ 下的两个模板。
/// </summary>
public sealed class MsixPackager
{
    /// <summary>MSIX 包扩展名。</summary>
    public const string MsixExtension = ".msix";

    /// <summary>Appx 包扩展名。</summary>
    public const string AppxExtension = ".appx";

    /// <summary>清单文件名（MakeAppx /d 目录约定）。</summary>
    public const string AppxManifestFileName = "AppxManifest.xml";

    /// <summary>MakeAppx.exe（Windows SDK 工具名）。</summary>
    private const string MakeAppxName = "MakeAppx.exe";

    /// <summary>signtool.exe（Windows SDK 工具名）。</summary>
    private const string SignToolName = "signtool.exe";

    /// <summary>Microsoft MSIX Packaging Tool（Microsoft Store 应用名）。</summary>
    private const string MsixPackagingToolName = "MsixPackagingTool.exe";

    /// <summary>存在文件关联时额外生成的 FileIcon.png 尺寸（像素）。</summary>
    private const int FileIconSize = 44;

    /// <summary>
    /// 基础资源清单（文件名 → 宽 × 高，像素）。
    /// 对应 Go <c>generatePlaceholderImage</c> 中的 sizes 表。
    /// </summary>
    internal static readonly (string Name, int Width, int Height)[] BaseAssets =
    {
        ("Square150x150Logo.png", 150, 150),
        ("Square44x44Logo.png", 44, 44),
        ("Wide310x150Logo.png", 310, 150),
        ("SplashScreen.png", 620, 300),
        ("StoreLogo.png", 50, 50),
    };

    /// <summary>
    /// 解析打包格式字符串对应的 MSIX/Appx 扩展名。
    /// </summary>
    /// <param name="format">格式字符串（msix / appx，大小写不敏感）。</param>
    /// <returns>扩展名（含前导点）；非 MSIX/Appx 格式返回 null。</returns>
    public static string? TryResolveExtension(string format) => format.ToLowerInvariant() switch
    {
        "msix" => MsixExtension,
        "appx" => AppxExtension,
        _ => null,
    };

    /// <summary>
    /// 将 Go 风格架构别名（amd64/386）翻译为 MSIX 架构名并校验合法性。
    /// 对应 Go <c>archToMSIX</c>（build-assets.go）与 <c>validateMSIXOptions</c> 的架构校验。
    /// </summary>
    /// <param name="input">架构名或别名。</param>
    /// <returns>校验通过的 MSIX 架构名。</returns>
    /// <exception cref="ArgumentException">架构不受支持时抛出。</exception>
    public static string TranslateArchitecture(string input)
    {
        var arch = input.ToLowerInvariant() switch
        {
            "amd64" => "x64",
            "386" or "i386" => "x86",
            var other => other,
        };

        return arch switch
        {
            "x64" or "x86" or "arm" or "arm64" or "x86a64" or "neutral" => arch,
            _ => throw new ArgumentException(
                $"不支持的 MSIX 处理器架构 \"{input}\"：应为 x64、x86、arm、arm64、x86a64 或 neutral"),
        };
    }

    /// <summary>
    /// 由目标 RID 推导 MSIX 处理器架构（如 <c>win-arm64</c> → <c>arm64</c>）。
    /// </summary>
    /// <param name="rid">目标 RID；为空时使用当前主机架构。</param>
    /// <returns>MSIX 架构名。</returns>
    public static string ResolveArchitecture(string? rid)
    {
        if (string.IsNullOrWhiteSpace(rid))
        {
            return ResolveHostArchitecture();
        }

        var suffix = rid.Trim();
        var dash = suffix.LastIndexOf('-');
        if (dash >= 0)
        {
            suffix = suffix[(dash + 1)..];
        }

        return TranslateArchitecture(suffix);
    }

    /// <summary>
    /// 获取当前主机架构对应的 MSIX 架构名。
    /// </summary>
    /// <returns>MSIX 架构名（x64/x86/arm64/arm）。</returns>
    public static string ResolveHostArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => "x64",
    };

    /// <summary>
    /// 填充默认值（发布者、架构、输出路径）。对应 Go <c>validateMSIXOptions</c> 中的默认赋值。
    /// </summary>
    /// <param name="options">MSIX 打包选项（就地修改）。</param>
    public static void ApplyDefaults(MsixPackageOptions options)
    {
        options.ProcessorArchitecture = TranslateArchitecture(
            string.IsNullOrWhiteSpace(options.ProcessorArchitecture)
                ? ResolveHostArchitecture()
                : options.ProcessorArchitecture);

        if (string.IsNullOrWhiteSpace(options.Publisher))
        {
            options.Publisher = $"CN={options.ProductCompany}";
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            options.OutputPath = Path.Combine(".", $"{options.ProductName}{MsixExtension}");
        }
    }

    /// <summary>
    /// 校验 MSIX 选项。对应 Go <c>validateMSIXOptions</c>。
    /// </summary>
    /// <param name="options">MSIX 打包选项。</param>
    /// <returns>校验通过返回 null；否则返回错误消息。</returns>
    public static string? Validate(MsixPackageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProductName))
        {
            return "产品名称（ProductName）不能为空";
        }

        if (string.IsNullOrWhiteSpace(options.ProductIdentifier))
        {
            return "产品标识符（ProductIdentifier）不能为空";
        }

        if (string.IsNullOrWhiteSpace(options.ProductCompany))
        {
            return "公司名称（ProductCompany）不能为空";
        }

        if (string.IsNullOrWhiteSpace(options.ExecutableName))
        {
            return "可执行文件名（ExecutableName）不能为空";
        }

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            return "可执行文件路径（ExecutablePath）不能为空";
        }

        if (!File.Exists(options.ExecutablePath))
        {
            return $"未找到可执行文件：{options.ExecutablePath}";
        }

        if (!string.IsNullOrWhiteSpace(options.CertificatePath) && !File.Exists(options.CertificatePath))
        {
            return $"未找到证书文件：{options.CertificatePath}";
        }

        try
        {
            TranslateArchitecture(options.ProcessorArchitecture);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        return null;
    }

    /// <summary>
    /// 在 PATH 或 Windows SDK 中查找工具（对应 Go <c>findWindowsSDKTool</c>）。
    /// 优先 PATH，其次 <c>{ProgramFiles(x86)}\Windows Kits\10\bin\10.*\{arch}</c>（版本降序）。
    /// </summary>
    /// <param name="fileName">工具文件名（含扩展名）。</param>
    /// <returns>工具完整路径；未找到返回 null。</returns>
    public static string? FindWindowsSdkTool(string fileName)
    {
        var inPath = Packager.FindExecutableInPath(fileName);
        if (inPath is not null)
        {
            return inPath;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(root))
        {
            root = @"C:\Program Files (x86)";
        }

        var bin = Path.Combine(root, "Windows Kits", "10", "bin");
        if (!Directory.Exists(bin))
        {
            return null;
        }

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };

        // 与 Go 一致：SDK 版本目录降序（优先最新），最后回退到 bin 目录本身。
        var versionDirectories = Directory.GetDirectories(bin, "10.*");
        Array.Sort(versionDirectories, StringComparer.Ordinal);
        Array.Reverse(versionDirectories);

        foreach (var directory in versionDirectories.Concat(new[] { bin }))
        {
            var candidate = Path.Combine(directory, arch, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 渲染 AppxManifest.xml（MakeAppx 输入）。
    /// 对应 Go 模板 <c>build_assets/windows/msix/app_manifest.xml.tmpl</c>。
    /// </summary>
    /// <param name="options">MSIX 打包选项。</param>
    /// <returns>AppxManifest.xml 内容。</returns>
    public static string GenerateAppxManifest(MsixPackageOptions options)
    {
        var executable = Path.GetFileName(options.ExecutableName);
        var builder = new StringBuilder();

        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<Package");
        builder.AppendLine("  xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"");
        builder.AppendLine("  xmlns:uap=\"http://schemas.microsoft.com/appx/manifest/uap/windows10\"");
        builder.AppendLine("  xmlns:uap3=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/3\"");
        builder.AppendLine("  xmlns:rescap=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities\"");
        builder.AppendLine("  xmlns:desktop=\"http://schemas.microsoft.com/appx/manifest/desktop/windows10\"");
        builder.AppendLine("  IgnorableNamespaces=\"uap3\">");
        builder.AppendLine();
        builder.AppendLine("  <Identity");
        builder.AppendLine($"    Name=\"{options.ProductIdentifier}\"");
        builder.AppendLine($"    Publisher=\"{WebUtility.HtmlEncode(options.Publisher)}\"");
        builder.AppendLine($"    Version=\"{options.ProductVersion}.0\"");
        builder.AppendLine($"    ProcessorArchitecture=\"{options.ProcessorArchitecture}\" />");
        builder.AppendLine();
        builder.AppendLine("  <Properties>");
        builder.AppendLine($"    <DisplayName>{options.ProductName}</DisplayName>");
        builder.AppendLine($"    <PublisherDisplayName>{options.ProductCompany}</PublisherDisplayName>");
        builder.AppendLine($"    <Description>{options.ProductDescription}</Description>");
        builder.AppendLine("    <Logo>Assets\\StoreLogo.png</Logo>");
        builder.AppendLine("  </Properties>");
        builder.AppendLine();
        builder.AppendLine("  <Dependencies>");
        builder.AppendLine("    <TargetDeviceFamily Name=\"Windows.Desktop\" MinVersion=\"10.0.17763.0\" MaxVersionTested=\"10.0.19041.0\" />");
        builder.AppendLine("  </Dependencies>");
        builder.AppendLine();
        builder.AppendLine("  <Resources>");
        builder.AppendLine("    <Resource Language=\"en-us\" />");
        builder.AppendLine("  </Resources>");
        builder.AppendLine();
        builder.AppendLine("  <Applications>");
        builder.AppendLine($"    <Application Id=\"{options.ProductIdentifier}\" Executable=\"{executable}\" EntryPoint=\"Windows.FullTrustApplication\">");
        builder.AppendLine("      <uap:VisualElements");
        builder.AppendLine($"        DisplayName=\"{options.ProductName}\"");
        builder.AppendLine($"        Description=\"{options.ProductDescription}\"");
        builder.AppendLine("        BackgroundColor=\"transparent\"");
        builder.AppendLine("        Square150x150Logo=\"Assets\\Square150x150Logo.png\"");
        builder.AppendLine("        Square44x44Logo=\"Assets\\Square44x44Logo.png\">");
        builder.AppendLine("        <uap:DefaultTile Wide310x150Logo=\"Assets\\Wide310x150Logo.png\" />");
        builder.AppendLine("        <uap:SplashScreen Image=\"Assets\\SplashScreen.png\" />");
        builder.AppendLine("      </uap:VisualElements>");
        builder.AppendLine();
        builder.AppendLine("      <Extensions>");
        builder.AppendLine($"        <desktop:Extension Category=\"windows.fullTrustProcess\" Executable=\"{executable}\" />");

        if (options.FileAssociations.Count > 0)
        {
            builder.AppendLine("        <uap:Extension Category=\"windows.fileTypeAssociation\">");
            builder.AppendLine($"          <uap:FileTypeAssociation Name=\"{options.ProductIdentifier}\">");
            builder.AppendLine($"            <uap:DisplayName>{options.ProductName}</uap:DisplayName>");
            builder.AppendLine("            <uap:Logo>Assets\\FileIcon.png</uap:Logo>");
            builder.AppendLine($"            <uap:InfoTip>{options.ProductName} File</uap:InfoTip>");
            builder.AppendLine("            <uap:SupportedFileTypes>");
            foreach (var association in options.FileAssociations)
            {
                builder.AppendLine($"              <uap:FileType>.{association.Ext}</uap:FileType>");
            }
            builder.AppendLine("            </uap:SupportedFileTypes>");
            builder.AppendLine("          </uap:FileTypeAssociation>");
            builder.AppendLine("        </uap:Extension>");
        }

        foreach (var protocol in options.Protocols)
        {
            builder.AppendLine("        <uap:Extension Category=\"windows.protocol\">");
            builder.AppendLine($"          <uap:Protocol Name=\"{protocol.Scheme}\">");
            builder.AppendLine($"            <uap:DisplayName>{protocol.Description}</uap:DisplayName>");
            builder.AppendLine("          </uap:Protocol>");
            builder.AppendLine("        </uap:Extension>");
        }

        builder.AppendLine("      </Extensions>");
        builder.AppendLine("    </Application>");
        builder.AppendLine("  </Applications>");
        builder.AppendLine();
        builder.AppendLine("  <Capabilities>");
        builder.AppendLine("    <rescap:Capability Name=\"runFullTrust\" />");
        if (options.FileAssociations.Count > 0)
        {
            builder.AppendLine("    <uap:Capability Name=\"documentsLibrary\" />");
        }
        builder.AppendLine("  </Capabilities>");
        builder.AppendLine("</Package>");

        return builder.ToString();
    }

    /// <summary>
    /// 渲染 Microsoft MSIX Packaging Tool 的 template.xml。
    /// 对应 Go 模板 <c>build_assets/windows/msix/template.xml.tmpl</c>。
    /// </summary>
    /// <param name="options">MSIX 打包选项。</param>
    /// <returns>template.xml 内容。</returns>
    public static string GeneratePackagingToolTemplate(MsixPackageOptions options)
    {
        var builder = new StringBuilder();

        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<MsixPackagingToolTemplate");
        builder.AppendLine("    xmlns=\"http://schemas.microsoft.com/msix/packaging/msixpackagingtool/template/2022\">");
        builder.AppendLine("    <Settings");
        builder.AppendLine("        AllowTelemetry=\"false\"");
        builder.AppendLine("        ApplyACLsToPackageFiles=\"true\"");
        builder.AppendLine("        GenerateCommandLineFile=\"true\"");
        builder.AppendLine("        AllowPromptForPassword=\"false\">");
        builder.AppendLine("    </Settings>");
        builder.AppendLine("    <Installer");
        builder.AppendLine($"        Path=\"{options.ExecutablePath}\"");
        builder.AppendLine("        Arguments=\"\"");
        builder.AppendLine($"        InstallLocation=\"C:\\Program Files\\{options.ProductCompany}\\{options.ProductName}\">");
        builder.AppendLine("    </Installer>");
        builder.AppendLine("    <PackageInformation");
        builder.AppendLine($"        PackageName=\"{options.ProductName}\"");
        builder.AppendLine($"        PackageDisplayName=\"{options.ProductName}\"");
        builder.AppendLine($"        PublisherName=\"{WebUtility.HtmlEncode(options.Publisher)}\"");
        builder.AppendLine($"        PublisherDisplayName=\"{options.ProductCompany}\"");
        builder.AppendLine($"        Version=\"{options.ProductVersion}.0\"");
        builder.AppendLine($"        PackageDescription=\"{options.ProductDescription}\">");
        builder.AppendLine("        <Capabilities>");
        builder.AppendLine("            <Capability Name=\"runFullTrust\" />");
        if (options.FileAssociations.Count > 0)
        {
            builder.AppendLine("            <Capability Name=\"documentsLibrary\" />");
        }
        builder.AppendLine("        </Capabilities>");
        builder.AppendLine("        <Applications>");
        builder.AppendLine("            <Application");
        builder.AppendLine($"                Id=\"{options.ProductIdentifier}\"");
        builder.AppendLine($"                Description=\"{options.ProductDescription}\"");
        builder.AppendLine($"                DisplayName=\"{options.ProductName}\"");
        builder.AppendLine($"                ExecutableName=\"{Path.GetFileName(options.ExecutableName)}\"");
        builder.AppendLine("                EntryPoint=\"Windows.FullTrustApplication\">");

        if (options.FileAssociations.Count > 0)
        {
            builder.AppendLine("                <Extensions>");
            builder.AppendLine("                    <Extension Category=\"windows.fileTypeAssociation\">");
            builder.AppendLine($"                        <FileTypeAssociation Name=\"{options.ProductIdentifier}\">");
            foreach (var association in options.FileAssociations)
            {
                builder.AppendLine("                            <SupportedFileTypes>");
                builder.AppendLine($"                                <FileType>.{association.Ext}</FileType>");
                builder.AppendLine("                            </SupportedFileTypes>");
            }
            builder.AppendLine("                        </FileTypeAssociation>");
            builder.AppendLine("                    </Extension>");
            builder.AppendLine("                </Extensions>");
        }

        builder.AppendLine("            </Application>");
        builder.AppendLine("        </Applications>");
        builder.AppendLine("        <Resources>");
        builder.AppendLine("            <Resource Language=\"en-us\" />");
        builder.AppendLine("        </Resources>");
        builder.AppendLine("        <Dependencies>");
        builder.AppendLine("            <TargetDeviceFamily Name=\"Windows.Desktop\" MinVersion=\"10.0.17763.0\" MaxVersionTested=\"10.0.19041.0\" />");
        builder.AppendLine("        </Dependencies>");
        builder.AppendLine("        <Properties>");
        builder.AppendLine("            <Framework>false</Framework>");
        builder.AppendLine($"            <DisplayName>{options.ProductName}</DisplayName>");
        builder.AppendLine($"            <PublisherDisplayName>{options.ProductCompany}</PublisherDisplayName>");
        builder.AppendLine($"            <Description>{options.ProductDescription}</Description>");
        builder.AppendLine("            <Logo>Assets\\AppIcon.png</Logo>");
        builder.AppendLine("        </Properties>");
        builder.AppendLine("    </PackageInformation>");
        builder.AppendLine($"    <SaveLocation PackagePath=\"{options.OutputPath}\" />");
        builder.AppendLine("    <PackageIntegrity>");
        builder.AppendLine($"        <CertificatePath>{options.CertificatePath}</CertificatePath>");
        builder.AppendLine("    </PackageIntegrity>");
        builder.AppendLine("</MsixPackagingToolTemplate>");

        return builder.ToString();
    }

    /// <summary>
    /// 生成 MSIX 包结构目录（AppxManifest.xml + Assets + 可执行文件）。
    /// 对应 Go <c>createMSIXPackageStructure</c>。
    /// </summary>
    /// <param name="options">MSIX 打包选项。</param>
    /// <param name="outputDirectory">结构目录。</param>
    public static async Task CreatePackageStructureAsync(MsixPackageOptions options, string outputDirectory)
    {
        var assetsDirectory = Path.Combine(outputDirectory, "Assets");
        Directory.CreateDirectory(assetsDirectory);

        var manifestPath = Path.Combine(outputDirectory, AppxManifestFileName);
        await File.WriteAllTextAsync(manifestPath, GenerateAppxManifest(options));

        var executableName = Path.GetFileName(options.ExecutableName);
        File.Copy(options.ExecutablePath, Path.Combine(outputDirectory, executableName), overwrite: true);

        foreach (var (name, width, height) in BaseAssets)
        {
            GeneratePlaceholderImage(Path.Combine(assetsDirectory, name), width, height);
        }

        if (options.FileAssociations.Count > 0)
        {
            GeneratePlaceholderImage(Path.Combine(assetsDirectory, "FileIcon.png"), FileIconSize, FileIconSize);
        }
    }

    /// <summary>
    /// 生成指定尺寸的透明 PNG 占位资源。
    /// 对应 Go <c>generatePlaceholderImage</c>（MakeAppx 会校验清单资源尺寸）。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="width">宽度（像素）。</param>
    /// <param name="height">高度（像素）。</param>
    internal static void GeneratePlaceholderImage(string outputPath, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
        {
            throw new InvalidOperationException($"PNG 编码失败：{outputPath}");
        }

        using var stream = File.Create(outputPath);
        encoded.SaveTo(stream);
    }

    /// <summary>
    /// 执行 MSIX 打包：生成包结构并调用 Windows SDK 工具产出 .msix/.appx。
    /// 对应 Go <c>ToolMSIX</c>。
    /// </summary>
    /// <param name="options">MSIX 打包选项。</param>
    /// <returns>打包结果；工具缺失时降级为结构化目录（Degraded=true，保留 StructureDirectory）。</returns>
    public async Task<MsixPackageResult> PackageAsync(MsixPackageOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MsixPackageResult
            {
                Success = false,
                ErrorMessage = "MSIX 打包仅支持 Windows 平台",
            };
        }

        ApplyDefaults(options);

        var validation = Validate(options);
        if (validation is not null)
        {
            return new MsixPackageResult { Success = false, ErrorMessage = validation };
        }

        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
        {
            return new MsixPackageResult
            {
                Success = false,
                ErrorMessage = $"MSIX 输出路径无效：{options.OutputPath}",
            };
        }

        Directory.CreateDirectory(outputDirectory);

        // 结构化目录：MakeAppx 的 /d 输入；工具缺失时作为替代产物保留。
        var structureDirectory = Path.Combine(
            outputDirectory, $"{Path.GetFileNameWithoutExtension(outputPath)}.structure");

        try
        {
            Directory.CreateDirectory(structureDirectory);
            await CreatePackageStructureAsync(options, structureDirectory);
        }
        catch (Exception ex)
        {
            return new MsixPackageResult
            {
                Success = false,
                ErrorMessage = $"生成 MSIX 包结构失败：{ex.Message}",
                StructureDirectory = structureDirectory,
            };
        }

        // 工具选择（对应 Go checkMSIXTools）：MSIX Packaging Tool 为显式 opt-in，
        // MakeAppx（UseMakeAppx 默认 true）为兜底路径。
        if (options.UseMsixPackagingTool)
        {
            var packagingTool = Packager.FindExecutableInPath(MsixPackagingToolName);
            if (packagingTool is null)
            {
                return Degraded(structureDirectory,
                    $"未在 PATH 中找到 Microsoft MSIX Packaging Tool（{MsixPackagingToolName}）；" +
                    $"已生成 MSIX 结构化目录，可安装该工具后重试或改用 MakeAppx");
            }

            var (exitCode, output) = await RunPackagingToolAsync(options, packagingTool);
            if (exitCode != 0)
            {
                return new MsixPackageResult
                {
                    Success = false,
                    ErrorMessage = $"Microsoft MSIX Packaging Tool 退出码 {exitCode}：{output}",
                    StructureDirectory = structureDirectory,
                };
            }
        }
        else
        {
            var makeAppx = FindWindowsSdkTool(MakeAppxName);
            if (makeAppx is null)
            {
                return Degraded(structureDirectory,
                    $"未在 PATH 或 Windows SDK 中找到 {MakeAppxName}；" +
                    $"已生成 MSIX 结构化目录，请安装 Windows SDK 后重试（或使用 --use-msix-tool）");
            }

            var (exitCode, output) = await CliProcess.RunAsync(
                makeAppx, "pack", "/o", "/d", structureDirectory, "/p", outputPath);
            if (exitCode != 0)
            {
                return new MsixPackageResult
                {
                    Success = false,
                    ErrorMessage = $"MakeAppx 退出码 {exitCode}：{output}",
                    StructureDirectory = structureDirectory,
                };
            }

            // 可选签名（对应 Go createMSIXWithMakeAppx 的 signtool 分支）。
            if (!string.IsNullOrWhiteSpace(options.CertificatePath))
            {
                var signTool = FindWindowsSdkTool(SignToolName);
                if (signTool is null)
                {
                    return new MsixPackageResult
                    {
                        Success = true,
                        OutputPath = outputPath,
                        Degraded = true,
                        ErrorMessage = $"未找到 {SignToolName}，MSIX 包已生成但未签名：{outputPath}",
                    };
                }

                var signArguments = new List<string>
                {
                    "sign", "/fd", "SHA256", "/a", "/f", options.CertificatePath,
                };
                if (!string.IsNullOrWhiteSpace(options.CertificatePassword))
                {
                    signArguments.Add("/p");
                    signArguments.Add(options.CertificatePassword);
                }
                signArguments.Add(outputPath);

                var (signExitCode, signOutput) = await CliProcess.RunAsync(signTool, signArguments.ToArray());
                if (signExitCode != 0)
                {
                    return new MsixPackageResult
                    {
                        Success = false,
                        ErrorMessage = $"signtool 退出码 {signExitCode}：{signOutput}",
                        StructureDirectory = structureDirectory,
                    };
                }
            }
        }

        // 成功产出后清理中间结构目录（对应 Go defer os.RemoveAll(tempDir)）。
        TryDeleteDirectory(structureDirectory);

        return new MsixPackageResult { Success = true, OutputPath = outputPath };
    }

    /// <summary>
    /// 构造工具缺失的降级结果：保留结构化目录并给出清晰错误（不得静默失败）。
    /// </summary>
    /// <param name="structureDirectory">结构化目录路径。</param>
    /// <param name="message">错误消息。</param>
    /// <returns>降级结果。</returns>
    private static MsixPackageResult Degraded(string structureDirectory, string message) => new()
    {
        Success = false,
        Degraded = true,
        ErrorMessage = message,
        StructureDirectory = structureDirectory,
    };

    /// <summary>
    /// 使用 Microsoft MSIX Packaging Tool 创建包（对应 Go <c>createMSIXWithPackagingTool</c>）。
    /// </summary>
    /// <param name="options">MSIX 打包选项。</param>
    /// <param name="toolPath">MsixPackagingTool.exe 路径。</param>
    /// <returns>(退出码, 输出)。</returns>
    private static async Task<(int ExitCode, string Output)> RunPackagingToolAsync(
        MsixPackageOptions options, string toolPath)
    {
        var templatePath = Path.Combine(Path.GetTempPath(), $"wails-msix-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(templatePath, GeneratePackagingToolTemplate(options));

            var arguments = new List<string> { "create-package", "--template", templatePath };
            if (!string.IsNullOrWhiteSpace(options.CertificatePassword))
            {
                arguments.Add("--certPassword");
                arguments.Add(options.CertificatePassword);
            }

            return await CliProcess.RunAsync(toolPath, arguments.ToArray());
        }
        finally
        {
            try
            {
                if (File.Exists(templatePath))
                {
                    File.Delete(templatePath);
                }
            }
            catch (IOException)
            {
                // 临时模板清理失败不影响打包结果。
            }
        }
    }

    /// <summary>
    /// 尽力删除目录，忽略失败。
    /// </summary>
    /// <param name="path">目录路径。</param>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 忽略清理失败（对应 Go defer os.RemoveAll 的容错语义）。
        }
        catch (UnauthorizedAccessException)
        {
            // 忽略清理失败。
        }
    }
}
