using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// deploy 命令：部署应用到连接的设备。
/// 对应 Tauri v2 的 <c>tauri android deploy</c> 命令。
/// 支持 Android 平台（通过 adb 部署 APK）。
/// </summary>
internal sealed class DeployCommand : CliCommandBase
{
    /// <summary>
    /// Android 包名正则：用于从 APK 文件名中提取包名（<c>com.example.app</c> 格式）。
    /// </summary>
    private static readonly Regex PackageNameRegex = new(
        @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$",
        RegexOptions.Compiled);

    /// <summary>
    /// 创建 deploy 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var platformOption = new Option<string>("--platform")
        {
            Description = "目标平台（目前仅支持 android）",
        };
        platformOption.DefaultValueFactory = _ => "android";

        var apkArgument = new Argument<string?>("apk-path")
        {
            Description = "APK 文件路径（若未指定则自动查找 bin/Release 下的最新 APK）",
        };

        var deviceOption = new Option<string?>("--device")
        {
            Description = "目标设备序列号（使用 deploy list 查看已连接设备）",
        };

        var startOption = new Option<bool>("--start")
        {
            Description = "安装后自动启动应用",
        };

        var packageNameOption = new Option<string?>("--package")
        {
            Description = "应用包名（用于 --start 启动，如 com.example.app）",
        };

        var command = new Command("deploy", "部署应用到连接的设备")
        {
            platformOption,
            apkArgument,
            deviceOption,
            startOption,
            packageNameOption,
        };

        command.Subcommands.Add(CreateListCommand());

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var platform = parseResult.GetValue(platformOption) ?? "android";
            var apkPath = parseResult.GetValue(apkArgument);
            var device = parseResult.GetValue(deviceOption);
            var start = parseResult.GetValue(startOption);
            var packageName = parseResult.GetValue(packageNameOption);

