using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.GuiContract.Tests;

/// <summary>
/// 跨平台 Webview 窗口测试夹具接口。
/// <para>
/// 每个平台测试项目实现此接口，提供平台特定的 <see cref="IWebviewWindowImpl"/> 实例创建与清理能力。
/// 契约测试基类通过此接口与平台实现解耦，实现"一份契约规范，三平台分别验证"。
/// </para>
/// </summary>
public interface IWebviewWindowFixture
{
    /// <summary>
    /// 当前平台名称（"windows" / "linux" / "android"）。
    /// 用于在测试输出与契约报告中标识平台。
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// 当前平台是否具备真实 GUI 运行环境。
    /// <para>
    /// 返回 false 时，仅 L1 通用契约（方法不抛异常）会被执行；
    /// L2 功能契约（Get/Set 反映真实状态变化）与 L3 平台特有契约将被跳过。
    /// </para>
    /// </summary>
    bool HasRealGuiEnvironment { get; }

    /// <summary>
    /// 创建一个 <see cref="IWebviewWindowImpl"/> 实例用于测试。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项。</param>
    /// <returns>平台特定的窗口实现实例。</returns>
    IWebviewWindowImpl CreateWindow(uint id, WebviewWindowOptions options);

    /// <summary>
    /// 销毁并清理指定窗口实例，释放所有原生资源。
    /// 多次调用应安全（幂等）。
    /// </summary>
    /// <param name="window">要销毁的窗口实例。</param>
    void DestroyWindow(IWebviewWindowImpl window);

    /// <summary>
    /// 在平台主线程/UI 线程上执行指定操作并等待完成。
    /// <para>
    /// - Windows：在 STA 线程执行（Win32 窗口要求）。
    /// - Linux：在 GTK 主线程执行（通过 GLib.MainContext）。
    /// - Android：在 MainLooper 上执行（通过 Handler.Post）。
    /// </para>
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    void RunOnUiThread(Action action);

    /// <summary>
    /// 在平台主线程/UI 线程上执行指定函数并返回结果。
    /// </summary>
    /// <typeparam name="T">返回类型。</typeparam>
    /// <param name="func">要执行的函数。</param>
    /// <returns>函数返回值。</returns>
    T RunOnUiThread<T>(Func<T> func);
}

/// <summary>
/// Webview 窗口契约测试的分级标记。
/// <para>
/// 用于在测试方法上通过 <c>[Category]</c> 标记所属契约级别，
/// 便于在 CI 中按级别过滤执行，或在缺少真实 GUI 环境时跳过 L2/L3 测试。
/// </para>
/// </summary>
public static class WindowContractLevel
{
    /// <summary>
    /// L1 通用契约：所有平台必须满足。
    /// <para>验证点：方法不抛异常、返回类型正确、空实现 no-op 行为明确。</para>
    /// </summary>
    public const string L1Universal = "L1-Universal";

    /// <summary>
    /// L2 功能契约：调用后状态变化可观察。
    /// <para>验证点：Set* 方法后 Get* 方法返回新值、状态查询方法反映真实状态。</para>
    /// <para>注意：Android 平台因窗口管理由系统控制，部分 L2 契约需标记为跳过。</para>
    /// </summary>
    public const string L2Functional = "L2-Functional";

    /// <summary>
    /// L3 平台特有契约：仅在该平台有意义的功能。
    /// <para>例如 Windows 的 Mica/Acrylic 特效、Linux 的 GTK CSS 类、Android 的 Activity 生命周期。</para>
    /// </summary>
    public const string L3PlatformSpecific = "L3-PlatformSpecific";
}
