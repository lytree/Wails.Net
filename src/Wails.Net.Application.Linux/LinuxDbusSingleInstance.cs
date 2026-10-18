using Gio;
using GLib;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 以应用自身 ID 占用 D-Bus bus name 的薄原生封装（best-effort）。
/// <para>
/// 对应 Wails v3 Go 版本 <c>pkg/application/single_instance_linux.go</c> 中
/// <c>linuxLock.acquire</c> 的 <c>conn.RequestName(l.dbusName, dbus.NameFlagDoNotQueue)</c> 部分
/// （beta.23 <c>#5973</c>）。
/// </para>
/// <para>
/// Wails.Net 未依赖 godbus，而是通过 GirCore Gio 的 <c>DBusConnection</c> 直接向
/// session bus 守护进程调用 <c>org.freedesktop.DBus.RequestName</c>。
/// 与 Go 语义对齐的部分：
/// <list type="bullet">
/// <item>占用的 bus name 为 <c>uniqueID + ".SingleInstance"</c>（由 <see cref="LinuxSingleInstanceNames"/> 推导）；</item>
/// <item>请求带 <c>DO_NOT_QUEUE</c> 标志，已有实例持有时直接返回 <c>EXISTS</c>（不排队）。</item>
/// </list>
/// 与 Go 的差异：Go 在 D-Bus 出错（如沙箱拒绝该名字）时把错误上报为致命错误；
/// 本实现将「D-Bus 不可用」视为可降级情形（<see cref="BusNameClaimResult.Unavailable"/>），
/// 由调用方回退到既有的锁文件机制，避免在无 session bus 的环境下直接中断启动。
/// </para>
/// <remarks>
/// 本类为原生调用边界，在非 Linux 平台不加载原生库（提前返回 <see cref="BusNameClaimResult.Unavailable"/>）。
/// 因本机为 Windows，Linux 运行时行为无法在本地验证，故本类保持薄封装，逻辑集中在可单测的纯逻辑类中。
/// </remarks>
/// </summary>
internal static class LinuxDbusSingleInstance
{
    /// <summary>
    /// bus name 的占用结果。
    /// </summary>
    internal enum BusNameClaimResult
    {
        /// <summary>本进程已成为该 bus name 的持有者（首实例）。</summary>
        Owned,

        /// <summary>该 bus name 已被其他进程持有（第二实例）。</summary>
        OwnedByOther,

        /// <summary>D-Bus 不可用（无 session bus、沙箱拒绝或调用异常），调用方应降级处理。</summary>
        Unavailable,
    }

    /// <summary>D-Bus 守护进程的 bus name / interface name（两者相同）。</summary>
    private const string DbusDaemonName = "org.freedesktop.DBus";

    /// <summary>D-Bus 守护进程的 object path。</summary>
    private const string DbusDaemonObjectPath = "/org/freedesktop/DBus";

    /// <summary>RequestName 方法名。</summary>
    private const string RequestNameMethod = "RequestName";

    /// <summary>
    /// <c>DBUS_REQUEST_NAME_REPLY_PRIMARY_OWNER</c>：本进程成为主要持有者。
    /// </summary>
    private const uint RequestNameReplyPrimaryOwner = 1;

    /// <summary>
    /// <c>DBUS_REQUEST_NAME_REPLY_EXISTS</c>：名字已被他人持有。
    /// </summary>
    private const uint RequestNameReplyExists = 3;

    /// <summary>
    /// <c>DBUS_REQUEST_NAME_REPLY_ALREADY_OWNER</c>：本进程已持有该名字。
    /// </summary>
    private const uint RequestNameReplyAlreadyOwner = 4;

    /// <summary>
    /// 尝试以 <paramref name="busName"/> 占用 session bus 的名字。
    /// </summary>
    /// <param name="busName">由 UniqueID 推导的 bus name（<c>uniqueID + ".SingleInstance"</c>）。</param>
    /// <param name="connection">
    /// 成功占用时输出持有该名字的 D-Bus 连接（调用方须在销毁时释放它，以隐式释放名字）；
    /// 其他情况下为 <c>null</c>。
    /// </param>
    /// <returns>占用结果。</returns>
    internal static BusNameClaimResult TryClaim(string busName, out DBusConnection? connection)
    {
        connection = null;

        // 非 Linux 平台不触碰原生 Gio 调用（与 Go 的 linux 构建约束一致）。
        if (!OperatingSystem.IsLinux())
        {
            return BusNameClaimResult.Unavailable;
        }

        DBusConnection? conn = null;
        try
        {
            conn = DBusConnection.Get(BusType.Session);
            if (conn is null)
            {
                return BusNameClaimResult.Unavailable;
            }

            var parameters = Variant.NewTuple(new Variant[]
            {
                Variant.NewString(busName),
                // 对应 Go 的 dbus.NameFlagDoNotQueue：不排队，已有持有者时立即返回 EXISTS。
                Variant.NewUint32((uint)BusNameOwnerFlags.DoNotQueue),
            });

            var reply = conn.CallSync(
                busName: DbusDaemonName,
                objectPath: DbusDaemonObjectPath,
                interfaceName: DbusDaemonName,
                methodName: RequestNameMethod,
                parameters: parameters,
                replyType: null,
                flags: DBusCallFlags.None,
                timeoutMsec: -1,
                cancellable: null);

            switch (reply.GetUint32())
            {
                case RequestNameReplyPrimaryOwner:
                case RequestNameReplyAlreadyOwner:
                    connection = conn;
                    return BusNameClaimResult.Owned;
                case RequestNameReplyExists:
                    // 名字已由首实例持有：本进程是第二实例。
                    conn.Dispose();
                    return BusNameClaimResult.OwnedByOther;
                default:
                    // 其余回复（如 IN_QUEUE）不应出现（已置 DO_NOT_QUEUE），按不可用处理。
                    conn.Dispose();
                    return BusNameClaimResult.Unavailable;
            }
        }
        catch
        {
            // 连接或调用失败（无 session bus、沙箱拒绝该名字等）：降级为不可用。
            conn?.Dispose();
            connection = null;
            return BusNameClaimResult.Unavailable;
        }
    }
}
