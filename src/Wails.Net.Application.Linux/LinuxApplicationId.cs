using System.Text;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux GTK 应用程序 ID（application id）的推导与校验纯逻辑。
/// <para>
/// 对应 Wails v3 Go 版本 <c>pkg/application/application_linux_appid.go</c> 中的
/// <c>sanitizeAppName</c> / <c>validateApplicationID</c> / <c>applicationID</c> / <c>programName</c>
/// （beta.23 <c>#5972</c>：Linux GTK application id 覆盖）。
/// </para>
/// <para>
/// <b>id 覆盖规则</b>（与 Go 一致）：
/// <list type="number">
/// <item>未显式覆盖时由应用名推导默认 id：<c>"org.wails." + <see cref="SanitizeAppName"/>(Name)</c>；</item>
/// <item>显式覆盖优先（对应 Go 的 <c>Options.Linux.ApplicationID</c>）：覆盖值须满足
/// <c>g_application_id_is_valid()</c> 契约，否则回退到推导 id，并通过
/// <see cref="ResolveApplicationId"/> 的 <c>error</c> 输出报告原因（Go 经错误处理器上报后继续使用推导 id）；</item>
/// <item>程序名（对应 GTK <c>g_set_prgname()</c>，在 Wayland 上决定窗口的 surface app_id）
/// 在设置了 application id 覆盖时继承该 id；显式程序名优先于继承。</item>
/// </list>
/// </para>
/// <para>
/// <b>与 Go 的差异（覆盖来源）</b>：Wails.Net 核心库
/// <c>Wails.Net.Application.Options.LinuxOptions</c> 未提供 <c>ApplicationID</c> / <c>ProgramName</c>
/// 字段（本任务约束下不修改核心库），因此覆盖改由本类的静态属性
/// <see cref="ApplicationIdOverride"/> / <see cref="ProgramNameOverride"/> 提供，
/// 应用须在应用启动（<c>Run()</c>）之前设置（对应 Go 在构造 platformApp 前配置 <c>Options.Linux</c>）。
/// </para>
/// <para>
/// <b>与 Go 的差异（落地方式）</b>：Go 将 id 交给 <c>gtk_application_new()</c>；Wails.Net 的 Linux
/// 平台未使用 <c>Gtk.Application</c>（而是 <c>GLib.MainLoop</c>），因此 id 无法挂到 application 对象上，
/// 仅通过程序名（<c>g_set_prgname()</c>）间接体现 —— 见 <c>LinuxPlatformApp.Run()</c>。
/// </para>
/// </summary>
public static class LinuxApplicationId
{
    /// <summary>
    /// GTK/D-Bus 接受的最长 application id 长度（继承自 D-Bus bus name 上限）。
    /// 对应 Go 常量 <c>maxApplicationIDLength</c>（255）。
    /// </summary>
    internal const int MaxApplicationIdLength = 255;

    /// <summary>
    /// 推导 id 的固定前缀。对应 Go 中的字面量 <c>"org.wails."</c>。
    /// </summary>
    private const string ApplicationIdPrefix = "org.wails.";

    /// <summary>
    /// 名称清洗后为空时的回退元素。对应 Go 中的字面量 <c>"wailsapp"</c>。
    /// </summary>
    private const string FallbackNameElement = "wailsapp";

    /// <summary>
    /// application id 覆盖值（对应 Go 的 <c>Options.Linux.ApplicationID</c>）。
    /// 为 null 或空时使用由应用名推导的默认 id。
    /// 须在应用启动前设置；设置后应与 <c>CreateWebviewWindow</c> 之前的阶段一致。
    /// </summary>
    public static string? ApplicationIdOverride
    {
        get => Volatile.Read(ref _applicationIdOverride);
        set => Volatile.Write(ref _applicationIdOverride, value);
    }

    /// <summary>
    /// 程序名覆盖值（对应 Go 的 <c>Options.Linux.ProgramName</c>），交给 GTK <c>g_set_prgname()</c>。
    /// 为 null 或空时，若设置了 <see cref="ApplicationIdOverride"/> 则继承解析后的 application id，
    /// 否则不设置程序名（保留 GTK 从可执行文件名推断的默认值）。
    /// </summary>
    public static string? ProgramNameOverride
    {
        get => Volatile.Read(ref _programNameOverride);
        set => Volatile.Write(ref _programNameOverride, value);
    }

    private static string? _applicationIdOverride;
    private static string? _programNameOverride;

