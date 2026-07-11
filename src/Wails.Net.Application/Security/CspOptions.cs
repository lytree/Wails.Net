namespace Wails.Net.Application.Security;

/// <summary>
/// 内容安全策略（CSP）配置选项。
/// 对应 Tauri v2 的 CSP 安全配置。
/// </summary>
public sealed class CspOptions
{
    /// <summary>是否启用 CSP（默认 true）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>默认来源（default-src），默认 'self'</summary>
    public string DefaultSrc { get; set; } = "'self'";

    /// <summary>脚本来源（script-src），默认 'self'</summary>
    public string ScriptSrc { get; set; } = "'self'";

    /// <summary>样式来源（style-src），默认 'self' 'unsafe-inline'</summary>
    public string StyleSrc { get; set; } = "'self' 'unsafe-inline'";

    /// <summary>图片来源（img-src），默认 'self' data:</summary>
    public string ImgSrc { get; set; } = "'self' data:";

    /// <summary>字体来源（font-src），默认 'self'</summary>
    public string FontSrc { get; set; } = "'self'";

    /// <summary>连接来源（connect-src），默认 'self'</summary>
    public string ConnectSrc { get; set; } = "'self'";

    /// <summary>框架来源（frame-src），默认 'none'</summary>
    public string FrameSrc { get; set; } = "'none'";

    /// <summary>对象来源（object-src），默认 'none'</summary>
    public string ObjectSrc { get; set; } = "'none'";

    /// <summary>构建完整的 CSP 头部值</summary>
    public string BuildHeader()
    {
        if (!Enabled) return string.Empty;
        return $"default-src {DefaultSrc}; script-src {ScriptSrc}; style-src {StyleSrc}; img-src {ImgSrc}; font-src {FontSrc}; connect-src {ConnectSrc}; frame-src {FrameSrc}; object-src {ObjectSrc}";
    }
}
