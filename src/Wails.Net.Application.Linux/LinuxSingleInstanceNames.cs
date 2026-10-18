namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 单实例锁所需的三个 D-Bus 标识符的推导与校验纯逻辑。
/// <para>
/// 对应 Wails v3 Go 版本 <c>pkg/application/single_instance_linux.go</c> 中的
/// <c>singleInstanceNames</c>（beta.23 <c>#5973</c>：single-instance 以应用自身 ID 占用 D-Bus bus name）。
/// </para>
/// <para>
/// 三个标识符不能共用同一个字符串：D-Bus 对它们的拼写规则不同 ——
/// bus name 允许连字符，interface name 不允许，object path 以 '/' 分隔元素且既不允许连字符也不允许 '.'。
/// bus name 因此逐字保留 UniqueID（<c>uniqueID + ".SingleInstance"</c>），这一点对沙箱构建很重要：
/// flatpak 只能占用以其自身 app id 为前缀的名字，遵循 UniqueID 文档约定
/// （"unique per application, e.g. com.myapp.myapplication"）的应用因此无需额外 portal 权限
/// 就能占用一个它有权占用的名字。
/// </para>
/// </summary>
internal static class LinuxSingleInstanceNames
{
    /// <summary>
    /// bus name / interface name 的固定后缀。对应 Go 中的字面量 <c>".SingleInstance"</c>。
    /// </summary>
    internal const string Suffix = ".SingleInstance";

    /// <summary>
    /// 由 UniqueID 推导所需的 D-Bus 标识符（bus name / interface name / object path）。
    /// 校验规则与 Go 一致：UniqueID 以 '.' 分隔、每个元素非空且不以数字开头，
    /// 且仅含 D-Bus 允许的字符（<c>A-Z a-z 0-9 _ -</c>）。
    /// </summary>
    /// <param name="uniqueId">应用唯一 ID（对应 Go 的 <c>SingleInstanceOptions.UniqueID</c>）。</param>
    /// <param name="busName">占用的 bus name（<c>uniqueID + ".SingleInstance"</c>，逐字保留 UniqueID）。</param>
    /// <param name="interfaceName">导出对象使用的 interface name（连字符替换为下划线）。</param>
    /// <param name="objectPath">导出对象的 object path（连字符替换为下划线，'.' 替换为 '/'）。</param>
    /// <param name="error">推导失败时的原因；成功时为 <c>null</c>。</param>
    /// <returns>推导成功返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    internal static bool TryDerive(
        string? uniqueId,
        out string busName,
        out string interfaceName,
        out string objectPath,
        out string? error)
    {
        busName = string.Empty;
        interfaceName = string.Empty;
        objectPath = string.Empty;

        // Go: linuxLock.acquire 在使用 singleInstanceNames 之前先拒绝空 UniqueID。
        if (string.IsNullOrEmpty(uniqueId))
        {
            error = "UniqueID 不能为空（single instance lock 需要它）";
            return false;
        }

        foreach (var element in uniqueId.Split('.'))
        {
            if (element.Length == 0)
            {
                error = $"UniqueID \"{uniqueId}\" 含有空元素；它必须是以 '.' 分隔的名字，例如 com.myapp.myapplication";
                return false;
            }

            if (element[0] >= '0' && element[0] <= '9')
            {
                error = $"UniqueID \"{uniqueId}\" 的元素 \"{element}\" 以数字开头，D-Bus 不允许";
                return false;
            }

            foreach (var c in element)
            {
                if (!IsDbusNameChar(c))
                {
                    error = $"UniqueID \"{uniqueId}\" 含有 '{c}'，D-Bus 不允许出现在名字中";
                    return false;
                }
            }
        }

        // 连字符在 bus name 中合法，但在 interface name 与 object path 中不合法，
        // 因此后两者由去掉连字符的形式构建。
        var unhyphenated = uniqueId.Replace('-', '_');

        busName = uniqueId + Suffix;
        interfaceName = unhyphenated + Suffix;
        objectPath = "/" + unhyphenated.Replace('.', '/') + "/SingleInstance";
        error = null;
        return true;
    }

    /// <summary>
    /// 判断字符是否属于 D-Bus 名字允许的字符集（ASCII 字母、数字、下划线、连字符）。
    /// </summary>
    private static bool IsDbusNameChar(char c)
    {
        return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';
    }
}
