using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// MSIX 打包器单元测试（P2-16）。
/// 覆盖 AppxManifest.xml / MSIX Packaging Tool 模板渲染、架构映射、校验、
/// 结构化目录（文件树 + 资源尺寸）以及工具缺失降级路径。
/// 对应 Wails v3 Go 版本 internal/commands/msix.go。
/// </summary>
[NotInParallel]
public sealed class MsixPackagerTests
{
    /// <summary>
    /// 创建临时目录并返回路径。
    /// </summary>
    /// <param name="hint">目录名提示。</param>
    /// <returns>新建的临时目录路径。</returns>
    private static string CreateTempDirectory(string hint = "msix")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wailsnet-{hint}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 构造一份基础 MSIX 选项。
    /// </summary>
    /// <param name="executablePath">可执行文件路径（需已存在）。</param>
    /// <param name="outputPath">MSIX 输出路径。</param>
    /// <returns>MSIX 选项。</returns>
    private static MsixPackageOptions CreateOptions(string executablePath, string outputPath)
        => new()
        {
            ProductName = "WailsTestApp",
            ProductIdentifier = "WailsTestApp",
            ProductVersion = "1.2.3",
            ProductCompany = "WailsTest",
            ProductDescription = "Test App",
            Publisher = "CN=WailsTest",
            ProcessorArchitecture = "x64",
            ExecutableName = "WailsTestApp.exe",
            ExecutablePath = executablePath,
            OutputPath = outputPath,
        };

    /// <summary>
    /// 读取 PNG 文件的宽高（直接解析 IHDR 头，避免引入图像库依赖）。
    /// </summary>
    /// <param name="path">PNG 文件路径。</param>
    /// <returns>(宽, 高)。</returns>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }

    #region GenerateAppxManifest

    /// <summary>
    /// 清单应包含标识、版本（补 .0）与架构等关键字段。
    /// </summary>
    [Test]
    public async Task GenerateAppxManifest_ContainsIdentityAndVersionFields()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            var manifest = MsixPackager.GenerateAppxManifest(options);

            await Assert.That(manifest).Contains("Name=\"WailsTestApp\"");
            await Assert.That(manifest).Contains("Publisher=\"CN=WailsTest\"");
            await Assert.That(manifest).Contains("Version=\"1.2.3.0\"");
            await Assert.That(manifest).Contains("ProcessorArchitecture=\"x64\"");
            await Assert.That(manifest).Contains("EntryPoint=\"Windows.FullTrustApplication\"");
            await Assert.That(manifest).Contains("Executable=\"WailsTestApp.exe\"");
            await Assert.That(manifest).Contains("rescap:Capability Name=\"runFullTrust\"");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 清单应展示名/公司/描述（对应模板 Properties 节点）。
    /// </summary>
    [Test]
    public async Task GenerateAppxManifest_ContainsDisplayProperties()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            var manifest = MsixPackager.GenerateAppxManifest(options);

