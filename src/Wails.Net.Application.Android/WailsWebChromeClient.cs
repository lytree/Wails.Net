using Android.Util;
using Android.Webkit;

namespace Wails.Net.Application.Android;

/// <summary>
/// Android WebView 的 WebChromeClient 实现，将前端 JS 的 <c>console.log</c> / <c>alert</c> 等转发到 Android Logcat。
/// 对应 ADR-0002 §5：调试支持。
/// </summary>
/// <remarks>
/// 不设置 WebChromeClient 时，WebView 默认不会输出 console.log 到 logcat，
/// 调试前端 JS 错误非常困难。本类将所有控制台消息以 <c>Wails.WebView</c> tag 输出到 Logcat。
/// </remarks>
public sealed class WailsWebChromeClient : WebChromeClient
{
    /// <summary>
    /// Logcat 输出的 tag。
    /// </summary>
    private const string LogTag = "Wails.WebView";

    /// <summary>
    /// 转发 console.log / console.info / console.warn / console.error 到 Logcat。
    /// </summary>
    /// <param name="consoleMessage">控制台消息对象，包含消息文本、行号、来源 ID。</param>
    /// <returns>始终返回 true，表示已处理。</returns>
    public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
    {
        if (consoleMessage is null)
        {
            return base.OnConsoleMessage(consoleMessage);
        }

        // .NET Android 绑定：Message/SourceId/LineNumber 均为属性（原 Java 方法）
        var msg = $"[JS:{consoleMessage.SourceId()}:{consoleMessage.LineNumber()}] {consoleMessage.Message()}";

        // 统一以 Info 级别输出，避免 .NET Android 不同版本的 MessageLevel 枚举差异
        Log.Info(LogTag, msg);

        return true;
    }
}