            var cmd = new DeployCommand();
            return await cmd.DeployAsync(platform, apkPath, device, start, packageName);
        });

        return command;
    }

    /// <summary>
    /// 创建 deploy list 子命令。
    /// </summary>
    /// <returns>list 子命令。</returns>
    private static Command CreateListCommand()
    {
        var command = new Command("list", "列出已连接的设备");
        command.Action = AsyncAction.Create(async (_, _) =>
        {
            var cmd = new DeployCommand();
            return await cmd.ListDevicesAsync();
        });
        return command;
    }

    /// <summary>
    /// 部署应用到指定平台。
    /// </summary>
    /// <param name="platform">目标平台（android）。</param>
    /// <param name="apkPath">APK 文件路径，若为 null 则自动查找。</param>
    /// <param name="deviceSerial">设备序列号，若为 null 则使用第一个可用设备。</param>
    /// <param name="startAfterInstall">安装后是否启动应用。</param>
    /// <param name="packageName">应用包名（用于启动）。</param>
    /// <returns>退出码：0=成功，1=参数错误，2=adb 未找到，3=无设备，4=安装失败。</returns>
    internal async Task<int> DeployAsync(
        string platform,
        string? apkPath,
        string? deviceSerial,
        bool startAfterInstall,
        string? packageName)
    {
        if (!string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase))
        {
            Error($"不支持的平台：{platform}（目前仅支持 android）");
            return 1;
        }

        // 查找 adb
        var adbPath = FindAdb();
        if (adbPath is null)
        {
            Error("未找到 adb（Android Debug Bridge）。");
            Info("请安装 Android SDK Platform-Tools 并将其添加到 PATH：");
            Info("  https://developer.android.com/tools/releases/platform-tools");
            return 2;
        }

        // 检查设备连接
        var devices = await ListDevicesAsync(adbPath);
        if (devices.Count == 0)
        {
            Error("未检测到已连接的设备。");
            Info("请确认：");
            Info("  1. 设备已通过 USB 连接或模拟器已启动");
            Info("  2. 已启用 USB 调试（设置 → 开发者选项）");
            Info("  3. 已接受 RSA 密钥指纹提示");
            return 3;
        }

        // 验证设备序列号
        if (deviceSerial is not null && !devices.Contains(deviceSerial))
        {
            Error($"指定的设备序列号 {deviceSerial} 不在已连接设备列表中。");
            Info($"可用设备：{string.Join(", ", devices)}");
            return 3;
        }

        var targetDevice = deviceSerial ?? devices[0];
        Info($"使用设备：{targetDevice}");

        // 查找 APK
        apkPath ??= FindLatestApk();
        if (apkPath is null || !File.Exists(apkPath))
        {
            Error($"APK 文件不存在：{apkPath ?? "(未指定且自动查找失败)"}");
            Info("请指定 APK 路径或确保 bin/Release 目录下存在 .apk 文件。");
            return 1;
        }

        Info($"安装 APK：{apkPath}");
        var (installExitCode, installOutput) = await RunProcessAsync(
            adbPath,
            BuildInstallArgs(apkPath, targetDevice));

        if (installExitCode != 0)
        {
            Error("APK 安装失败。");
            if (!string.IsNullOrEmpty(installOutput))
            {
                Info(installOutput);
            }
            return 4;
        }

        Success($"APK 安装成功：{apkPath}");

        // 可选：启动应用
        if (startAfterInstall)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                Warn("未指定 --package，跳过应用启动。");
                Info("使用 --package com.example.app 指定包名以启用 --start。");
            }
            else
            {
                Info($"启动应用：{packageName}");
                var (startExitCode, startOutput) = await RunProcessAsync(
                    adbPath,
                    BuildStartArgs(packageName, null, targetDevice));

                if (startExitCode != 0)
                {
                    Warn("应用启动失败（APK 已安装）。");
                    if (!string.IsNullOrEmpty(startOutput))
                    {
                        Info(startOutput);
                    }
                }
                else
                {
                    Success($"应用已启动：{packageName}");
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// 列出已连接的设备。
    /// </summary>
    /// <returns>退出码。</returns>
    internal async Task<int> ListDevicesAsync()
    {
        var adbPath = FindAdb();
        if (adbPath is null)
        {
            Error("未找到 adb。请安装 Android SDK Platform-Tools。");
            return 2;
        }

        return await ListDevicesAsync(adbPath) is { Count: > 0 } devices
            ? PrintDevices(devices)
            : PrintNoDevices();
    }

    /// <summary>
    /// 打印设备列表。
    /// </summary>
    private static int PrintDevices(List<string> devices)
    {
        Info("已连接的设备：");
        for (var i = 0; i < devices.Count; i++)
        {
            Info($"  [{i}] {devices[i]}");
        }
        return 0;
    }

    /// <summary>
    /// 打印无设备消息。
    /// </summary>
    private static int PrintNoDevices()
    {
        Info("未检测到已连接的设备。");
        return 0;
    }

    /// <summary>
    /// 列出已连接的设备（内部方法，复用 adb 路径）。
    /// </summary>
    /// <param name="adbPath">adb 可执行文件路径。</param>
    /// <returns>设备序列号列表。</returns>
    internal static async Task<List<string>> ListDevicesAsync(string adbPath)
    {
        var (_, output) = await RunProcessAsync(adbPath, ["devices"]);
        return ParseAdbDevicesOutput(output);
    }

    /// <summary>
    /// 查找 adb 可执行文件路径。
    /// 优先从 PATH 查找，其次从 Android SDK 默认安装路径查找。
    /// </summary>
    /// <returns>adb 完整路径，未找到返回 null。</returns>
    internal static string? FindAdb()
    {
        // 1. PATH 中的 adb
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var executableName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";

        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(dir.Trim(), executableName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // 2. Android SDK 默认路径
        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
            ?? Environment.GetEnvironmentVariable("ANDROID_HOME");

        if (!string.IsNullOrEmpty(sdkRoot))
        {
            var platformToolsPath = Path.Combine(sdkRoot, "platform-tools", executableName);
            if (File.Exists(platformToolsPath))
            {
                return platformToolsPath;
            }
        }

        // 3. Windows 默认安装路径
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winPath = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", executableName);
            if (File.Exists(winPath))
            {
                return winPath;
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFilesPath = Path.Combine(programFilesX86, "Android", "android-sdk", "platform-tools", executableName);
            if (File.Exists(programFilesPath))
            {
                return programFilesPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 在 bin/Release 目录下查找最新的 APK 文件。
    /// 搜索优先级：bin/Release → bin → 当前目录（递归）。
    /// </summary>
    /// <returns>最新的 APK 路径，未找到返回 null。</returns>
    internal static string? FindLatestApk()
    {
        var currentDir = Environment.CurrentDirectory;
        var searchRoots = new[]
        {
            Path.Combine(currentDir, "bin", "Release"),
            Path.Combine(currentDir, "bin"),
            currentDir,
        };

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var apks = Directory.GetFiles(root, "*.apk", SearchOption.AllDirectories);
            if (apks.Length > 0)
            {
                // 返回最后修改的 APK
                return apks
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTime)
                    .First()
                    .FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// 构建 adb install 命令参数。
    /// </summary>
    /// <param name="apkPath">APK 文件路径。</param>
    /// <param name="deviceSerial">设备序列号，为 null 则不指定。</param>
    /// <returns>adb 参数列表。</returns>
    internal static string[] BuildInstallArgs(string apkPath, string? deviceSerial)
    {
        ArgumentException.ThrowIfNullOrEmpty(apkPath);

        var args = new List<string>();
        if (!string.IsNullOrEmpty(deviceSerial))
        {
            args.Add("-s");
            args.Add(deviceSerial);
        }

        args.Add("install");
        args.Add("-r"); // 替换现有应用
        args.Add(apkPath);
        return args.ToArray();
    }

    /// <summary>
    /// 构建 adb shell am start 命令参数（启动应用）。
    /// </summary>
    /// <param name="packageName">应用包名。</param>
    /// <param name="activityName">Activity 名称，为 null 则使用默认 MainActivity。</param>
    /// <param name="deviceSerial">设备序列号，为 null 则不指定。</param>
    /// <returns>adb 参数列表。</returns>
    internal static string[] BuildStartArgs(string packageName, string? activityName, string? deviceSerial)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageName);

        var args = new List<string>();
        if (!string.IsNullOrEmpty(deviceSerial))
        {
            args.Add("-s");
            args.Add(deviceSerial);
        }

        args.Add("shell");
        args.Add("am");
        args.Add("start");
        args.Add("-n");

        // 如未指定 activity，使用 <package>/.MainActivity
        var component = string.IsNullOrEmpty(activityName)
            ? $"{packageName}/.MainActivity"
            : activityName.StartsWith($"{packageName}/", StringComparison.Ordinal)
                ? activityName
                : $"{packageName}/{activityName}";

        args.Add(component);
        return args.ToArray();
    }

    /// <summary>
    /// 解析 adb devices 命令的输出，提取设备序列号列表。
    /// </summary>
    /// <param name="output">adb devices 命令的标准输出。</param>
    /// <returns>设备序列号列表。</returns>
    internal static List<string> ParseAdbDevicesOutput(string? output)
    {
        var devices = new List<string>();
        if (string.IsNullOrEmpty(output))
        {
            return devices;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // 跳过 "List of devices attached" 标题行
            if (trimmed.StartsWith("List of", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 跳过空行
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            // 设备行格式：<serial>\t<state>
            var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            // 仅包含状态为 "device" 的条目（排除 "offline" 和 "unauthorized"）
            if (string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            {
                devices.Add(parts[0]);
            }
        }

        return devices;
    }

    /// <summary>
    /// 验证包名格式是否合法。
    /// </summary>
    /// <param name="packageName">包名字符串。</param>
    /// <returns>合法返回 true。</returns>
    internal static bool IsValidPackageName(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            return false;
        }

        return PackageNameRegex.IsMatch(packageName);
    }

    /// <summary>
    /// 运行外部进程并捕获输出。
    /// </summary>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="args">参数列表。</param>
    /// <returns>(退出码, 标准输出+错误输出)。</returns>
    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
