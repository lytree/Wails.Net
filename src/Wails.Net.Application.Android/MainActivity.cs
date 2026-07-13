using Android.App;
using Android.OS;
using Android.Webkit;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Android 主 Activity 入口示例。
/// 对应 ADR-0002 §3：单 Activity + Fragment 窗口模型，Activity 作为 Host 宿主。
/// 实际项目中应在此 Activity 中启动 .NET Generic Host 并创建 Wails.Net Application。
/// </summary>
[Activity(Label = "Wails.Net", MainLauncher = true)]
public class MainActivity : Activity
{
    /// <summary>
    /// 主 WebView 实例，承载前端内容。
    /// </summary>
    private WebView? _webView;

    /// <summary>
    /// Activity 创建时调用，初始化 WebView 并加载前端资源。
    /// </summary>
    /// <param name="savedInstanceState">保存的实例状态，首次启动为 null。</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 创建 WebView 并配置
        _webView = new WebView(this);
        var settings = _webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = true;
        settings.AllowContentAccess = true;

        // 启用 WebView 调试
        WebView.SetWebContentsDebuggingEnabled(true);

        // 设置 WebView 为内容视图
        SetContentView(_webView);

        // TODO: 启动 Wails.Net Generic Host 并创建 Application
        // 完整集成示例：
        //   var app = Wails.Net.Application.Application.Create();
        //   app.UseAndroid().Run();
        //   var window = app.NewWebviewWindow(new WebviewWindowOptions { URL = "file:///android_asset/index.html" });

        // 加载前端入口页面（从 assets 目录）
        _webView.LoadUrl("file:///android_asset/index.html");
    }

    /// <summary>
    /// Activity 销毁时调用，清理 WebView 资源。
    /// </summary>
    protected override void OnDestroy()
    {
        _webView?.Destroy();
        _webView = null;
        base.OnDestroy();
    }
}