            await Assert.That(manifest).Contains("<DisplayName>WailsTestApp</DisplayName>");
            await Assert.That(manifest).Contains("<PublisherDisplayName>WailsTest</PublisherDisplayName>");
            await Assert.That(manifest).Contains("<Description>Test App</Description>");
            await Assert.That(manifest).Contains("<Logo>Assets\\StoreLogo.png</Logo>");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Publisher 应做 HTML 转义（对应 Go 模板的 <c>| html</c>）。
    /// </summary>
    [Test]
    public async Task GenerateAppxManifest_EscapesPublisher()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));
            options.Publisher = "CN=A&B\"C\"";

            var manifest = MsixPackager.GenerateAppxManifest(options);

            await Assert.That(manifest).Contains("Publisher=\"CN=A&amp;B&quot;C&quot;\"");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 有文件关联时应写入 fileTypeAssociation 与 documentsLibrary；无则为空。
    /// </summary>
    [Test]
    public async Task GenerateAppxManifest_FileAssociations_AreConditional()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var withoutAssociations = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            var plainManifest = MsixPackager.GenerateAppxManifest(withoutAssociations);
            await Assert.That(plainManifest).DoesNotContain("windows.fileTypeAssociation");
            await Assert.That(plainManifest).DoesNotContain("documentsLibrary");

            var withAssociations = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));
            withAssociations.FileAssociations = new[] { new MsixFileAssociation { Ext = "pdf" } };

            var manifest = MsixPackager.GenerateAppxManifest(withAssociations);
            await Assert.That(manifest).Contains("windows.fileTypeAssociation");
            await Assert.That(manifest).Contains("<uap:FileType>.pdf</uap:FileType>");
            await Assert.That(manifest).Contains("<uap:Logo>Assets\\FileIcon.png</uap:Logo>");
            await Assert.That(manifest).Contains("<uap:Capability Name=\"documentsLibrary\" />");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 自定义协议应按 windows.protocol 扩展写入。
    /// </summary>
    [Test]
    public async Task GenerateAppxManifest_Protocols_AreRendered()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));
            options.Protocols = new[] { new MsixProtocol { Scheme = "wailsnet", Description = "Wails.Net App" } };

            var manifest = MsixPackager.GenerateAppxManifest(options);

            await Assert.That(manifest).Contains("windows.protocol");
            await Assert.That(manifest).Contains("<uap:Protocol Name=\"wailsnet\">");
            await Assert.That(manifest).Contains("<uap:DisplayName>Wails.Net App</uap:DisplayName>");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region GeneratePackagingToolTemplate

    /// <summary>
    /// MSIX Packaging Tool 模板应包含 Installer/包信息/保存路径/证书节点。
    /// </summary>
    [Test]
    public async Task GeneratePackagingToolTemplate_ContainsKeyNodes()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            var template = MsixPackager.GeneratePackagingToolTemplate(options);

            await Assert.That(template).Contains("<MsixPackagingToolTemplate");
            await Assert.That(template).Contains($"Path=\"{exePath}\"");
            await Assert.That(template).Contains("PackageName=\"WailsTestApp\"");
            await Assert.That(template).Contains("ExecutableName=\"WailsTestApp.exe\"");
            await Assert.That(template).Contains("Version=\"1.2.3.0\"");
            await Assert.That(template).Contains($"<SaveLocation PackagePath=\"{Path.Combine(tempDir, "out.msix")}\" />");
            await Assert.That(template).Contains("runFullTrust");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region 架构映射与格式解析

    /// <summary>
    /// 应把 Go 风格别名（amd64/386）翻译为 MSIX 架构名。
    /// </summary>
    [Test]
    public async Task TranslateArchitecture_MapsGoAliases()
    {
        await Assert.That(MsixPackager.TranslateArchitecture("amd64")).IsEqualTo("x64");
        await Assert.That(MsixPackager.TranslateArchitecture("386")).IsEqualTo("x86");
        await Assert.That(MsixPackager.TranslateArchitecture("arm64")).IsEqualTo("arm64");
        await Assert.That(MsixPackager.TranslateArchitecture("neutral")).IsEqualTo("neutral");
    }

    /// <summary>
    /// 不支持的架构应抛出异常（对应 Go 的架构校验错误）。
    /// </summary>
    [Test]
    public async Task TranslateArchitecture_Unsupported_Throws()
    {
        await Assert.That(() => MsixPackager.TranslateArchitecture("mips"))
            .ThrowsExactly<ArgumentException>();
    }

    /// <summary>
    /// 应由目标 RID 推导架构。
    /// </summary>
    [Test]
    public async Task ResolveArchitecture_FromRid()
    {
        await Assert.That(MsixPackager.ResolveArchitecture("win-x64")).IsEqualTo("x64");
        await Assert.That(MsixPackager.ResolveArchitecture("win-arm64")).IsEqualTo("arm64");
        await Assert.That(MsixPackager.ResolveArchitecture("win-x86")).IsEqualTo("x86");
    }

    /// <summary>
    /// 空 RID 应回退到主机架构（结果为合法 MSIX 架构之一）。
    /// </summary>
    [Test]
    public async Task ResolveArchitecture_Null_UsesHostArchitecture()
    {
        var arch = MsixPackager.ResolveArchitecture(null);

        await Assert.That(arch is "x64" or "x86" or "arm" or "arm64").IsTrue();
    }

    /// <summary>
    /// 应解析 msix/appx 扩展名（大小写不敏感），其它格式返回 null。
    /// </summary>
    [Test]
    public async Task TryResolveExtension_ParsesMsixAndAppx()
    {
        await Assert.That(MsixPackager.TryResolveExtension("msix")).IsEqualTo(".msix");
        await Assert.That(MsixPackager.TryResolveExtension("MSIX")).IsEqualTo(".msix");
        await Assert.That(MsixPackager.TryResolveExtension("appx")).IsEqualTo(".appx");
        await Assert.That(MsixPackager.TryResolveExtension("zip")).IsNull();
    }

    #endregion

    #region Validate

    /// <summary>
    /// 缺少产品名称时应返回错误。
    /// </summary>
    [Test]
    public async Task Validate_MissingProductName_ReturnsError()
    {
        var options = new MsixPackageOptions { ProductName = string.Empty };

        var error = MsixPackager.Validate(options);

        await Assert.That(error).IsNotNull();
        await Assert.That(error!).Contains("产品名称");
    }

    /// <summary>
    /// 可执行文件不存在时应返回错误（对应 Go 的 os.Stat 校验）。
    /// </summary>
    [Test]
    public async Task Validate_MissingExecutable_ReturnsError()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var options = CreateOptions(
                Path.Combine(tempDir, "does-not-exist.exe"), Path.Combine(tempDir, "out.msix"));

            var error = MsixPackager.Validate(options);

            await Assert.That(error).IsNotNull();
            await Assert.That(error!).Contains("未找到可执行文件");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 证书文件不存在时应返回错误。
    /// </summary>
    [Test]
    public async Task Validate_MissingCertificate_ReturnsError()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));
            options.CertificatePath = Path.Combine(tempDir, "missing.pfx");

            var error = MsixPackager.Validate(options);

            await Assert.That(error).IsNotNull();
            await Assert.That(error!).Contains("未找到证书文件");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 选项完整时应通过校验。
    /// </summary>
    [Test]
    public async Task Validate_ValidOptions_ReturnsNull()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            var error = MsixPackager.Validate(options);

            await Assert.That(error).IsNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region CreatePackageStructureAsync

    /// <summary>
    /// 结构化目录应包含 AppxManifest.xml、可执行文件与正确尺寸的资源图标。
    /// </summary>
    [Test]
    public async Task CreatePackageStructureAsync_CreatesManifestExecutableAndAssets()
    {
        var tempDir = CreateTempDirectory();
        var structureDir = Path.Combine(tempDir, "structure");
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy-exe");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            await MsixPackager.CreatePackageStructureAsync(options, structureDir);

            var manifestPath = Path.Combine(structureDir, "AppxManifest.xml");
            await Assert.That(File.Exists(manifestPath)).IsTrue();
            var manifest = await File.ReadAllTextAsync(manifestPath);
            await Assert.That(manifest).Contains("Name=\"WailsTestApp\"");
            await Assert.That(manifest).Contains("Version=\"1.2.3.0\"");

            await Assert.That(File.Exists(Path.Combine(structureDir, "WailsTestApp.exe"))).IsTrue();

            var storeLogo = Path.Combine(structureDir, "Assets", "StoreLogo.png");
            await Assert.That(File.Exists(storeLogo)).IsTrue();
            await Assert.That(ReadPngSize(storeLogo)).IsEqualTo((50, 50));

            var splash = Path.Combine(structureDir, "Assets", "SplashScreen.png");
            await Assert.That(ReadPngSize(splash)).IsEqualTo((620, 300));

            var wide = Path.Combine(structureDir, "Assets", "Wide310x150Logo.png");
            await Assert.That(ReadPngSize(wide)).IsEqualTo((310, 150));

            // 无文件关联时不生成 FileIcon.png
            await Assert.That(File.Exists(Path.Combine(structureDir, "Assets", "FileIcon.png"))).IsFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 存在文件关联时应额外生成 44x44 的 FileIcon.png。
    /// </summary>
    [Test]
    public async Task CreatePackageStructureAsync_FileAssociations_AddsFileIcon()
    {
        var tempDir = CreateTempDirectory();
        var structureDir = Path.Combine(tempDir, "structure");
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy-exe");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));
            options.FileAssociations = new[] { new MsixFileAssociation { Ext = "pdf" } };

            await MsixPackager.CreatePackageStructureAsync(options, structureDir);

            var fileIcon = Path.Combine(structureDir, "Assets", "FileIcon.png");
            await Assert.That(File.Exists(fileIcon)).IsTrue();
            await Assert.That(ReadPngSize(fileIcon)).IsEqualTo((44, 44));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region PackageAsync

    /// <summary>
    /// 缺少可执行文件时 PackageAsync 应返回失败（校验先于工具调用）。
    /// </summary>
    [Test]
    public async Task PackageAsync_MissingExecutable_ReturnsValidationFailure()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var options = CreateOptions(
                Path.Combine(tempDir, "missing.exe"), Path.Combine(tempDir, "out.msix"));

            var result = await new MsixPackager().PackageAsync(options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.ErrorMessage).Contains("未找到可执行文件");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// MakeAppx 缺失时应降级：保留结构化目录（AppxManifest.xml + 资源）并给出清晰错误。
    /// 本机已安装 Windows SDK 时该路径不可复现，测试跳过。
    /// </summary>
    [Test]
    public async Task PackageAsync_MakeAppxMissing_ProducesStructureFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("MSIX 打包仅支持 Windows");
            return;
        }

        if (MsixPackager.FindWindowsSdkTool("MakeAppx.exe") is not null)
        {
            Skip.Test("本机已安装 Windows SDK（含 MakeAppx.exe），无法复现降级路径");
            return;
        }

        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy-exe");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));

            var result = await new MsixPackager().PackageAsync(options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Degraded).IsTrue();
            await Assert.That(result.StructureDirectory).IsNotNull();
            await Assert.That(result.ErrorMessage).Contains("MakeAppx");
            await Assert.That(File.Exists(Path.Combine(result.StructureDirectory!, "AppxManifest.xml"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(result.StructureDirectory!, "WailsTestApp.exe"))).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Microsoft MSIX Packaging Tool 缺失时应降级为结构化目录（opt-in 工具通常未安装）。
    /// </summary>
    [Test]
    public async Task PackageAsync_PackagingToolMissing_ProducesStructureFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("MSIX 打包仅支持 Windows");
            return;
        }

        if (Packager.FindExecutableInPath("MsixPackagingTool.exe") is not null)
        {
            Skip.Test("本机已安装 Microsoft MSIX Packaging Tool，无法复现降级路径");
            return;
        }

        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy-exe");
            var options = CreateOptions(exePath, Path.Combine(tempDir, "out.msix"));
            options.UseMsixPackagingTool = true;

            var result = await new MsixPackager().PackageAsync(options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Degraded).IsTrue();
            await Assert.That(result.StructureDirectory).IsNotNull();
            await Assert.That(result.ErrorMessage).Contains("MSIX Packaging Tool");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 存在 MakeAppx 时端到端产出 .msix（验证清单 + 资源可通过 MakeAppx 校验）。
    /// </summary>
    [Test]
    public async Task PackageAsync_WithMakeAppx_ProducesMsix()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("MSIX 打包仅支持 Windows");
            return;
        }

        if (MsixPackager.FindWindowsSdkTool("MakeAppx.exe") is null)
        {
            Skip.Test("未安装 Windows SDK（缺少 MakeAppx.exe）");
            return;
        }

        var tempDir = CreateTempDirectory();
        try
        {
            var exePath = Path.Combine(tempDir, "WailsTestApp.exe");
            File.WriteAllText(exePath, "dummy-exe");
            var outputPath = Path.Combine(tempDir, "WailsTestApp.msix");
            var options = CreateOptions(exePath, outputPath);

            var result = await new MsixPackager().PackageAsync(options);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.OutputPath).IsEqualTo(Path.GetFullPath(outputPath));
            await Assert.That(File.Exists(result.OutputPath!)).IsTrue();
            await Assert.That(new FileInfo(result.OutputPath!).Length).IsGreaterThan(0L);

            // 成功后应清理中间结构目录
            var structureDirectory = Path.Combine(tempDir, "WailsTestApp.structure");
            await Assert.That(Directory.Exists(structureDirectory)).IsFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion
}
