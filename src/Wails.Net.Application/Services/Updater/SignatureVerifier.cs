using System.Diagnostics;
using System.Text.Json;
using Wails.Net.Application.Security.Minisign;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// 更新包签名验证结果。
/// </summary>
public sealed class SignatureVerifyResult
{
    /// <summary>
    /// 获取一个值，指示签名是否有效。
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 获取签名者名称，可为 null。
    /// </summary>
    public string? SignerName { get; init; }

    /// <summary>
    /// 获取错误消息，可为 null。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 获取 minisign 公钥指纹（验证通过时填充），可为 null。
    /// 对应 Tauri v2 updater 的密钥指纹。
    /// </summary>
    public string? Fingerprint { get; init; }
}

/// <summary>
/// 更新包签名验证器。
/// 对应 Tauri v2 的更新签名验证机制（minisign 方案）。
/// <para>
/// 支持两种验证路径：
/// <list type="number">
/// <item><see cref="VerifyMinisignAsync"/> — minisign 路径（推荐，对应 Tauri v2 updater）</item>
/// <item><see cref="VerifyAsync"/> — 旧路径（Authenticode/GPG，保留向后兼容，已标记 [Obsolete]）</item>
/// </list>
/// </para>
/// </summary>
public sealed class SignatureVerifier
{
    private readonly UpdaterConfig _config;

    /// <summary>
    /// 初始化 <see cref="SignatureVerifier"/>。
    /// </summary>
    /// <param name="config">更新服务配置，提供 <see cref="UpdaterConfig.TrustedPublicKey"/> 等参数。</param>
    public SignatureVerifier(UpdaterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <summary>
    /// 使用 minisign 验证文件签名。
    /// 对应 Tauri v2 的更新签名验证流程：BLAKE2b-512 + Ed25519 验签。
    /// </summary>
    /// <param name="filePath">要验证的文件路径。</param>
    /// <param name="base64Signature">Base64 编码的 minisign 签名（来自 <see cref="UpdateManifest.Signature"/>）。</param>
    /// <param name="trustedPublicKey">信任的 Ed25519 公钥（32 字节）。为 null 时从 <see cref="UpdaterConfig.TrustedPublicKey"/> 加载。</param>
    /// <returns>签名验证结果。验证通过时 <see cref="SignatureVerifyResult.Fingerprint"/> 填充公钥指纹。</returns>
    public Task<SignatureVerifyResult> VerifyMinisignAsync(string filePath, string base64Signature, byte[]? trustedPublicKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentException.ThrowIfNullOrEmpty(base64Signature);

        if (!File.Exists(filePath))
        {
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"文件不存在: {filePath}"
            });
        }

