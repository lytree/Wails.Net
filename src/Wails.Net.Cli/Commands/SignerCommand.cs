using System.CommandLine;
using Wails.Net.Application.Security.Minisign;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// signer 命令：生成 minisign 签名密钥对、签名文件、验证签名。
/// 对应 Tauri v2 的 <c>tauri signer</c> 命令。
/// 使用 Ed25519 + BLAKE2b-512 签名算法（minisign 方案），与 <see cref="Wails.Net.Application.Services.Updater.SignatureVerifier"/> 配套。
/// </summary>
internal sealed class SignerCommand : CliCommandBase
{
    /// <summary>
    /// 创建 signer 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("signer", "生成和管理签名密钥（minisign / Ed25519）");

        command.Subcommands.Add(CreateGenerateCommand());
        command.Subcommands.Add(CreateSignCommand());
        command.Subcommands.Add(CreateVerifyCommand());

        return command;
    }

    /// <summary>
    /// 创建 signer generate 子命令。
    /// </summary>
    /// <returns>generate 子命令。</returns>
    private static Command CreateGenerateCommand()
    {
        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "密钥文件输出路径前缀（如 ~/.wailsnet/signing-key，将生成 .minisign.key 和 .minisign.pub）",
        };

        var command = new Command("generate", "生成 minisign 签名密钥对（Ed25519）");
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var output = parseResult.GetValue(outputOption);
            var cmd = new SignerCommand();
            return await cmd.GenerateKeyPairAsync(output);
        });

        return command;
    }

    /// <summary>
    /// 创建 signer sign 子命令。
    /// </summary>
    /// <returns>sign 子命令。</returns>
    private static Command CreateSignCommand()
    {
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "要签名的文件路径",
        };

        var keyOption = new Option<FileInfo>("--key", "-k")
        {
            Description = "私钥文件路径（.minisign.key）",
        };

        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "签名文件输出路径（默认为 {file}.minisign.sig）",
        };

        var command = new Command("sign", "使用私钥对文件签名");
        command.Arguments.Add(fileArgument);
        command.Options.Add(keyOption);
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArgument);
            var key = parseResult.GetValue(keyOption);
            var output = parseResult.GetValue(outputOption);
            var cmd = new SignerCommand();
            return await cmd.SignFileAsync(file!, key!, output);
        });

        return command;
    }

    /// <summary>
    /// 创建 signer verify 子命令。
    /// </summary>
    /// <returns>verify 子命令。</returns>
    private static Command CreateVerifyCommand()
    {
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "要验证的文件路径",
        };

        var keyOption = new Option<FileInfo>("--pubkey", "-p")
        {
            Description = "公钥文件路径（.minisign.pub）",
        };

        var sigOption = new Option<FileInfo?>("--signature", "-s")
        {
            Description = "签名文件路径（默认为 {file}.minisign.sig）",
        };

        var command = new Command("verify", "使用公钥验证文件签名");
        command.Arguments.Add(fileArgument);
        command.Options.Add(keyOption);
        command.Options.Add(sigOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArgument);
            var pubkey = parseResult.GetValue(keyOption);
            var sig = parseResult.GetValue(sigOption);
            var cmd = new SignerCommand();
            return await cmd.VerifyFileAsync(file!, pubkey!, sig);
        });

        return command;
    }

    /// <summary>
    /// 生成 minisign 密钥对（Ed25519）。
    /// </summary>
    /// <param name="output">输出路径前缀。若为 null 则使用默认路径 ~/.wailsnet/signing-key。</param>
    /// <returns>退出码。</returns>
    internal async Task<int> GenerateKeyPairAsync(FileInfo? output)
    {
        var basePath = output?.FullName;
        if (string.IsNullOrEmpty(basePath))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            basePath = Path.Combine(homeDir, ".wailsnet", "signing-key");
        }

        var keyPath = basePath + ".minisign.key";
        var pubKeyPath = basePath + ".minisign.pub";

        var keyDir = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(keyDir))
        {
            Directory.CreateDirectory(keyDir);
        }

        // 生成 Ed25519 密钥对
        var keyPair = MinisignSigner.GenerateKeyPair();

        // 写入 minisign 格式文件
        MinisignSigner.WritePrivateKeyFile(keyPath, keyPair);
        MinisignSigner.WritePublicKeyFile(pubKeyPath, keyPair);

        await Task.CompletedTask;

        Success("minisign 密钥对已生成：");
        Info($"  私钥: {keyPath}");
        Info($"  公钥: {pubKeyPath}");
        Info($"  指纹: {keyPair.KeyFingerprint}");
        Warn("请妥善保管私钥文件，不要提交到版本控制系统。");

        return 0;
    }

    /// <summary>
    /// 使用私钥对文件签名。
    /// </summary>
    /// <param name="file">要签名的文件。</param>
    /// <param name="keyFile">私钥文件（.minisign.key）。</param>
    /// <param name="output">签名文件输出路径（可选）。</param>
    /// <returns>退出码。</returns>
    internal async Task<int> SignFileAsync(FileInfo file, FileInfo keyFile, FileInfo? output)
    {
        if (!file.Exists)
        {
            Error($"文件不存在: {file.FullName}");
            return 1;
        }

        if (!keyFile.Exists)
        {
            Error($"私钥文件不存在: {keyFile.FullName}");
            return 1;
        }

        // 读取并解析私钥文件
        var keyContent = await File.ReadAllTextAsync(keyFile.FullName);
        if (!MinisignFormat.TryParsePrivateKeyFile(keyContent, out var base64PrivateKey, out _))
        {
            Error($"私钥文件格式错误: {keyFile.FullName}");
            return 1;
        }

        var keyPair = MinisignSigner.ImportKeyPair(base64PrivateKey!);

        // 对文件签名（返回 Base64 编码的完整签名）
        var base64Signature = MinisignSigner.SignFile(file.FullName, keyPair.PrivateKey);

        // 输出签名文件
        var sigPath = output?.FullName ?? (file.FullName + ".minisign.sig");
        MinisignSigner.WriteSignatureFile(sigPath, base64Signature, keyPair);

        Success("文件已签名（minisign）：");
        Info($"  文件: {file.FullName}");
        Info($"  签名: {sigPath}");
        Info($"  签名值: {base64Signature}");

        return 0;
    }

    /// <summary>
    /// 使用公钥验证文件签名。
    /// </summary>
    /// <param name="file">要验证的文件。</param>
    /// <param name="pubKeyFile">公钥文件（.minisign.pub）。</param>
    /// <param name="sigFile">签名文件（可选，默认为 {file}.minisign.sig）。</param>
    /// <returns>退出码。</returns>
    internal async Task<int> VerifyFileAsync(FileInfo file, FileInfo pubKeyFile, FileInfo? sigFile)
    {
        if (!file.Exists)
        {
            Error($"文件不存在: {file.FullName}");
            return 1;
        }

        if (!pubKeyFile.Exists)
        {
            Error($"公钥文件不存在: {pubKeyFile.FullName}");
            return 1;
        }

        var sigPath = sigFile?.FullName ?? (file.FullName + ".minisign.sig");
        if (!File.Exists(sigPath))
        {
            Error($"签名文件不存在: {sigPath}");
            return 1;
        }

        // 加载公钥和签名
        var (publicKey, keyFingerprint) = MinisignVerifier.LoadPublicKey(pubKeyFile.FullName);
        var (base64Sig, sigFingerprint) = MinisignVerifier.LoadSignature(sigPath);

        // 校验公钥指纹与签名指纹一致
        if (!string.Equals(keyFingerprint, sigFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Error("签名指纹与公钥指纹不匹配。");
            Info($"  公钥指纹: {keyFingerprint}");
            Info($"  签名指纹: {sigFingerprint}");
            return 2;
        }

        // 验证签名
        var isValid = MinisignVerifier.VerifyFile(file.FullName, base64Sig, publicKey);

        if (isValid)
        {
            await Task.CompletedTask;
            Success("签名验证通过 ✓");
            Info($"  文件: {file.FullName}");
            Info($"  签名: {sigPath}");
            Info($"  指纹: {keyFingerprint}");
            return 0;
        }

        Error("签名验证失败 ✗");
        Info($"  文件: {file.FullName}");
        Info($"  签名: {sigPath}");
        return 2;
    }
}
