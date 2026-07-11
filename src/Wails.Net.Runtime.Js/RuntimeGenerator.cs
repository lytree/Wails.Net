using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Wails.Net.Runtime.Js;

/// <summary>
/// JavaScript 运行时代码生成器。
/// 对应 Wails v3 Go 版本 <c>internal/runtime/runtime.go</c> 中的运行时生成逻辑。
/// 负责生成注入 Webview 的 JavaScript 运行时代码，包括标志对象、API 对象与传输层。
/// </summary>
public static class RuntimeGenerator
{
    /// <summary>
    /// 运行时模板嵌入资源的文件名。
    /// </summary>
    private const string RuntimeTemplateFileName = "runtime.template.js";

    /// <summary>
    /// 传输层模板嵌入资源的文件名。
    /// </summary>
    private const string TransportTemplateFileName = "transport.template.js";

    /// <summary>
    /// 生成完整的运行时 JavaScript 代码。
    /// 根据 <see cref="RuntimeOptions.IsServerMode" /> 选择桌面或 Server 运行时，
    /// 并组合运行时模板、传输层模板与平台运行时代码。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>生成的完整运行时 JavaScript 代码字符串。</returns>
    public static string Generate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var runtime = LoadTemplate(RuntimeTemplateFileName, options);
        var transport = LoadTemplate(TransportTemplateFileName, options);
        var platformRuntime = options.IsServerMode
            ? ServerRuntime.Generate(options)
            : DesktopRuntime.Generate(options);

        return $"{runtime}\n{transport}\n{platformRuntime}";
    }

    /// <summary>
    /// 生成 <c>window._wails</c> 标志对象。
    /// 包含平台、调试模式、Server 模式等运行时标志。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>包含 <c>window._wails</c> 标志对象的 JavaScript 代码字符串。</returns>
    public static string GenerateFlags(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var platform = JsonSerializer.Serialize(options.Platform);
        var isDebug = FormatBool(options.IsDebug);
        var isServerMode = FormatBool(options.IsServerMode);

        return $$"""
        // Wails.NET Runtime Flags - 自动生成，请勿手动修改
        window._wails = {
          platform: {{platform}},
          isDebug: {{isDebug}},
          isServerMode: {{isServerMode}}
        };
        """;
    }

    /// <summary>
    /// 生成 <c>window.wails</c> API 对象。
    /// 包含绑定调用与事件订阅/发布的 API 接口。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>包含 <c>window.wails</c> API 对象的 JavaScript 代码字符串。</returns>
    public static string GenerateApi(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return """
        window.wails = {
          bindings: {
            call: function(bindingId, args) {
              return window._wailsInvoke("binding.call", { id: bindingId, args: args });
            }
          },
          events: {
            on: function(eventName, callback) {
              return window._wailsInvoke("event.on", { name: eventName, callback: callback });
            },
            emit: function(eventName, data) {
              return window._wailsInvoke("event.emit", { name: eventName, data: data });
            }
          }
        };
        """;
    }

    /// <summary>
    /// 从程序集嵌入资源中加载指定模板并替换占位符。
    /// </summary>
    /// <param name="templateFileName">模板文件名（用于在嵌入资源中按后缀匹配查找）。</param>
    /// <param name="options">运行时生成选项，用于占位符替换。</param>
    /// <returns>替换占位符后的模板内容。</returns>
    /// <exception cref="InvalidOperationException">指定的嵌入资源未找到或无法读取。</exception>
    internal static string LoadTemplate(string templateFileName, RuntimeOptions options)
    {
        var assembly = typeof(RuntimeGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(templateFileName, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"嵌入资源 '{templateFileName}' 未找到。");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"无法读取嵌入资源 '{resourceName}'。");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var template = reader.ReadToEnd();
        return ReplacePlaceholders(template, options);
    }

    /// <summary>
    /// 替换模板中的占位符为实际运行时选项值。
    /// 支持的占位符：<c>{PLATFORM}</c>、<c>{IS_DEBUG}</c>、<c>{IS_SERVER_MODE}</c>、
    /// <c>{ASSET_SERVER_URL}</c>、<c>{WEBSOCKET_URL}</c>。
    /// </summary>
    /// <param name="template">包含占位符的模板字符串。</param>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>占位符替换后的字符串。</returns>
    private static string ReplacePlaceholders(string template, RuntimeOptions options)
    {
        return template
            .Replace("{PLATFORM}", options.Platform)
            .Replace("{IS_DEBUG}", FormatBool(options.IsDebug))
            .Replace("{IS_SERVER_MODE}", FormatBool(options.IsServerMode))
            .Replace("{ASSET_SERVER_URL}", options.AssetServerUrl)
            .Replace("{WEBSOCKET_URL}", options.WebSocketUrl);
    }

    /// <summary>
    /// 将布尔值格式化为 JavaScript 布尔字面量（小写 true/false）。
    /// </summary>
    /// <param name="value">要格式化的布尔值。</param>
    /// <returns>JavaScript 布尔字面量字符串。</returns>
    private static string FormatBool(bool value) => value ? "true" : "false";
}
