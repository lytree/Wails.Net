using Gio;
using GLib;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Unity Launcher API（<c>com.canonical.Unity.LauncherEntry</c>）的启动器状态快照。
/// <para>
/// 对应桌面启动器的进度条与徽章语义：<c>progress</c>（0..1，负值表示不确定进度）、
/// <c>progress-visible</c>、<c>count</c>、<c>count-visible</c>、<c>urgent</c>。
/// 主流桌面（Unity、KDE Plasma、GNOME 扩展）均监听该信号。
/// </para>
/// </summary>
internal readonly record struct LauncherEntryState(
    bool ProgressVisible,
    double Progress,
    bool CountVisible,
    long Count,
    bool Urgent)
{
    /// <summary>清除进度与计数的空状态。</summary>
    public static LauncherEntryState Empty => new(false, 0, false, 0, false);
}

/// <summary>
/// Linux 桌面启动器状态桥（best-effort）。
/// <para>
/// 通过 session bus 广播 <c>com.canonical.Unity.LauncherEntry.Update</c> 信号，
/// 让桌面环境在启动器图标上显示进度条/徽章。Linux 无统一的任务栏进度 API，
/// 该接口是跨桌面的事实标准（Wails v3 Go 版本未提供等价实现）。
/// </para>
/// <para>
/// 信号参数为 <c>(s a{sv})</c>：启动器 URI（<c>application://{appId}.desktop</c>）
/// 与属性字典。D-Bus 不可用或非 Linux 平台时静默降级。
/// </para>
/// </summary>
internal static class LinuxLauncherEntry
{
    /// <summary>Unity LauncherEntry 接口名。</summary>
    internal const string InterfaceName = "com.canonical.Unity.LauncherEntry";

    /// <summary>更新信号名。</summary>
    private const string UpdateSignal = "Update";

    /// <summary>发送方对象路径前缀。</summary>
    private const string ObjectPathPrefix = "/com/canonical/unity/launcherentry/";

    /// <summary>
    /// 构造启动器 URI：<c>application://{appId}.desktop</c>。
    /// </summary>
    /// <param name="appId">GTK/D-Bus application id。</param>
    /// <returns>启动器 URI。</returns>
    internal static string BuildLauncherUri(string appId)
    {
        var id = appId.EndsWith(".desktop", StringComparison.Ordinal) ? appId : appId + ".desktop";
        return "application://" + id;
    }

    /// <summary>
    /// 构造发送方对象路径。D-Bus 路径元素仅允许 <c>[A-Za-z0-9_]</c>，
    /// 因此把 application id 中的非法字符替换为下划线。
    /// </summary>
    /// <param name="appId">GTK/D-Bus application id。</param>
    /// <returns>合法的对象路径。</returns>
    internal static string BuildObjectPath(string appId)
    {
        var builder = new System.Text.StringBuilder(ObjectPathPrefix, ObjectPathPrefix.Length + appId.Length);
        foreach (var ch in appId)
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 由任务栏进度状态构造启动器状态。
    /// <paramref name="completed"/> / <paramref name="total"/> 归一化为 0..1；
    /// <see cref="TaskbarProgressState.Indeterminate"/> 使用 <c>-1</c>（Unity API 约定）。
    /// </summary>
    /// <param name="state">进度状态。</param>
    /// <param name="completed">已完成值。</param>
    /// <param name="total">总值。</param>
    /// <returns>启动器状态快照。</returns>
    internal static LauncherEntryState BuildProgressState(
        Wails.Net.Application.Windows.TaskbarProgressState state,
        ulong completed,
        ulong total)
    {
        if (state == Wails.Net.Application.Windows.TaskbarProgressState.None)
        {
            return LauncherEntryState.Empty;
        }

        var progress = state == Wails.Net.Application.Windows.TaskbarProgressState.Indeterminate
            ? -1.0
            : total > 0
                ? Math.Clamp((double)completed / total, 0.0, 1.0)
                : 0.0;
        return new LauncherEntryState(true, progress, false, 0, false);
    }

    /// <summary>
    /// 由徽章数量构造启动器状态（<c>count</c> / <c>count-visible</c>）。
    /// </summary>
    /// <param name="count">徽章数量，0 表示清除。</param>
    /// <returns>启动器状态快照。</returns>
    internal static LauncherEntryState BuildBadgeState(int count)
        => count > 0
            ? new LauncherEntryState(false, 0, true, count, false)
            : LauncherEntryState.Empty;

    /// <summary>
    /// 广播启动器更新信号（best-effort）。
    /// </summary>
    /// <param name="appId">GTK/D-Bus application id。</param>
    /// <param name="state">状态快照。</param>
    /// <returns>成功广播返回 true；非 Linux/D-Bus 不可用时返回 false。</returns>
    internal static bool TryUpdate(string appId, LauncherEntryState state)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(appId))
        {
            return false;
        }

        DBusConnection? connection = null;
        try
        {
            connection = DBusConnection.Get(BusType.Session);
            if (connection is null)
            {
                return false;
            }

            using var properties = VariantDict.New(null);
            if (state.ProgressVisible)
            {
                properties.InsertValue("progress", Variant.NewDouble(state.Progress));
                properties.InsertValue("progress-visible", Variant.NewBoolean(true));
            }
            else
            {
                properties.InsertValue("progress-visible", Variant.NewBoolean(false));
            }

            if (state.CountVisible)
            {
                properties.InsertValue("count", Variant.NewInt64(state.Count));
                properties.InsertValue("count-visible", Variant.NewBoolean(true));
            }
            else
            {
                properties.InsertValue("count-visible", Variant.NewBoolean(false));
            }

            properties.InsertValue("urgent", Variant.NewBoolean(state.Urgent));

            using var dictionary = properties.End();
            using var parameters = Variant.NewTuple(new[]
            {
                Variant.NewString(BuildLauncherUri(appId)),
                dictionary,
            });

            connection.EmitSignal(
                destinationBusName: null,
                objectPath: BuildObjectPath(appId),
                interfaceName: InterfaceName,
                signalName: UpdateSignal,
                parameters: parameters);

            return true;
        }
        catch
        {
            // 无 session bus / 桌面不支持：静默降级。
            return false;
        }
        finally
        {
            connection?.Dispose();
        }
    }
}
