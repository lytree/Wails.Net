using System.Text.Json.Serialization;

namespace Wails.Net.Application.Windows;

/// <summary>
/// 文件拖放目标详情，对应 Wails v3 Go 版本 <c>DropTargetDetails</c> 结构。
/// <para>
/// 后端在收到文件拖放事件后，通过注入脚本查询拖放位置的 HTML 元素信息，
/// 让后端能识别拖放目标元素的 ID / class / attributes，适用于"指定区域接收文件"场景。
/// </para>
/// <para>
/// 前端通过在 HTML 元素上添加 <c>data-file-drop-target</c> 属性标记后端可识别的拖放区域；
/// 注入脚本扫描该属性并填充 ElementID / ClassList / Attributes。
/// </para>
/// </summary>
public sealed class DropTargetDetails
{
    /// <summary>
    /// 拖放位置的 X 坐标（相对于窗口客户端区，单位像素）。
    /// </summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>
    /// 拖放位置的 Y 坐标（相对于窗口客户端区，单位像素）。
    /// </summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>
    /// 拖放目标元素的 ID（HTML <c>id</c> 属性值），若元素无 ID 则为空字符串。
    /// </summary>
    [JsonPropertyName("elementId")]
    public string ElementId { get; set; } = string.Empty;

    /// <summary>
    /// 拖放目标元素的 class 列表（HTML <c>class</c> 属性解析后的列表）。
    /// </summary>
    [JsonPropertyName("classList")]
    public List<string> ClassList { get; set; } = new();

    /// <summary>
    /// 拖放目标元素的所有 <c>data-*</c> 属性字典（用于业务侧自定义标记识别）。
    /// 键为属性名（不含 <c>data-</c> 前缀），值为属性值。
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, string> Attributes { get; set; } = new();
}