    /// <summary>
    /// 将应用名清洗为 GTK/D-Bus application id 的单个元素：
    /// 仅保留字母数字、连字符与下划线，折叠连续下划线并去除首尾下划线，
    /// 空结果回退为 <c>wailsapp</c>，前导数字加下划线前缀，最后转小写。
    /// 对应 Go <c>sanitizeAppName</c>（<c>application_linux_appid.go</c>）。
    /// </summary>
    /// <param name="name">应用名（可为 null/空）。</param>
    /// <returns>清洗后的单个 id 元素（永不含 '.'、永不为空、永不以数字开头）。</returns>
    public static string SanitizeAppName(string? name)
    {
        // 1) 非法字符替换为 '_'（Go: invalidAppNameChars.ReplaceAllString(name, "_")）。
        var source = name ?? string.Empty;
        var buffer = new StringBuilder(source.Length);
        foreach (var c in source)
        {
            buffer.Append(IsApplicationIdChar(c) ? c : '_');
        }

        // 2) 折叠连续下划线（Go: for strings.Contains(name, "__") { ReplaceAll("__", "_") }）。
        var collapsed = buffer.ToString();
        while (collapsed.Contains("__", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("__", "_", StringComparison.Ordinal);
        }

        // 3) 去除首尾下划线（Go: strings.Trim(name, "_")）。
        var trimmed = collapsed.Trim('_');

        // 4) 空结果回退（Go: if name == "" { name = "wailsapp" }）。
        if (trimmed.Length == 0)
        {
            trimmed = FallbackNameElement;
        }

        // 5) 前导数字加下划线前缀（Go: leadingDigits.ReplaceAllString(name, "_$0")）。
        //    必须放在 trim 之后，否则前缀会被 trim 再次裁掉，留下 GTK 拒绝的、以数字开头的元素。
        if (trimmed[0] >= '0' && trimmed[0] <= '9')
        {
            trimmed = "_" + trimmed;
        }

        // 6) 转小写（Go: strings.ToLower(name)）。
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// 校验 application id 是否满足 <c>g_application_id_is_valid()</c> 契约：
    /// 由 '.' 分隔为两个及以上非空元素；每个元素仅含 ASCII 字符 <c>A-Z a-z 0-9 _ -</c>
    /// 且不以数字开头；总长度不超过 255。
    /// 对应 Go <c>validateApplicationID</c>（<c>application_linux_appid.go</c>）。
    /// </summary>
    /// <param name="id">待校验的 application id。</param>
    /// <returns>合法时返回 <c>null</c>；不合法时返回描述原因的说明文本。</returns>
    public static string? ValidateApplicationId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "application id 为空";
        }

        if (id.Length > MaxApplicationIdLength)
        {
            return $"application id \"{id}\" 长度为 {id.Length}，最大为 {MaxApplicationIdLength}";
        }

        var elements = id.Split('.');
        if (elements.Length < 2)
        {
            return $"application id \"{id}\" 需要两个及以上以 '.' 分隔的元素，例如 \"com.example.MyApp\"";
        }

        foreach (var element in elements)
        {
            if (element.Length == 0)
            {
                return $"application id \"{id}\" 含有空元素：不能以 '.' 开头或结尾，也不能包含 \"..\"";
            }

            if (element[0] >= '0' && element[0] <= '9')
            {
                return $"application id \"{id}\" 的元素 \"{element}\" 以数字开头";
            }

            foreach (var c in element)
            {
                if (!IsApplicationIdChar(c))
                {
                    return $"application id \"{id}\" 含非法字符 '{c}'：仅允许 A-Z、a-z、0-9、'_' 与 '-'";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 解析最终使用的 application id：未设置覆盖时返回推导 id；
    /// 设置了合法覆盖时返回覆盖值；覆盖值非法时回退到推导 id 并通过 <paramref name="error"/> 报告原因。
    /// 对应 Go <c>applicationID</c>（<c>application_linux_appid.go</c>）。
    /// </summary>
    /// <param name="applicationName">应用名（<c>ApplicationOptions.Name</c>），用于推导默认 id。</param>
    /// <param name="applicationIdOverride">application id 覆盖值（可为 null）。</param>
    /// <param name="error">覆盖值被拒绝时的原因；无错误时为 <c>null</c>。</param>
    /// <returns>最终使用的 application id（始终是 GTK 可接受的推导 id 或合法的覆盖值）。</returns>
    public static string ResolveApplicationId(string? applicationName, string? applicationIdOverride, out string? error)
    {
        var derived = ApplicationIdPrefix + SanitizeAppName(applicationName);

        // Go: sanitizeAppName 不会产出 '.'，故截断尾部只会留下元素中部合法的字符。
        if (derived.Length > MaxApplicationIdLength)
        {
            derived = derived[..MaxApplicationIdLength];
        }

        error = null;
        if (string.IsNullOrEmpty(applicationIdOverride))
        {
            return derived;
        }

        var validationError = ValidateApplicationId(applicationIdOverride);
        if (validationError is not null)
        {
            error = validationError;
            return derived;
        }

        return applicationIdOverride;
    }

    /// <summary>
    /// 解析交给 GTK <c>g_set_prgname()</c> 的程序名，或返回 <c>null</c> 表示不设置
    /// （保留 GTK 从可执行文件名推断的默认值）。
    /// 对应 Go <c>programName</c>（<c>application_linux_appid.go</c>）。
    /// </summary>
    /// <param name="programNameOverride">显式程序名覆盖值（可为 null/空）。</param>
    /// <param name="applicationIdOverride">application id 覆盖值（可为 null/空）。</param>
    /// <param name="applicationId">实际使用的 application id（可能已被校验回退）。</param>
    /// <returns>要设置的程序名；不应设置时为 <c>null</c>。</returns>
    public static string? ResolveProgramName(
        string? programNameOverride,
        string? applicationIdOverride,
        string applicationId)
    {
        if (!string.IsNullOrEmpty(programNameOverride))
        {
            return programNameOverride;
        }

        // 设置过 ApplicationID 的应用继承实际使用的 id（含校验失败后的回退值），
        // 使程序名不会与该 id 冲突（Go 注释：program name cannot disagree with it）。
        if (!string.IsNullOrEmpty(applicationIdOverride))
        {
            return applicationId;
        }

        return null;
    }

    /// <summary>
    /// 判断字符是否属于 application id / 名称清洗允许的字符集（ASCII 字母、数字、下划线、连字符）。
    /// 对应 Go <c>isApplicationIDChar</c>，同时等价于 Go 名称清洗正则 <c>[^a-zA-Z0-9_-]</c> 的反集。
    /// </summary>
    private static bool IsApplicationIdChar(char c)
    {
        return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';
    }
}
</static_reminder>
</invoke>
