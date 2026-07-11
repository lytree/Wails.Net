namespace Wails.Net.Runtime.Js;

/// <summary>
/// 桌面运行时代码生成器。
/// 对应 Wails v3 Go 版本中桌面平台（Windows/Linux）注入 Webview 的运行时生成逻辑。
/// 扩展基础运行时模板，添加原生 IPC 通信支持。
/// </summary>
public static class DesktopRuntime
{
    /// <summary>
    /// 桌面运行时模板嵌入资源的文件名。
    /// </summary>
    private const string DesktopTemplateFileName = "desktop.template.js";

    /// <summary>
    /// 生成桌面运行时 JavaScript 代码。
    /// 读取桌面运行时模板并替换占位符，提供原生 IPC 通信的 JS 实现。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>生成的桌面运行时 JavaScript 代码字符串。</returns>
    public static string Generate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RuntimeGenerator.LoadTemplate(DesktopTemplateFileName, options);
    }
}
