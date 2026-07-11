using System.Diagnostics;
using System.Text;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 通知记录，保存已发送通知的元数据。
/// 对应 Wails v3 Go 版本 pkg/services/notifications 中的通知记录结构。
/// </summary>
public sealed class NotificationRecord
{
    /// <summary>
    /// 获取通知唯一标识。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 获取通知标题。
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 获取通知正文。
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// 获取操作按钮文本，可为 null。
    /// </summary>
    public string? ActionText { get; init; }

    /// <summary>
    /// 获取通知发送时间戳（UTC）。
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 获取通知是否已被取消。
    /// </summary>
    public bool IsCancelled { get; set; }
}

/// <summary>
/// 通知服务，允许应用发送系统通知。
/// 对应 Wails v3 Go 版本 pkg/services/notifications。
/// 跨平台支持：Windows 使用 WinRT Toast 通知（通过 PowerShell 调用），Linux 使用 notify-send 命令（回退到 dbus-send）。
/// </summary>
public class NotificationService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 已发送通知的记录字典，键为通知 ID。
    /// </summary>
    private readonly Dictionary<string, NotificationRecord> _notifications = new();

    /// <summary>
    /// 通知记录的线程安全锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 图标临时文件路径缓存。
    /// </summary>
    private string? _iconTempPath;

    /// <summary>
    /// 通知被点击时触发，参数为通知 ID。
    /// 注意：简化实现下（Toast / notify-send）原生通知不支持点击回调，
    /// 通过 <see cref="TriggerNotificationClick"/> 方法可程序化触发此事件，
    /// 例如前端通过 IPC 回调通知点击。
    /// </summary>
    public event Action<string>? NotificationClicked;

    /// <summary>
    /// 通知被关闭时触发，参数为通知 ID。
    /// 注意：简化实现下此事件不会被触发。
    /// </summary>
    public event Action<string>? NotificationClosed;

    /// <summary>
    /// 获取或设置通知图标的字节数据。
    /// 设置后将在支持图标的平台上使用（Linux 的 notify-send）。
    /// Windows 的 Toast 通知暂不支持自定义图标，此属性被忽略。
    /// </summary>
    public byte[]? Icon { get; set; }

    /// <summary>
    /// 服务启动，初始化通知服务。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，清理临时资源。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public Task ServiceShutdown(CancellationToken cancellationToken)
    {
        CleanupIconTempFile();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送系统通知（向后兼容方法）。
    /// 尝试调用平台原生的通知机制，失败时仅记录通知不抛出异常。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    public void SendNotification(string title, string body)
    {
        var record = new NotificationRecord
        {
            Id = GenerateNotificationId(),
            Title = title,
            Body = body,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock)
        {
            _notifications[record.Id] = record;
        }

        TrySendPlatformNotification(title, body, null);
    }

    /// <summary>
    /// 发送带操作按钮的系统通知（向后兼容方法）。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="actionText">操作按钮文本。</param>
    public void SendNotificationWithAction(string title, string body, string actionText)
    {
        var record = new NotificationRecord
        {
            Id = GenerateNotificationId(),
            Title = title,
            Body = body,
            ActionText = actionText,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock)
        {
            _notifications[record.Id] = record;
        }

        TrySendPlatformNotification(title, $"{body} [{actionText}]", null);
    }

    /// <summary>
    /// 显示系统通知并返回通知 ID，支持后续取消操作。
    /// 对应 Wails v3 中 NotificationService.Notify 方法。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="icon">通知图标字节数据，为 null 时使用默认图标。</param>
    /// <returns>通知唯一标识，可用于 <see cref="CancelNotification"/> 取消通知。</returns>
    public string ShowNotification(string title, string body, byte[]? icon = null)
    {
        var effectiveIcon = icon ?? Icon;
        var id = GenerateNotificationId();
        var record = new NotificationRecord
        {
            Id = id,
            Title = title,
            Body = body,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock)
        {
            _notifications[id] = record;
        }

        TrySendPlatformNotification(title, body, effectiveIcon);
        return id;
    }

    /// <summary>
    /// 显示带操作按钮的系统通知。
    /// 简化实现：将按钮文本追加到通知正文中，按钮回调存储但不会被自动触发。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="actions">按钮文本到回调的映射。</param>
    /// <returns>通知唯一标识。</returns>
    public string ShowNotificationWithActions(string title, string body, Dictionary<string, Action> actions)
    {
        var id = GenerateNotificationId();
        var actionText = actions.Count > 0
            ? string.Join(" | ", actions.Keys)
            : null;

        var record = new NotificationRecord
        {
            Id = id,
            Title = title,
            Body = body,
            ActionText = actionText,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock)
        {
            _notifications[id] = record;
        }

        // 简化实现：将按钮文本追加到正文中显示
        var displayBody = actions.Count > 0
            ? $"{body} [{actionText}]"
            : body;

        TrySendPlatformNotification(title, displayBody, Icon);
        return id;
    }

    /// <summary>
    /// 取消指定 ID 的通知。
    /// 简化实现下（Toast / notify-send）无法真正撤回已发送的通知，
    /// 此方法仅将通知标记为已取消并从内部记录中移除。
    /// </summary>
    /// <param name="id">通知 ID。</param>
    /// <returns>如果通知存在并已取消返回 true，否则返回 false。</returns>
    public bool CancelNotification(string id)
    {
        lock (_lock)
        {
            if (_notifications.TryGetValue(id, out var record))
            {
                record.IsCancelled = true;
                _notifications.Remove(id);
                NotificationClosed?.Invoke(id);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取所有已发送通知的记录（主要用于测试验证）。
    /// </summary>
    /// <returns>通知记录的只读列表。</returns>
    internal IReadOnlyList<NotificationRecord> GetNotifications()
    {
        lock (_lock)
        {
            return _notifications.Values
                .OrderBy(r => r.Timestamp)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// 程序化触发通知点击事件。
    /// 简化实现下（Toast / notify-send）原生通知不支持点击回调，
    /// 前端可通过 IPC 调用此方法来模拟通知点击，触发 <see cref="NotificationClicked"/> 事件。
    /// 对应 Wails v3 Go 版本中通过 NotificationActivation 回调触发的方式。
    /// </summary>
    /// <param name="id">通知 ID。</param>
    /// <returns>若通知存在且事件已触发返回 true，否则返回 false。</returns>
    public bool TriggerNotificationClick(string id)
    {
        lock (_lock)
        {
            if (!_notifications.ContainsKey(id))
            {
                return false;
            }
        }

        NotificationClicked?.Invoke(id);
        return true;
    }

    /// <summary>
    /// 生成通知唯一标识。
    /// </summary>
    /// <returns>32 位 GUID 字符串（无连字符）。</returns>
    private static string GenerateNotificationId()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 尝试调用平台原生的通知机制。
    /// 调用失败时静默忽略，不影响服务正常运行。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="icon">图标字节数据，可为 null。</param>
    private void TrySendPlatformNotification(string title, string body, byte[]? icon)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                SendLinuxNotification(title, body, icon);
            }
            else if (OperatingSystem.IsWindows())
            {
                SendWindowsNotification(title, body);
            }
            // 其他平台暂不调用原生通知
        }
        catch
        {
            // 平台通知调用失败时静默忽略
        }
    }

    /// <summary>
    /// 通过 notify-send 命令发送 Linux 桌面通知。
    /// 当 notify-send 不可用或调用失败时，回退到 dbus-send 直接调用 FreeDesktop 通知接口。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="icon">图标字节数据，为 null 时使用默认图标名称。</param>
    private void SendLinuxNotification(string title, string body, byte[]? icon)
    {
        var iconArg = ResolveLinuxIcon(icon);
        var psi = new ProcessStartInfo
        {
            FileName = "notify-send",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("--urgency");
        psi.ArgumentList.Add("normal");
        psi.ArgumentList.Add("--icon");
        psi.ArgumentList.Add(iconArg);
        psi.ArgumentList.Add("--app-name");
        psi.ArgumentList.Add("Wails.Net");
        psi.ArgumentList.Add("--expire-time");
        psi.ArgumentList.Add("5000");
        psi.ArgumentList.Add(title);
        psi.ArgumentList.Add(body);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return;
            }

            // 等待 notify-send 完成，最多 3 秒，超时不阻塞应用。
            process.WaitForExit(3000);

            // 检查是否成功执行，失败时回退到 dbus-send。
            if (!process.HasExited || process.ExitCode != 0)
            {
                SendLinuxNotificationViaDbus(title, body);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // notify-send 未安装，回退到 dbus-send 直接调用 FreeDesktop 通知接口。
            SendLinuxNotificationViaDbus(title, body);
        }
    }

    /// <summary>
    /// 通过 dbus-send 调用 FreeDesktop 通知接口发送 Linux 桌面通知。
    /// 当 notify-send 不可用时作为回退方案使用。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    private void SendLinuxNotificationViaDbus(string title, string body)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dbus-send",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--session");
        psi.ArgumentList.Add("--dest=org.freedesktop.Notifications");
        psi.ArgumentList.Add("--type=method_call");
        psi.ArgumentList.Add("/org/freedesktop/Notifications");
        psi.ArgumentList.Add("org.freedesktop.Notifications.Notify");
        psi.ArgumentList.Add("string:Wails.Net");
        psi.ArgumentList.Add("uint32:0");
        psi.ArgumentList.Add("string:dialog-information");
        psi.ArgumentList.Add($"string:{title}");
        psi.ArgumentList.Add($"string:{body}");
        psi.ArgumentList.Add("array:string:");
        psi.ArgumentList.Add("dict:string:variant:");
        psi.ArgumentList.Add("int32:5000");

        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch
        {
            // dbus-send 也不可用时静默忽略，通知发送失败不影响应用运行。
        }
    }

    /// <summary>
    /// 解析 Linux 通知图标参数。
    /// 若提供了图标字节数据则写入临时文件返回路径，否则返回默认图标名称。
    /// </summary>
    /// <param name="icon">图标字节数据。</param>
    /// <returns>图标路径或图标名称。</returns>
    private string ResolveLinuxIcon(byte[]? icon)
    {
        if (icon is null || icon.Length == 0)
        {
            return "dialog-information";
        }

        // 缓存图标临时文件，避免重复写入
        if (_iconTempPath is not null && File.Exists(_iconTempPath))
        {
            return _iconTempPath;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"wails-net-icon-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempPath, icon);
        _iconTempPath = tempPath;
        return tempPath;
    }

    /// <summary>
    /// 通过 PowerShell 调用 WinRT Toast 通知 API 发送 Windows 10+ 原生 Toast 通知。
    /// 对应 Wails v3 Go 版本中通过 COM 调用 IToastNotifier 的实现。
    /// 注意：使用 PowerShell 进程调用 WinRT API 避免引入新的 NuGet 包依赖。
    /// AppUserModelID 未在系统注册时 Toast 仍可显示，但应用名称和图标可能不正确；
    /// 完整实现应注册 AUMID 并关联快捷方式以获得正确的应用标识。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    private void SendWindowsNotification(string title, string body)
    {
        var xmlTitle = XmlEscape(title);
        var xmlBody = XmlEscape(body);
        var toastXml = $"<toast><visual><binding template=\"ToastText02\"><text id=\"1\">{xmlTitle}</text><text id=\"2\">{xmlBody}</text></binding></visual></toast>";

        // PowerShell 脚本通过 WinRT 投影调用 ToastNotificationManager 显示 Toast 通知。
        // 使用 -EncodedCommand 传递 Base64 编码的 UTF-16 脚本，避免字符串转义问题。
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml('{toastXml}')
    $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Wails.Net')
    $notifier.Show($toast)
}} catch {{
    # WinRT Toast 调用失败时静默忽略
}}";

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encodedCommand);

        using var process = Process.Start(psi);
        if (process is not null)
        {
            // 等待 PowerShell 执行完成，最多 10 秒。
            process.WaitForExit(10000);
        }
    }

    /// <summary>
    /// 对字符串进行 XML 实体转义，确保可安全嵌入 XML 文本节点。
    /// </summary>
    /// <param name="text">需要转义的文本。</param>
    /// <returns>转义后的字符串。</returns>
    private static string XmlEscape(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// 清理图标临时文件。
    /// </summary>
    private void CleanupIconTempFile()
    {
        if (_iconTempPath is not null && File.Exists(_iconTempPath))
        {
            try
            {
                File.Delete(_iconTempPath);
            }
            catch
            {
                // 清理失败时忽略
            }

            _iconTempPath = null;
        }
    }
}
