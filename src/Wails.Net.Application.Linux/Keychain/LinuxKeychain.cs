using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Wails.Net.Application.Plugins.Keychain;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux Secret Service 钥匙串实现。
/// 对应 Wails v3 Go 版本 <c>internal/keychain/keychain_linux.go</c>，
/// 通过 libsecret 的命令行工具 <c>secret-tool</c> 读写 Freedesktop Secret Service。
/// </summary>
/// <remarks>
/// 使用命令行工具而非直接绑定 libsecret 的原因：
/// <list type="bullet">
/// <item>避免引入额外的原生依赖（libsecret-1.so 及其 Gir 绑定）；</item>
/// <item><c>secret-tool</c> 使用 D-Bus Secret Service API，与 GNOME Keyring / KWallet 兼容。</item>
/// </list>
/// 凭据以 <c>service</c> 与 <c>account</c> 作为 Secret Service 属性存储；
/// 所有进程参数通过 <see cref="ProcessStartInfo.ArgumentList"/> 传递，不经 shell，避免注入。
/// </remarks>
public sealed class LinuxKeychain : IPlatformKeychain
{
    /// <summary>
    /// Secret Service 命令行工具名称。
    /// </summary>
    private const string ToolName = "secret-tool";

    private readonly ILinuxSecretToolRunner _runner;

    /// <summary>
    /// 使用默认的 <c>secret-tool</c> 进程运行器构造实例。
    /// </summary>
    public LinuxKeychain()
        : this(new ProcessSecretToolRunner())
    {
    }

    /// <summary>
    /// 使用自定义运行器构造实例（供单元测试注入）。
    /// </summary>
    /// <param name="runner">secret-tool 运行器。</param>
    internal LinuxKeychain(ILinuxSecretToolRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public bool SetPassword(string service, string account, string password)
    {
        if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(account))
        {
            return false;
        }

        var result = _runner.Run(
            [
                "store",
                "--label", $"Wails.Net:{service}",
                "service", service,
                "account", account,
            ],
            password ?? string.Empty);

        return !result.ToolMissing && result.ExitCode == 0;
    }

    /// <inheritdoc />
    public string? GetPassword(string service, string account)
    {
        if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(account))
        {
            return null;
        }

        var result = _runner.Run(
            [
                "lookup",
                "service", service,
                "account", account,
            ],
            standardInput: null);

        if (result.ToolMissing || result.ExitCode != 0)
        {
            return null;
        }

        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    /// <inheritdoc />
    public bool DeletePassword(string service, string account)
    {
        if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(account))
        {
            return false;
        }

        var result = _runner.Run(
            [
                "clear",
                "service", service,
                "account", account,
            ],
            standardInput: null);

        // secret-tool clear 在条目不存在时返回 1；按接口语义视为删除成功。
        return !result.ToolMissing && result.ExitCode is 0 or 1;
    }
}

/// <summary>
/// <c>secret-tool</c> 调用结果。
/// </summary>
/// <param name="ExitCode">退出码；-1 表示未能启动。</param>
/// <param name="StandardOutput">标准输出。</param>
/// <param name="StandardError">标准错误。</param>
/// <param name="ToolMissing">工具不存在（未安装）时为 <c>true</c>。</param>
internal sealed record LinuxSecretToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool ToolMissing);

/// <summary>
/// <c>secret-tool</c> 进程运行器抽象（便于单元测试替换）。
/// </summary>
internal interface ILinuxSecretToolRunner
{
    /// <summary>
    /// 运行 <c>secret-tool</c>。
    /// </summary>
    /// <param name="args">命令行参数（不含可执行文件名）。</param>
    /// <param name="standardInput">标准输入内容；null 表示不写入。</param>
    /// <returns>调用结果。</returns>
    LinuxSecretToolResult Run(IReadOnlyList<string> args, string? standardInput);
}

/// <summary>
/// 基于 <see cref="Process"/> 的 <c>secret-tool</c> 运行器。
/// </summary>
internal sealed class ProcessSecretToolRunner : ILinuxSecretToolRunner
{
    /// <summary>单次调用的等待上限（毫秒）。</summary>
    private const int TimeoutMilliseconds = 5000;

    /// <inheritdoc />
    public LinuxSecretToolResult Run(IReadOnlyList<string> args, string? standardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "secret-tool",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new LinuxSecretToolResult(-1, string.Empty, "无法启动 secret-tool", ToolMissing: false);
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // 进程已退出时忽略
                }

                return new LinuxSecretToolResult(-1, stdout.GetAwaiter().GetResult(), "secret-tool 超时", ToolMissing: false);
            }

            return new LinuxSecretToolResult(
                process.ExitCode,
                stdout.GetAwaiter().GetResult(),
                stderr.GetAwaiter().GetResult(),
                ToolMissing: false);
        }
        catch (Win32Exception)
        {
            // secret-tool 未安装
            return new LinuxSecretToolResult(-1, string.Empty, "secret-tool 未安装", ToolMissing: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new LinuxSecretToolResult(-1, string.Empty, ex.Message, ToolMissing: false);
        }
    }
}
