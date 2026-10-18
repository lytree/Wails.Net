namespace Wails.Net.Cli.Build;

/// <summary>
/// MSIX 打包选项。
/// 对应 Wails v3 Go 版本 internal/commands/msix.go 中的 <c>MSIXOptions</c> 结构
/// （产品信息来自 WailsConfig，其余字段来自 internal/flags/msix.go 的 <c>ToolMSIX</c>）。
/// </summary>
public sealed class MsixPackageOptions
{
    /// <summary>产品名称（显示名）。对应 Go <c>MSIXOptions.WailsConfig.Info.ProductName</c>。</summary>
    public string ProductName { get; set; } = "WailsApp";

    /// <summary>产品标识符（清单 Identity/@Name 与 Application/@Id）。对应 Go <c>Info.ProductIdentifier</c>。</summary>
    public string ProductIdentifier { get; set; } = "WailsApp";

    /// <summary>产品版本（三段式，渲染时追加 <c>.0</c> 成为 MSIX 要求的四段版本）。对应 Go <c>Info.Version</c>。</summary>
    public string ProductVersion { get; set; } = "1.0.0";

    /// <summary>公司名称（清单 PublisherDisplayName）。对应 Go <c>Info.CompanyName</c>。</summary>
    public string ProductCompany { get; set; } = "Wails.Net";

    /// <summary>产品描述。对应 Go <c>Info.Description</c>。</summary>
    public string? ProductDescription { get; set; }

    /// <summary>发布者标识（如 <c>CN=CompanyName</c>），缺省为 <c>CN={ProductCompany}</c>。对应 Go <c>flags.ToolMSIX.Publisher</c>。</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>处理器架构（x64/x86/arm/arm64/x86a64/neutral）。对应 Go <c>flags.ToolMSIX.Arch</c>。</summary>
    public string ProcessorArchitecture { get; set; } = "x64";

    /// <summary>包内可执行文件名（清单 Application/@Executable）。对应 Go <c>flags.ToolMSIX.ExecutableName</c>。</summary>
    public string ExecutableName { get; set; } = string.Empty;

    /// <summary>源可执行文件完整路径。对应 Go <c>flags.ToolMSIX.ExecutablePath</c>。</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>MSIX/Appx 输出文件路径。对应 Go <c>flags.ToolMSIX.OutputPath</c>。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>签名证书路径（可选，.pfx）。对应 Go <c>flags.ToolMSIX.CertificatePath</c>。</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>签名证书密码（可选）。对应 Go <c>flags.ToolMSIX.CertificatePassword</c>。</summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>是否使用 Microsoft MSIX Packaging Tool 打包。对应 Go <c>flags.ToolMSIX.UseMsixPackagingTool</c>（默认 false）。</summary>
    public bool UseMsixPackagingTool { get; set; }

    /// <summary>是否使用 MakeAppx.exe 打包。对应 Go <c>flags.ToolMSIX.UseMakeAppx</c>（默认 true）。</summary>
    public bool UseMakeAppx { get; set; } = true;

    /// <summary>文件关联列表（可选，为空则不写入相关清单节点）。对应 Go <c>WailsConfig.FileAssociations</c>。</summary>
    public IReadOnlyList<MsixFileAssociation> FileAssociations { get; set; } = Array.Empty<MsixFileAssociation>();

    /// <summary>自定义协议列表（可选）。对应 Go <c>WailsConfig.Protocols</c>。</summary>
    public IReadOnlyList<MsixProtocol> Protocols { get; set; } = Array.Empty<MsixProtocol>();
}

/// <summary>
/// MSIX 文件关联项（清单模板中的 <c>.Ext</c> 字段）。
/// </summary>
public sealed class MsixFileAssociation
{
    /// <summary>文件扩展名（不含前导点，如 <c>pdf</c>）。</summary>
    public string Ext { get; set; } = string.Empty;
}

/// <summary>
/// MSIX 自定义协议项（清单模板中的 <c>.Scheme</c> / <c>.Description</c> 字段）。
/// </summary>
public sealed class MsixProtocol
{
    /// <summary>协议 Scheme（如 <c>wailsnet</c>）。</summary>
    public string Scheme { get; set; } = string.Empty;

    /// <summary>协议显示描述。</summary>
    public string Description { get; set; } = string.Empty;
}