        // 优先使用参数传入的公钥，否则从配置加载
        var publicKey = trustedPublicKey ?? LoadTrustedPublicKey();
        if (publicKey is null || publicKey.Length != 32)
        {
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = "未配置信任的 minisign 公钥，或公钥长度不为 32 字节。请在 UpdaterConfig.TrustedPublicKey 中配置。"
            });
        }

        try
        {
            var isValid = MinisignVerifier.VerifyFile(filePath, base64Signature, publicKey);
            if (!isValid)
            {
                return Task.FromResult(new SignatureVerifyResult
                {
                    IsValid = false,
                    ErrorMessage = "minisign 签名验证失败：签名无效或数据已被篡改。"
                });
            }

            // 计算公钥指纹用于结果展示
            var fingerprint = MinisignKeyPair.ComputeFingerprint(publicKey);
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = true,
                Fingerprint = fingerprint,
                SignerName = fingerprint
            });
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"文件不存在: {ex.FileName}"
            });
        }
        catch (FormatException ex)
        {
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"签名格式错误: {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"minisign 验证异常: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 异步验证文件签名（旧路径：Authenticode / GPG）。
    /// Windows：验证 Authenticode 签名（通过 powershell Get-AuthenticodeSignature）。
    /// Linux：验证 GPG 签名（通过 gpg --verify）。
    /// 其他平台：返回 IsValid=false, ErrorMessage="不支持的平台"。
    /// 如果 expectedSigner 不为 null，校验签名者是否匹配。
    /// </summary>
    /// <param name="filePath">要验证的文件路径。</param>
    /// <param name="expectedSigner">期望的签名者（可选）。Windows 上与 SignerCertificate.Subject 匹配；Linux 上与 GOODSIG 行匹配。</param>
    /// <returns>签名验证结果。</returns>
    [Obsolete("使用 VerifyMinisignAsync 替代。保留用于向后兼容 Authenticode/GPG 验证。")]
    public Task<SignatureVerifyResult> VerifyAsync(string filePath, string? expectedSigner = null)
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"文件不存在: {filePath}"
            });
        }

        if (OperatingSystem.IsWindows())
        {
            return VerifyAuthenticodeAsync(filePath, expectedSigner);
        }

        if (OperatingSystem.IsLinux())
        {
            return VerifyGpgAsync(filePath, expectedSigner);
        }

        return Task.FromResult(new SignatureVerifyResult
        {
            IsValid = false,
            ErrorMessage = "不支持的平台：仅支持 Windows 和 Linux。"
        });
    }

    /// <summary>
    /// 从配置加载信任的 minisign 公钥。
    /// 支持 Base64 字符串或公钥文件路径两种格式。
    /// </summary>
    /// <returns>公钥字节（32 字节），若未配置则返回 null。</returns>
    private byte[]? LoadTrustedPublicKey()
    {
        if (string.IsNullOrEmpty(_config.TrustedPublicKey))
        {
            return null;
        }

        // 优先按文件路径加载（minisign 公钥文件格式）
        if (File.Exists(_config.TrustedPublicKey))
        {
            try
            {
                var (publicKey, _) = MinisignVerifier.LoadPublicKey(_config.TrustedPublicKey);
                return publicKey;
            }
            catch
            {
                // 文件存在但解析失败，回退到 Base64 解析
            }
        }

        // 尝试作为 Base64 字符串解析
        try
        {
            return Convert.FromBase64String(_config.TrustedPublicKey);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在 Windows 上通过 PowerShell 的 Get-AuthenticodeSignature 验证 Authenticode 签名。
    /// 解析 ConvertTo-Json 输出，检查 Status 是否为 Valid。
    /// </summary>
    /// <param name="filePath">要验证的文件路径。</param>
    /// <param name="expectedSigner">期望的签名者（可选）。</param>
    /// <returns>签名验证结果。</returns>
    private async Task<SignatureVerifyResult> VerifyAuthenticodeAsync(string filePath, string? expectedSigner)
    {
        // 转义路径中的单引号（PowerShell 单引号字符串中的转义方式为 ''）
        var escapedPath = filePath.Replace("'", "''");
        var command = $"Get-AuthenticodeSignature -FilePath '{escapedPath}' | ConvertTo-Json -Depth 4";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!TryStartProcess(process, out var startError))
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"无法启动 PowerShell: {startError}"
            };
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"PowerShell 调用失败: {stderr.Trim()}"
            };
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = "PowerShell 未返回任何输出。"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Status", out var statusProp))
            {
                return new SignatureVerifyResult
                {
                    IsValid = false,
                    ErrorMessage = "无法从 PowerShell 输出中获取签名状态。"
                };
            }

            var status = statusProp.ValueKind == JsonValueKind.String
                ? statusProp.GetString() ?? string.Empty
                : statusProp.ValueKind == JsonValueKind.Number && statusProp.TryGetInt32(out var statusNum)
                    ? (statusNum == 0 ? "Valid" : $"Invalid({statusNum})")
                    : string.Empty;

            string? signerName = null;
            if (root.TryGetProperty("SignerCertificate", out var cert) &&
                cert.ValueKind == JsonValueKind.Object &&
                cert.TryGetProperty("Subject", out var subjectProp))
            {
                signerName = subjectProp.GetString();
            }

            if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
            {
                return new SignatureVerifyResult
                {
                    IsValid = false,
                    SignerName = signerName,
                    ErrorMessage = $"签名状态无效: {status}"
                };
            }

            if (expectedSigner is not null &&
                (signerName is null || !signerName.Contains(expectedSigner, StringComparison.OrdinalIgnoreCase)))
            {
                return new SignatureVerifyResult
                {
                    IsValid = false,
                    SignerName = signerName,
                    ErrorMessage = $"签名者不匹配。期望: {expectedSigner}，实际: {signerName ?? "(未知)"}"
                };
            }

            return new SignatureVerifyResult
            {
                IsValid = true,
                SignerName = signerName
            };
        }
        catch (JsonException ex)
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"解析 PowerShell 输出失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 在 Linux 上通过 gpg --verify 验证 GPG 签名。
    /// 签名文件路径自动推导：优先尝试 {filePath}.sig，其次 {filePath}.asc。
    /// 通过 --status-fd 1 输出 GOODSIG 行提取签名者名称。
    /// </summary>
    /// <param name="filePath">要验证的文件路径。</param>
    /// <param name="expectedSigner">期望的签名者（可选）。</param>
    /// <returns>签名验证结果。</returns>
    private async Task<SignatureVerifyResult> VerifyGpgAsync(string filePath, string? expectedSigner)
    {
        var sigFile = $"{filePath}.sig";
        if (!File.Exists(sigFile))
        {
            sigFile = $"{filePath}.asc";
            if (!File.Exists(sigFile))
            {
                return new SignatureVerifyResult
                {
                    IsValid = false,
                    ErrorMessage = $"未找到签名文件: {filePath}.sig 或 {filePath}.asc"
                };
            }
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gpg",
                Arguments = $"--verify --status-fd 1 \"{sigFile}\" \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!TryStartProcess(process, out var startError))
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"无法启动 gpg: {startError}"
            };
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        _ = await stderrTask;

        if (process.ExitCode != 0)
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                ErrorMessage = $"GPG 验证失败，退出码: {process.ExitCode}"
            };
        }

        // 从 status-fd 输出中提取 GOODSIG 行的签名者
        // GOODSIG 格式: [GNUPG:] GOODSIG <key_id> <signer_name>
        string? signerName = null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[GNUPG:] GOODSIG", StringComparison.Ordinal))
            {
                var parts = trimmed.Split(' ', 4);
                if (parts.Length >= 4)
                {
                    signerName = parts[3];
                }
                break;
            }
        }

        if (expectedSigner is not null &&
            (signerName is null || !signerName.Contains(expectedSigner, StringComparison.OrdinalIgnoreCase)))
        {
            return new SignatureVerifyResult
            {
                IsValid = false,
                SignerName = signerName,
                ErrorMessage = $"签名者不匹配。期望: {expectedSigner}，实际: {signerName ?? "(未知)"}"
            };
        }

        return new SignatureVerifyResult
        {
            IsValid = true,
            SignerName = signerName
        };
    }

    /// <summary>
    /// 尝试启动进程，捕获启动失败异常。
    /// </summary>
    /// <param name="process">要启动的进程。</param>
    /// <param name="errorMessage">启动失败时的错误消息。</param>
    /// <returns>启动成功返回 true；启动失败返回 false 并输出错误消息。</returns>
    private static bool TryStartProcess(Process process, out string errorMessage)
    {
        try
        {
            process.Start();
            errorMessage = string.Empty;
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
