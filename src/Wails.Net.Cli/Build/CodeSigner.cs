using System.Diagnostics;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 签名后端类型。
/// 对应 Tauri v2 的 Windows 代码签名支持（signtool / AzureSignTool）。
/// </summary>
public enum SignBackend
{
    /// <summary>Windows signtool.exe（PFX 证书）。</summary>
    Signtool,

    /// <summary>AzureSignTool（Azure Key Vault 证书）。</summary>
    AzureSignTool,
}

/// <summary>
/// 代码签名选项。
/// </summary>
public sealed class SignOptions
{
    /// <summary>签名后端。</summary>
    public SignBackend Backend { get; set; } = SignBackend.Signtool;

    /// <summary>PFX 证书路径（Signtool 后端）。</summary>
    public string? CertificatePath { get; set; }

    /// <summary>PFX 密码（Signtool 后端）。</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>时间戳服务器 URL（默认 DigiCert）。</summary>
    public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Azure Key Vault URL（AzureSignTool 后端）。</summary>
    public string? KeyVaultUrl { get; set; }

    /// <summary>Azure Key Vault 证书名（AzureSignTool 后端）。</summary>
    public string? KeyVaultCertificateName { get; set; }

    /// <summary>Azure 客户端 ID（AzureSignTool 后端）。</summary>
    public string? AzureClientId { get; set; }

    /// <summary>Azure 客户端密钥（AzureSignTool 后端）。</summary>
    public string? AzureClientSecret { get; set; }

    /// <summary>Azure 租户 ID（AzureSignTool 后端）。</summary>
    public string? AzureTenantId { get; set; }
}

/// <summary>
/// 代码签名结果。
/// </summary>
public sealed class SignResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>错误消息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>已签名的文件路径。</summary>
    public string? SignedFilePath { get; set; }
}

/// <summary>
/// 代码签名器，对可执行文件执行 Authenticode 签名。
/// 对应 Tauri v2 的 Windows 代码签名能力，支持 signtool 和 AzureSignTool 两种后端。
/// </summary>
public sealed class CodeSigner
{
    /// <summary>
    /// 从环境变量解析签名选项（CI 友好）。
    /// 全部缺失时返回 null（表示未配置签名）。
    /// </summary>
    /// <remarks>
    /// 识别环境变量：
    /// - WAILS_SIGN_BACKEND=signtool|azuresigntool（不设默认不签名）
    /// - WAILS_SIGN_CERT_PATH / WAILS_SIGN_CERT_PASSWORD（signtool）
    /// - WAILS_SIGN_AKV_URL / WAILS_SIGN_AKV_CERT（azuresigntool）
    /// - WAILS_SIGN_AZURE_CLIENT_ID / WAILS_SIGN_AZURE_CLIENT_SECRET / WAILS_SIGN_AZURE_TENANT_ID（azuresigntool）
    /// - WAILS_SIGN_TIMESTAMP_URL（可选，默认 http://timestamp.digicert.com）
    /// </remarks>
    /// <returns>签名选项；未配置签名时返回 null。</returns>
    public static SignOptions? ResolveFromEnvironment()
    {
        var backend = Environment.GetEnvironmentVariable("WAILS_SIGN_BACKEND");
        if (string.IsNullOrEmpty(backend))
        {
            return null;
        }

        var options = new SignOptions
        {
            TimestampUrl = Environment.GetEnvironmentVariable("WAILS_SIGN_TIMESTAMP_URL")
                ?? "http://timestamp.digicert.com",
        };

        if (backend.Equals("azuresigntool", StringComparison.OrdinalIgnoreCase))
        {
            options.Backend = SignBackend.AzureSignTool;
            options.KeyVaultUrl = Environment.GetEnvironmentVariable("WAILS_SIGN_AKV_URL");
            options.KeyVaultCertificateName = Environment.GetEnvironmentVariable("WAILS_SIGN_AKV_CERT");
            options.AzureClientId = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_ID");
            options.AzureClientSecret = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_SECRET");
            options.AzureTenantId = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_TENANT_ID");
        }
        else
        {
            options.Backend = SignBackend.Signtool;
            options.CertificatePath = Environment.GetEnvironmentVariable("WAILS_SIGN_CERT_PATH");
            options.CertificatePassword = Environment.GetEnvironmentVariable("WAILS_SIGN_CERT_PASSWORD");
        }

        return options;
    }

