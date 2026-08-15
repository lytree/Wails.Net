using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Security;
using Wails.Net.Cli.Scaffolding;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 模板 appsettings.json 与 Options 绑定一致性测试（审计 P0-5 / A-3 落地）。
/// <para>
/// 防止配置示例漂移：脚手架模板与 Demo 的 <c>Wails</c> 配置节必须能绑定到
/// <see cref="DesktopHostOptions"/> / <see cref="PermissionOptions"/> / <see cref="IpcOptions"/>，
/// 且模板中出现的每个配置键都能映射到 Options 的公共属性路径——新增配置时若只改模板
/// 不改 Options（或反之），此测试会失败，提示同步更新。
/// </para>
/// </summary>
[NotInParallel]
public sealed class AppSettingsTemplateDriftTests
{
    /// <summary>
    /// 校验脚手架生成的模板 appsettings.json：Wails 节全部键可绑定，且关键安全默认值正确。
    /// </summary>
    [Test]
    public async Task ScaffoldedAppSettings_AllWailsKeys_BindToOptions()
    {
        // 安排：通过脚手架生成模板项目（vanilla-ts 最快），读取 appsettings.json
        var tempDir = Path.Combine(Path.GetTempPath(), "WailsDrift_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("DriftCheckApp", "vanilla-ts", new DirectoryInfo(tempDir));

            await Assert.That(result.Success).IsTrue();
            var appSettingsPath = Path.Combine(tempDir, "DriftCheckApp", "appsettings.json");
            await Assert.That(File.Exists(appSettingsPath)).IsTrue();

            // 操作：解析 JSON 并绑定到 Options（GetSection 将配置路径定位到各 Options 的根节）
            using var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
            var wails = doc.RootElement.GetProperty("Wails");
            var config = BuildConfigurationFromJson(wails, "Wails");

            // DesktopHostOptions 绑定 "Wails" 节；PermissionOptions 绑定 "Wails:Permissions"；
            // IpcOptions 绑定 "Wails:Ipc"（与 AddWailsCore 中 BindConfiguration 的节一致）。
            var hostOptions = config.GetSection("Wails").Get<DesktopHostOptions>();
            var permOptions = config.GetSection("Wails:Permissions").Get<PermissionOptions>();
            var ipcOptions = config.GetSection("Wails:Ipc").Get<IpcOptions>();

            // 断言 1：模板默认安全值（OPT-SEC-003）正确
            await Assert.That(permOptions!.Mode).IsEqualTo(PermissionMode.Strict);
            await Assert.That(permOptions.Enabled).IsTrue();
            await Assert.That(permOptions.DenyByDefault).IsTrue();
            await Assert.That(permOptions.CapabilitiesDirectory).IsEqualTo("capabilities");
            await Assert.That(string.IsNullOrWhiteSpace(hostOptions!.App?.Security?.Csp)).IsFalse();

            // 断言 2：模板出现的每个配置键都能映射到 Options 公共属性（drift check 核心）
            var templateKeys = FlattenJsonKeys(wails, "Wails");
            var unknownKeys = templateKeys
                .Where(key => !CanBindToOptions(key, hostOptions, permOptions, ipcOptions))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            // 模板键（Wails:ApplicationName / Wails:Permissions:Mode 等）必须全部可绑定
            await Assert.That(unknownKeys).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 校验 Demo 总览项目 appsettings.json 的 Wails 节也能绑定到 Options（示例不漂移）。
    /// </summary>
    [Test]
    public async Task DemoAppSettings_WailsSection_BindsToOptions()
    {
        // 安排：定位仓库内 Demo 的 appsettings.json
        var repoRoot = LocateRepoRoot();
        var demoSettings = Path.Combine(repoRoot, "examples", "Wails.Net.Demo", "appsettings.json");
        if (!File.Exists(demoSettings))
        {
            return; // 仓库布局变化时跳过（不视为失败）
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(demoSettings));
        if (!doc.RootElement.TryGetProperty("Wails", out var wails))
        {
            return;
        }

        var config = BuildConfigurationFromJson(wails, "Wails");

        // 操作：绑定三个 Options 类型（与 AddWailsCore 的配置节一致）
        var hostOptions = config.GetSection("Wails").Get<DesktopHostOptions>();
        var permOptions = config.GetSection("Wails:Permissions").Get<PermissionOptions>();
        var ipcOptions = config.GetSection("Wails:Ipc").Get<IpcOptions>();

        // 断言：绑定不抛异常且非空（配置键可被 Options 接收）。
        // 注意：Ipc 节可能未配置（旧项目无 Wails:Ipc），此时 Get 返回 null 属正常，
        // 仅需确保存在的节都能绑定。
        await Assert.That(hostOptions).IsNotNull();
        await Assert.That(permOptions).IsNotNull();

        // Demo 不强制 Strict（可兼容模式），但 Options 必须能接收其全部键
        var templateKeys = FlattenJsonKeys(wails, "Wails");
        var unknownKeys = templateKeys
            .Where(key => !CanBindToOptions(key, hostOptions, permOptions, ipcOptions))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        await Assert.That(unknownKeys).IsEmpty();
    }

    /// <summary>
    /// 将 JSON 元素扁平化为 <c>Wails:Section:Key</c> 形式的键列表（含叶子与中间节点）。
    /// </summary>
    private static List<string> FlattenJsonKeys(JsonElement element, string prefix)
    {
        var keys = new List<string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}:{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    keys.Add(path); // 中间节点（如 Wails:Permissions）也应可绑定
                    keys.AddRange(FlattenJsonKeys(prop.Value, path));
                }
                else
                {
                    keys.Add(path); // 叶子节点（如 Wails:ApplicationName）
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// 用 JSON 中的 Wails 节构建内存配置（ConfigurationBuilder + InMemory），
    /// 避免依赖磁盘 JSON provider，使测试可独立运行。
    /// </summary>
    private static IConfiguration BuildConfigurationFromJson(JsonElement wailsElement, string prefix)
    {
        var keyValues = new Dictionary<string, string?>();
        FlattenJsonKeyValues(wailsElement, prefix, keyValues);

        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(keyValues);
        return builder.Build();
    }

    /// <summary>
    /// 将 JSON 对象扁平化为配置键值对（叶子节点的原始文本值）。
    /// </summary>
    private static void FlattenJsonKeyValues(JsonElement element, string prefix, Dictionary<string, string?> target)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in element.EnumerateObject())
        {
            var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}:{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenJsonKeyValues(prop.Value, path, target);
            }
            else if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                     or JsonValueKind.True or JsonValueKind.False)
            {
                target[path] = prop.Value.ToString();
            }
        }
    }

    /// <summary>
    /// 判断配置键是否能绑定到任一 Options 类型的公共属性路径。
    /// <para>
    /// 规则：去掉 <c>Wails:</c> 前缀后，键必须匹配 <c>DesktopHostOptions</c>、
    /// <c>PermissionOptions</c> 或 <c>IpcOptions</c> 的公共可写属性链；
    /// 允许中间节点（如 <c>Permissions</c> 本身）存在，但叶子键必须能找到对应属性。
    /// </para>
    /// </summary>
    private static bool CanBindToOptions(
        string configKey,
        DesktopHostOptions? hostOptions,
        PermissionOptions? permOptions,
        IpcOptions? ipcOptions)
    {
        var relative = configKey.StartsWith("Wails:", StringComparison.OrdinalIgnoreCase)
            ? configKey["Wails:".Length..]
            : configKey;

        // 根级：ApplicationName / Window / Assets / App 属于 DesktopHostOptions；
        // Permissions 属于 PermissionOptions；Ipc 属于 IpcOptions。
        var segment0 = relative.Split(':')[0];
        return segment0 switch
        {
            "Permissions" => OptionsContainsPath(typeof(PermissionOptions), StripSegment(relative)),
            "Ipc" => OptionsContainsPath(typeof(IpcOptions), StripSegment(relative)),
            _ => OptionsContainsPath(typeof(DesktopHostOptions), relative),
        };
    }

    private static string StripSegment(string path)
    {
        var idx = path.IndexOf(':');
        return idx < 0 ? string.Empty : path[(idx + 1)..];
    }

    /// <summary>
    /// 递归检查类型是否包含给定属性路径（忽略大小写）。
    /// </summary>
    private static bool OptionsContainsPath(Type optionsType, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true; // 中间节点（如 Wails:Permissions 本身）视为可绑定
        }

        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return WalkPropertyPath(optionsType, segments, 0);
    }

    private static bool WalkPropertyPath(Type type, string[] segments, int index)
    {
        if (index >= segments.Length)
        {
            return true;
        }

        var property = type.GetProperty(
            segments[index],
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is null || !property.CanWrite)
        {
            return false;
        }

        return WalkPropertyPath(property.PropertyType, segments, index + 1);
    }

    /// <summary>
    /// 从当前目录向上定位仓库根（含 .git 或 Wails.Net.slnx）。
    /// </summary>
    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wails.Net.slnx")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory; // 兜底：使用测试输出目录
    }
}
