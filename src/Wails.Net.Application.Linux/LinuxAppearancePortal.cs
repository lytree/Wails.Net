using Gio;
using GLib;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 外观设置读取器：优先使用 xdg-desktop-portal 的
/// <c>org.freedesktop.portal.Settings</c> 接口读取 <c>color-scheme</c>。
/// <para>
/// 对应 Wails v3.1.0 #6072：此前只能依赖 GNOME 的 gsettings schema，
/// 在 KDE 等不提供该 schema 的桌面上会始终回退到亮色；settings portal 是跨桌面的标准来源。
/// </para>
/// <para>
/// portal 返回值为 <c>uint32</c>：0 表示无偏好，1 表示偏好暗色，2 表示偏好亮色。
/// 无法连接 portal 或调用失败时返回 null，由调用方继续使用 gsettings / 环境变量回退。
/// </para>
/// </summary>
internal static class LinuxAppearancePortal
{
    /// <summary>
    /// settings portal 的 D-Bus 服务名。
    /// </summary>
    internal const string PortalBusName = "org.freedesktop.portal.Desktop";

    /// <summary>
    /// settings portal 的 D-Bus 对象路径。
    /// </summary>
    internal const string PortalObjectPath = "/org/freedesktop/portal/desktop";

    /// <summary>
    /// settings 接口名。
    /// </summary>
    internal const string PortalInterfaceName = "org.freedesktop.portal.Settings";

    /// <summary>
    /// 外观设置命名空间。
    /// </summary>
    internal const string AppearanceNamespace = "org.freedesktop.appearance";

    /// <summary>
    /// 颜色方案键名。
    /// </summary>
    internal const string ColorSchemeKey = "color-scheme";

    /// <summary>
    /// 读取调用的超时时间（毫秒）：portal 不可用时快速失败，避免拖慢启动。
    /// </summary>
    internal const int PortalTimeoutMs = 500;

    /// <summary>
    /// 读取 portal 的 color-scheme。
    /// </summary>
    /// <returns>true 表示偏好暗色，false 表示偏好亮色，null 表示无偏好或 portal 不可用。</returns>
    internal static bool? TryReadColorSchemeIsDark()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var value = ReadColorSchemeValue("Read") ?? ReadColorSchemeValue("ReadOne");
        return value is null ? null : MapColorScheme(value.Value);
    }

    /// <summary>
    /// 将 portal 返回的 color-scheme 数值映射为暗色判定。
    /// </summary>
    /// <param name="value">portal 返回值：0 无偏好、1 暗色、2 亮色。</param>
    /// <returns>true 暗色；false 亮色；null 无偏好或未知值。</returns>
    internal static bool? MapColorScheme(uint value)
    {
        return value switch
        {
            1 => true,
            2 => false,
            _ => null,
        };
    }

    /// <summary>
    /// 调用 portal 的 Settings.Read / ReadOne 方法，返回 color-scheme 的原始数值。
    /// </summary>
    /// <param name="methodName">方法名：<c>Read</c> 或 <c>ReadOne</c>（兼容旧版 portal）。</param>
    /// <returns>成功返回数值，失败返回 null。</returns>
    private static uint? ReadColorSchemeValue(string methodName)
    {
        DBusConnection? connection = null;
        Variant? parameters = null;
        VariantType? replyType = null;
        Variant? reply = null;
        Variant? wrapper = null;
        Variant? inner = null;

        try
        {
            connection = DBusConnection.Get(BusType.Session);
            if (connection is null)
            {
                return null;
            }

            parameters = Variant.NewTuple(new Variant[]
            {
                Variant.NewString(AppearanceNamespace),
                Variant.NewString(ColorSchemeKey),
            });
            replyType = VariantType.New("(v)");

            reply = connection.CallSync(
                busName: PortalBusName,
                objectPath: PortalObjectPath,
                interfaceName: PortalInterfaceName,
                methodName: methodName,
                parameters: parameters,
                replyType: replyType,
                flags: DBusCallFlags.None,
                timeoutMsec: PortalTimeoutMs,
                cancellable: null);

            if (reply is null)
            {
                return null;
            }

            wrapper = reply.GetChildValue(0);
            inner = wrapper.GetVariant();
            return inner?.GetUint32();
        }
        catch
        {
            // portal 不存在、接口版本不兼容或返回类型异常时静默失败，交由上层回退。
            return null;
        }
        finally
        {
            inner?.Dispose();
            wrapper?.Dispose();
            reply?.Dispose();
            replyType?.Dispose();
            parameters?.Dispose();
            connection?.Dispose();
        }
    }
}