    /// <summary>
    /// 对指定可执行文件执行 Authenticode 签名。
    /// </summary>
    /// <param name="filePath">要签名的文件路径。</param>
    /// <param name="options">签名选项。</param>
    /// <returns>签名结果。</returns>
    public async Task<SignResult> SignAsync(string filePath, SignOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(options);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Authenticode 签名只能在 Windows 上执行");
        }

        if (!File.Exists(filePath))
        {
            return new SignResult
            {
                Success = false,
                ErrorMessage = $"文件不存在：{filePath}",
            };
        }

        string? toolPath;
        string arguments;

        if (options.Backend == SignBackend.AzureSignTool)
        {
            toolPath = Packager.FindExecutableInPath("AzureSignTool.exe");
            if (toolPath is null)
            {
                throw new FileNotFoundException(
                    "未找到 AzureSignTool.exe，请安装 AzureSignTool 并确保其在 PATH 中");
            }
            arguments = BuildAzureSignToolArgs(filePath, options);
        }
        else
        {
            toolPath = FindSigntool();
            if (toolPath is null)
            {
                throw new FileNotFoundException(
                    "未找到 signtool.exe，请安装 Windows SDK 并确保其在 PATH 中");
            }
            arguments = BuildSigntoolArgs(filePath, options);
        }

        try
        {
            var (exitCode, output) = await RunProcessAsync(toolPath, arguments);
            if (exitCode != 0)
            {
                return new SignResult
                {
                    Success = false,
                    ErrorMessage = $"签名工具退出码 {exitCode}：{output}",
                };
            }

            return new SignResult
            {
                Success = true,
                SignedFilePath = filePath,
            };
        }
        catch (Exception ex)
        {
            return new SignResult
            {
                Success = false,
                ErrorMessage = $"签名执行失败：{ex.Message}",
            };
        }
    }

    /// <summary>
    /// 构建 signtool.exe 命令行参数。
    /// </summary>
    /// <param name="filePath">要签名的文件路径。</param>
    /// <param name="options">签名选项。</param>
    /// <returns>signtool 命令行参数字符串。</returns>
    internal static string BuildSigntoolArgs(string filePath, SignOptions options)
    {
        var password = options.CertificatePassword ?? string.Empty;
        return $"sign /f \"{options.CertificatePath}\" /p \"{password}\" /tr \"{options.TimestampUrl}\" /td sha256 /fd sha256 \"{filePath}\"";
    }

    /// <summary>
    /// 构建 AzureSignTool 命令行参数。
    /// </summary>
    /// <param name="filePath">要签名的文件路径。</param>
    /// <param name="options">签名选项。</param>
    /// <returns>AzureSignTool 命令行参数字符串。</returns>
    internal static string BuildAzureSignToolArgs(string filePath, SignOptions options)
    {
        return $"sign -kvu \"{options.KeyVaultUrl}\" -kvc \"{options.KeyVaultCertificateName}\" " +
               $"-kvi \"{options.AzureClientId}\" -kvs \"{options.AzureClientSecret}\" " +
               $"-kvt \"{options.AzureTenantId}\" -tr \"{options.TimestampUrl}\" " +
               $"-td sha256 -fd sha256 \"{filePath}\"";
    }

    /// <summary>
    /// 查找 signtool.exe（PATH 中或 Windows SDK 常见位置）。
    /// </summary>
    private static string? FindSigntool()
    {
        var path = Packager.FindExecutableInPath("signtool.exe");
        if (path is not null) return path;

        // Windows SDK 常见安装位置
        var sdkBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits", "10", "bin");
        if (Directory.Exists(sdkBase))
        {
            foreach (var verDir in Directory.GetDirectories(sdkBase))
            {
                var candidate = Path.Combine(verDir, Environment.Is64BitOperatingSystem ? "x64" : "x86", "signtool.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 运行外部进程并返回退出码与输出。
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
        return (process.ExitCode, output);
    }
}
