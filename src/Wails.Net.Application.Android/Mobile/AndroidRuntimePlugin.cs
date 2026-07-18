using System.Text.Json;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 运行时插件，提供 Android 平台专属的运行时命令。
/// 对应 Wails v3 Go 版本 <c>messageprocessor_android.go</c> 中除 Haptics（由 <see cref="Wails.Net.Application.Plugins.Mobile.HapticsPlugin"/> 提供）外的两个方法：
/// <list type="bullet">
///   <item><c>device.info</c> — 对应 <c>androidDeviceInfo()</c>，返回设备制造商、品牌、型号等信息。</item>
///   <item><c>toast.show</c> — 对应 <c>androidShowToast(message)</c>，显示 Android Toast 提示。</item>
/// </list>
/// <para>
/// Wails v3 通过 object ID 12（<c>objectNames.Android</c>）路由这些方法，
/// Wails.Net 采用 Tauri v2 风格的插件命令名（<c>device.*</c> / <c>toast.*</c>）。
/// </para>
/// </summary>
public class AndroidRuntimePlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "android-runtime";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册 Android 运行时相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("android-runtime:default",
            "Android 运行时默认权限集",
            "android-runtime:allow-device-info",
            "android-runtime:allow-toast");
        context.Permissions.DeclarePermission("android-runtime:allow-device-info",
            "允许获取 Android 设备信息");
        context.Permissions.DeclarePermission("android-runtime:allow-toast",
            "允许显示 Android Toast 提示");

        var commands = context.Commands;

        // device.info — 对应 Wails v3 messageprocessor_android.go AndroidDeviceInfo (method=1)
        commands.MapCommand("device.info",
            (Func<ICommandContext, string>)(_ => GetDeviceInfoJson()));

        // toast.show — 对应 Wails v3 messageprocessor_android.go AndroidToast (method=2)
        commands.MapCommand("toast.show",
            (Action<ICommandContext, ToastShowOptions>)((ctx, opts) => ShowToast(opts.Message)));
    }

    /// <summary>
    /// 获取 Android 设备信息并序列化为 JSON 字符串。
    /// 对应 Wails v3 <c>androidDeviceInfo()</c>，通过 <c>Android.OS.Build</c> 读取硬件信息。
    /// </summary>
    /// <returns>JSON 格式的设备信息，包含 platform、manufacturer、brand、model、device、version、sdkInt 字段。</returns>
    public static string GetDeviceInfoJson()
    {
        var info = GetDeviceInfo();
        return JsonSerializer.Serialize(info, JsonOptions.DefaultSerializerOptions);
    }

    /// <summary>
    /// 获取 Android 设备信息字典。
    /// 对应 Wails v3 <c>androidDeviceInfo()</c> 返回的 <c>map[string]interface{}</c>。
    /// </summary>
    /// <returns>包含设备信息的字典；非 Android 环境下仅包含 platform 字段。</returns>
    public static Dictionary<string, object?> GetDeviceInfo()
    {
        var info = new Dictionary<string, object?>
        {
            ["platform"] = "android",
        };

        try
        {
            // Android.OS.Build 是静态类，非 Android 环境下访问会抛 TypeLoadException 或返回 null
            info["manufacturer"] = Build.Manufacturer ?? string.Empty;
            info["brand"] = Build.Brand ?? string.Empty;
            info["model"] = Build.Model ?? string.Empty;
            info["device"] = Build.Device ?? string.Empty;
            info["version"] = Build.VERSION.Release ?? string.Empty;
            info["sdkInt"] = (int)Build.VERSION.SdkInt;
        }
        catch (Java.Lang.Exception)
        {
            // 部分 Build 字段在异常环境下访问可能抛出 Java 异常，保留已填充字段
        }
        catch (TypeLoadException)
        {
            // 非 Android 环境（单元测试）：仅返回 platform 字段
        }

        return info;
    }

    /// <summary>
    /// 显示 Android Toast 提示。
    /// 对应 Wails v3 <c>androidShowToast(message)</c>，通过 <c>Android.Widget.Toast.MakeText</c> 显示。
    /// </summary>
    /// <param name="message">要显示的提示文本。</param>
    public static void ShowToast(string message)
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            // 非 Android 环境（单元测试）：no-op
            return;
        }

        try
        {
            // ToastLength.Short = 0, ToastLength.Long = 1
            // 必须在主线程调用 Toast.MakeText，否则会抛 RuntimeException
            // 由调用方确保在主线程（命令调度器已在主线程分发）
            var toast = global::Android.Widget.Toast.MakeText(context, message ?? string.Empty,
                global::Android.Widget.ToastLength.Short);
            toast?.Show();
        }
        catch (Java.Lang.Exception)
        {
            // 视图未准备好 / 系统异常时静默忽略
        }
    }
}

/// <summary>toast.show 命令参数。</summary>
public sealed class ToastShowOptions
{
    /// <summary>Toast 提示文本。</summary>
    public string Message { get; set; } = string.Empty;
}
