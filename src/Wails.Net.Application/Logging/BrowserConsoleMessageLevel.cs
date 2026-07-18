namespace Wails.Net.Application.Logging;

/// <summary>
/// 浏览器控制台消息级别（P1-3-4）。
/// 对应 W3C Console Standard 中的 console 方法分类，用于平台 console 事件转发到后端日志。
/// </summary>
public enum BrowserConsoleMessageLevel
{
    /// <summary>
    /// 调试级别（console.debug）。
    /// </summary>
    Debug = 0,

    /// <summary>
    /// 信息级别（console.info / console.log）。
    /// </summary>
    Info = 1,

    /// <summary>
    /// 警告级别（console.warn）。
    /// </summary>
    Warning = 2,

    /// <summary>
    /// 错误级别（console.error / console.assert 失败）。
    /// </summary>
    Error = 3,
}
