using System.CommandLine;
using System.Security.Cryptography;
using System.Text;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// signer 命令：生成 RSA 签名密钥对、签名文件、验证签名。
/// 对应 Tauri v2 的 <c>tauri signer</c> 命令。
/// 使用 RSA 2048 + SHA-256 签名算法，与 <see cref="Wails.Net.Application.Services.Updater.SignatureVerifier"/> 配套。
/// </summary>
internal sealed class SignerCommand : CliCommandBase
{
    /// <summary>
    /// RSA 密钥大小（位）。
    /// </summary>
    private const int KeySize = 2048;

    /// <summary>
    /// 创建 signer 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("signer", "生成和管理签名密钥（RSA 2048）");

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
            Description = "密钥文件输出路径前缀（如 ~/.wailsnet/key，将生成 .key 和 .pub）",
        };

        var command = new Command("generate", "生成 RSA 签名密钥对");
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
            Description = "私钥文件路径",
        };

        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "签名文件输出路径（默认为 {file}.sig）",
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
            Description = "公钥文件路径",
        };

        var sigOption = new Option<FileInfo?>("--signature", "-s")
        {
            Description = "签名文件路径（默认为 {file}.sig）",
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
    /// 生成 RSA 密钥对。
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

        var keyPath = basePath + ".key";
        var pubKeyPath = basePath + ".pub";

        var keyDir = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(keyDir))
        {
            Directory.CreateDirectory(keyDir);
        }

        // 生成 RSA 密钥对
        using var rsa = RSA.Create(KeySize);
        var privateKeyPem = ExportPrivateKeyPem(rsa);
        var publicKeyPem = ExportPublicKeyPem(rsa);

        // 写入私钥（PEM 格式）
        await File.WriteAllTextAsync(keyPath, privateKeyPem);
        // 写入公钥（PEM 格式）
        await File.WriteAllTextAsync(pubKeyPath, publicKeyPem);

        Success("RSA 密钥对已生成：");
        Info($"  私钥: {keyPath}");
        Info($"  公钥: {pubKeyPath}");
        Warn("请妥善保管私钥文件，不要提交到版本控制系统。");

        return 0;
    }

    /// <summary>
    /// 使用私钥对文件签名。
    /// </summary>
    /// <param name="file">要签名的文件。</param>
    /// <param name="keyFile">私钥文件。</param>
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

        // 读取私钥
        var keyPem = await File.ReadAllTextAsync(keyFile.FullName);
        using var rsa = RSA.Create();
        ImportPrivateKeyPem(rsa, keyPem);

        // 读取文件数据并签名
        var data = await File.ReadAllBytesAsync(file.FullName);
        var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // 输出签名文件
        var sigPath = output?.FullName ?? (file.FullName + ".sig");
        await File.WriteAllBytesAsync(sigPath, signature);

        Success($"文件已签名：");
        Info($"  文件:   {file.FullName}");
        Info($"  签名:   {sigPath}");
        Info($"  签名值: {Convert.ToBase64String(signature)}");

        return 0;
    }

    /// <summary>
    /// 使用公钥验证文件签名。
    /// </summary>
    /// <param name="file">要验证的文件。</param>
    /// <param name="pubKeyFile">公钥文件。</param>
    /// <param name="sigFile">签名文件（可选，默认为 {file}.sig）。</param>
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

        var sigPath = sigFile?.FullName ?? (file.FullName + ".sig");
        if (!File.Exists(sigPath))
        {
            Error($"签名文件不存在: {sigPath}");
            return 1;
        }

        // 读取公钥
        var pubKeyPem = await File.ReadAllTextAsync(pubKeyFile.FullName);
        using var rsa = RSA.Create();
        ImportPublicKeyPem(rsa, pubKeyPem);

        // 读取文件数据和签名
        var data = await File.ReadAllBytesAsync(file.FullName);
        var signature = await File.ReadAllBytesAsync(sigPath);

        // 验证签名
        var isValid = rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (isValid)
        {
            Success("签名验证通过 ✓");
            Info($"  文件: {file.FullName}");
            Info($"  签名: {sigPath}");
            return 0;
        }

        Error("签名验证失败 ✗");
        Info($"  文件: {file.FullName}");
        Info($"  签名: {sigPath}");
        return 2;
    }

    /// <summary>
    /// 导出 RSA 私钥为 PKCS#8 PEM 格式。
    /// </summary>
    /// <param name="rsa">RSA 实例。</param>
    /// <returns>PEM 格式私钥字符串。</returns>
    internal static string ExportPrivateKeyPem(RSA rsa)
    {
        return new string(PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()));
    }

    /// <summary>
    /// 导出 RSA 公钥为 SubjectPublicKeyInfo PEM 格式。
    /// </summary>
    /// <param name="rsa">RSA 实例。</param>
    /// <returns>PEM 格式公钥字符串。</returns>
    internal static string ExportPublicKeyPem(RSA rsa)
    {
        return new string(PemEncoding.Write("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>
    /// 从 PEM 字符串导入 RSA 私钥。
    /// </summary>
    /// <param name="rsa">RSA 实例。</param>
    /// <param name="pem">PEM 格式私钥字符串。</param>
    internal static void ImportPrivateKeyPem(RSA rsa, string pem)
    {
        rsa.ImportFromPem(pem);
    }

    /// <summary>
    /// 从 PEM 字符串导入 RSA 公钥。
    /// </summary>
    /// <param name="rsa">RSA 实例。</param>
    /// <param name="pem">PEM 格式公钥字符串。</param>
    internal static void ImportPublicKeyPem(RSA rsa, string pem)
    {
        rsa.ImportFromPem(pem);
    }
}
