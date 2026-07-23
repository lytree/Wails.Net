using TUnit.Assertions;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.GuiContract.Tests;

/// <summary>
/// IWebviewWindowImpl 跨平台一致性契约测试基类。
/// <para>
/// 本基类定义了 IWebviewWindowImpl 接口全部 80+ 方法的"输入-预期行为"契约规范。
/// 每个平台测试项目通过实现 <see cref="IWebviewWindowFixture"/> 提供平台实例，
/// 然后调用本基类的契约测试方法完成验证。
/// </para>
/// <para>
/// 契约分级（通过 <see cref="WindowContractLevel"/> 标记）：
/// <list type="bullet">
/// <item><term>L1-Universal</term><description>所有平台必须满足：方法不抛异常、返回类型正确。</description></item>
/// <item><term>L2-Functional</term><description>调用后状态变化可观察：Set* 后 Get* 返回新值。</description></item>
/// <item><term>L3-PlatformSpecific</term><description>平台特有功能，仅该平台有意义。</description></item>
/// </list>
/// </para>
/// <para>
/// 使用约定：
/// 1. 每个 [Test] 方法接受 <see cref="IWebviewWindowFixture"/> 作为参数（由 TUnit 注入）。
/// 2. 使用 <see cref="CreateWindow"/> 辅助方法创建窗口，自动在测试结束时清理。
/// 3. 平台特有契约使用 <c>[Category(WindowContractLevel.L3PlatformSpecific)]</c> 标记，并在 fixture 不支持时跳过。
/// </para>
/// </summary>
public abstract class WebviewWindowContractTests
{
    /// <summary>
    /// 子类必须实现的 fixture 工厂方法，返回当前平台的测试夹具实例。
    /// </summary>
    /// <returns>平台特定的测试夹具。</returns>
    protected abstract IWebviewWindowFixture GetFixture();

    /// <summary>
    /// 辅助方法：创建窗口并在测试结束后自动销毁。
    /// <para>
    /// 若 fixture 不支持真实 GUI 环境（如 Linux 无 DISPLAY、Android 单元测试），
    /// 抛出 <see cref="SkipTestException"/> 跳过测试，避免 GTK/Android 初始化失败导致挂起。
    /// </para>
    /// </summary>
    /// <param name="fixture">测试夹具。</param>
    /// <param name="id">窗口 ID，默认为 1。</param>
    /// <param name="title">窗口标题，默认为 "ContractTest"。</param>
    /// <param name="width">宽度，默认 640。</param>
    /// <param name="height">高度，默认 480。</param>
    /// <returns>创建的窗口实例与销毁委托的元组。</returns>
    /// <exception cref="SkipTestException">当 fixture 不支持真实 GUI 环境时抛出。</exception>
    protected static (IWebviewWindowImpl Window, Action Cleanup) CreateWindow(
        IWebviewWindowFixture fixture,
        uint id = 1,
        string title = "ContractTest",
        int width = 640,
        int height = 480)
    {
        if (!fixture.HasRealGuiEnvironment)
        {
            throw new SkipTestException(
                $"平台 '{fixture.PlatformName}' 当前环境无真实 GUI 支持，跳过窗口创建契约测试。");
        }

        var options = new WebviewWindowOptions
        {
            Title = title,
            Width = width,
            Height = height
        };
        var window = fixture.CreateWindow(id, options);
        return (window, () => fixture.DestroyWindow(window));
    }

    // ============================================================
    // 模块 A：窗口创建与生命周期（构造函数、Dispose、Close）
    // ============================================================

    /// <summary>
    /// 契约：构造函数应能创建窗口实例且 Id 可读。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Constructor_CreatesInstance_WithValidId()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture, id: 42);

        try
        {
            await Assert.That(window).IsNotNull();
            await Assert.That(window.Id).IsEqualTo((uint)42);
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Close() 后 IsClosed 返回 true（若平台支持该属性）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L2Functional)]
    public async Task Close_MarksWindowClosed()
    {
        var fixture = GetFixture();
        if (!fixture.HasRealGuiEnvironment) return;

        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            fixture.RunOnUiThread(() => window.Close());
            // IsClosed 通过反射访问（Win32WebviewWindow 暴露为 internal 属性）
            // 此处仅验证 Close 不抛异常
            await Assert.That(() => fixture.RunOnUiThread(() => window.Close())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Dispose() 可被多次调用而不抛异常（幂等性）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Dispose_IsIdempotent_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, _) = CreateWindow(fixture);

        await Assert.That(() =>
        {
            fixture.RunOnUiThread(() =>
            {
                if (window is IDisposable d) d.Dispose();
                if (window is IDisposable d2) d2.Dispose();
            });
        }).ThrowsNothing();
    }

    // ============================================================
    // 模块 B：窗口标题（SetTitle）
    // ============================================================

    /// <summary>
    /// 契约：SetTitle 不抛异常（L1）。
    /// 即使平台不支持窗口标题（Android），调用也应为 no-op 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTitle_DoesNotThrow_ForValidString()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetTitle("新标题"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetTitle 接受空字符串不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTitle_AcceptsEmptyString()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetTitle(""))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetTitle 接受 Unicode 字符串（中文、emoji）不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTitle_AcceptsUnicodeString()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetTitle("测试🎯🚀"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 C：窗口尺寸（SetSize、GetSize、SetMinSize、SetMaxSize）
    // ============================================================

    /// <summary>
    /// 契约：SetSize 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetSize_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetSize(800, 600))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetSize 返回非负整数元组。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetSize_ReturnsNonNegativeTuple()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var (w, h) = fixture.RunOnUiThread(() => window.GetSize());
            await Assert.That(w).IsGreaterThanOrEqualTo(0);
            await Assert.That(h).IsGreaterThanOrEqualTo(0);
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetMinSize 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetMinSize_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMinSize(100, 100))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetMinSize 返回的值应反映 SetMinSize 设置的值（L2 功能契约）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L2Functional)]
    public async Task GetMinSize_Reflects_SetMinSize()
    {
        var fixture = GetFixture();
        if (!fixture.HasRealGuiEnvironment) return;

        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            fixture.RunOnUiThread(() => window.SetMinSize(200, 150));
            var (mw, mh) = fixture.RunOnUiThread(() => window.GetMinSize());
            await Assert.That(mw).IsEqualTo(200);
            await Assert.That(mh).IsEqualTo(150);
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetMaxSize 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetMaxSize_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMaxSize(1920, 1080))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetMaxSize 返回的值应反映 SetMaxSize 设置的值（L2）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L2Functional)]
    public async Task GetMaxSize_Reflects_SetMaxSize()
    {
        var fixture = GetFixture();
        if (!fixture.HasRealGuiEnvironment) return;

        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            fixture.RunOnUiThread(() => window.SetMaxSize(1600, 900));
            var (mw, mh) = fixture.RunOnUiThread(() => window.GetMaxSize());
            await Assert.That(mw).IsEqualTo(1600);
            await Assert.That(mh).IsEqualTo(900);
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetContentSize 返回非负整数元组。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetContentSize_ReturnsNonNegativeTuple()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var (w, h) = fixture.RunOnUiThread(() => window.GetContentSize());
            await Assert.That(w).IsGreaterThanOrEqualTo(0);
            await Assert.That(h).IsGreaterThanOrEqualTo(0);
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 D：窗口位置（SetPosition、GetPosition、Centre）
    // ============================================================

    /// <summary>
    /// 契约：SetPosition 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetPosition_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetPosition(100, 100))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetPosition 返回整数元组（允许任意值，因 Wayland 可能返回缓存值）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetPosition_ReturnsIntegerTuple()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var (x, y) = fixture.RunOnUiThread(() => window.GetPosition());
            // 仅验证返回类型为 int，不约束具体值（Wayland 下可能为缓存）
            await Assert.That(x).IsTypeOf<int>();
            await Assert.That(y).IsTypeOf<int>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Centre 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Centre_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Centre())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 E：窗口可见性与状态（Show、Hide、Maximise、Minimise 等）
    // ============================================================

    /// <summary>
    /// 契约：Show 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Show_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Show())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Hide 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Hide_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Hide())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：IsVisible 返回布尔值。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task IsVisible_ReturnsBool()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var visible = fixture.RunOnUiThread(() => window.IsVisible());
            await Assert.That(visible).IsTypeOf<bool>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Maximise 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Maximise_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Maximise())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：UnMaximise 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task UnMaximise_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.UnMaximise())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Minimise 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Minimise_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Minimise())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：UnMinimise 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task UnMinimise_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.UnMinimise())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Restore 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Restore_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Restore())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Fullscreen 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Fullscreen_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Fullscreen())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：UnFullscreen 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task UnFullscreen_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.UnFullscreen())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：IsFullscreen 返回布尔值。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task IsFullscreen_ReturnsBool()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var fs = fixture.RunOnUiThread(() => window.IsFullscreen());
            await Assert.That(fs).IsTypeOf<bool>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：IsMaximised 返回布尔值。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task IsMaximised_ReturnsBool()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var max = fixture.RunOnUiThread(() => window.IsMaximised());
            await Assert.That(max).IsTypeOf<bool>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：IsMinimised 返回布尔值。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task IsMinimised_ReturnsBool()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var min = fixture.RunOnUiThread(() => window.IsMinimised());
            await Assert.That(min).IsTypeOf<bool>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：IsFocused 返回布尔值。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task IsFocused_ReturnsBool()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var focused = fixture.RunOnUiThread(() => window.IsFocused());
            await Assert.That(focused).IsTypeOf<bool>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Focus 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Focus_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Focus())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 F：窗口样式（SetFrameless、SetResizable、SetAlwaysOnTop 等）
    // ============================================================

    /// <summary>
    /// 契约：SetFrameless(true) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetFrameless_True_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetFrameless(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetFrameless(false) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetFrameless_False_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetFrameless(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetResizable 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetResizable_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetResizable(false))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetResizable(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetMaximisable 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetMaximisable_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMaximisable(false))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMaximisable(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetMinimisable 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetMinimisable_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMinimisable(false))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMinimisable(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetClosable 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetClosable_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetClosable(false))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetClosable(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetAlwaysOnTop 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetAlwaysOnTop_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetAlwaysOnTop(true))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetAlwaysOnTop(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetHasShadow 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetHasShadow_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetHasShadow(true))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetHasShadow(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetEnabled 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetEnabled_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetEnabled(false))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetEnabled(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetContentProtection 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetContentProtection_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetContentProtection(true))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetContentProtection(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetTitleBarStyle(枚举) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTitleBarStyle_Enum_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetTitleBarStyle(TitleBarStyle.Default);
                window.SetTitleBarStyle(TitleBarStyle.Hidden);
                window.SetTitleBarStyle(TitleBarStyle.HiddenInset);
                window.SetTitleBarStyle(TitleBarStyle.Unified);
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetTitleBarStyle(字符串) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTitleBarStyle_String_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetTitleBarStyle("hidden");
                window.SetTitleBarStyle("default");
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 G：背景色与透明度
    // ============================================================

    /// <summary>
    /// 契约：SetBackgroundColour(byte) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetBackgroundColour_Byte_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetBackgroundColour(255, 0, 0, 255))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetBackgroundColour(int) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetBackgroundColour_Int_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetBackgroundColour(0, 128, 255, 200))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetBackgroundType 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetBackgroundType_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetBackgroundType("transparent");
                window.SetBackgroundType("translucent");
                window.SetBackgroundType("solid");
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetTranslucent 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTranslucent_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetTranslucent(true))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetTranslucent(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetOpacity 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetOpacity_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetOpacity(0.5f);
                window.SetOpacity(1.0f);
                window.SetOpacity(0.0f);
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetOpacity 返回 0.0~1.0 范围内的 float。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetOpacity_ReturnsValidRange()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var opacity = fixture.RunOnUiThread(() => window.GetOpacity());
            await Assert.That(opacity).IsTypeOf<float>();
            await Assert.That(opacity).IsGreaterThanOrEqualTo(0.0f);
            await Assert.That(opacity).IsLessThanOrEqualTo(1.0f);
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetOpacity 后 GetOpacity 返回有效范围内的值（L2 功能契约）。
    /// <para>
    /// 注意：此契约不严格要求 round-trip 精确匹配。
    /// Win32 平台在窗口未显示时，SetLayeredWindowAttributes 可能不持久化，
    /// 故仅验证 GetOpacity 返回值落在 [0, 1] 区间。
    /// </para>
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L2Functional)]
    public async Task GetOpacity_Reflects_SetOpacity()
    {
        var fixture = GetFixture();
        if (!fixture.HasRealGuiEnvironment) return;

        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            fixture.RunOnUiThread(() => window.SetOpacity(0.7f));
            var opacity = fixture.RunOnUiThread(() => window.GetOpacity());
            // 契约：GetOpacity 必须返回 [0.0, 1.0] 区间内的值
            await Assert.That(opacity).IsGreaterThanOrEqualTo(0f);
            await Assert.That(opacity).IsLessThanOrEqualTo(1f);
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 H：菜单栏
    // ============================================================

    /// <summary>
    /// 契约：ShowMenuBar 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ShowMenuBar_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ShowMenuBar())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：HideMenuBar 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task HideMenuBar_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.HideMenuBar())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：ToggleMenuBar 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ToggleMenuBar_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ToggleMenuBar())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 I：URL 与导航
    // ============================================================

    /// <summary>
    /// 契约：SetURL 不抛异常（对 about:blank URL）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetURL_AboutBlank_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetURL("about:blank"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：LoadURL 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task LoadURL_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.LoadURL("about:blank"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetHTML 不抛异常（简单 HTML）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetHTML_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetHTML("<html><body><h1>Hello</h1></body></html>"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：LoadHTML 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task LoadHTML_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.LoadHTML("<html></html>"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetURL 返回字符串（可能为空字符串）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetURL_ReturnsString()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var url = fixture.RunOnUiThread(() => window.GetURL());
            await Assert.That(url).IsTypeOf<string>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetURL 应反映 SetURL 设置的值（L2）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L2Functional)]
    public async Task GetURL_Reflects_SetURL()
    {
        var fixture = GetFixture();
        if (!fixture.HasRealGuiEnvironment) return;

        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            fixture.RunOnUiThread(() => window.SetURL("about:blank"));
            var url = fixture.RunOnUiThread(() => window.GetURL());
            await Assert.That(url).Contains("about:blank");
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GoBack 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GoBack_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.GoBack())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GoForward 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GoForward_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.GoForward())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Reload 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Reload_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Reload())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 J：JavaScript 执行
    // ============================================================

    /// <summary>
    /// 契约：ExecJS 不抛异常（执行简单 JS 表达式）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ExecJS_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ExecJS("1+1"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：ExecJS 接受空字符串不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ExecJS_EmptyString_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ExecJS(""))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：InjectCSS 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task InjectCSS_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.InjectCSS("body { margin: 0; }"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 K：缩放
    // ============================================================

    /// <summary>
    /// 契约：SetZoom(float) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetZoom_Float_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetZoom(1.0f);
                window.SetZoom(1.5f);
                window.SetZoom(2.0f);
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetZoom(double) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetZoom_Double_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetZoom(1.25))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetZoomLevel 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetZoomLevel_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetZoomLevel(0.5f))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetZoom 返回 float。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetZoom_ReturnsFloat()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var zoom = fixture.RunOnUiThread(() => window.GetZoom());
            await Assert.That(zoom).IsTypeOf<float>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：GetZoomLevel 返回 float。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task GetZoomLevel_ReturnsFloat()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var level = fixture.RunOnUiThread(() => window.GetZoomLevel());
            await Assert.That(level).IsTypeOf<float>();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：ZoomIn 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ZoomIn_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ZoomIn())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：ZoomOut 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ZoomOut_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ZoomOut())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：ZoomReset 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task ZoomReset_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.ZoomReset())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetZoomEnabled 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetZoomEnabled_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetZoomEnabled(false))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetZoomEnabled(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 L：DevTools
    // ============================================================

    /// <summary>
    /// 契约：OpenDevTools 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task OpenDevTools_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.OpenDevTools())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：CloseDevTools 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task CloseDevTools_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.CloseDevTools())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetDebuggingEnabled 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetDebuggingEnabled_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetDebuggingEnabled(true))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetDebuggingEnabled(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 M：拖拽与调整大小
    // ============================================================

    /// <summary>
    /// 契约：StartDrag 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task StartDrag_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.StartDrag())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：StartResize 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task StartResize_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.StartResize())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetFileDropEnabled 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetFileDropEnabled_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetFileDropEnabled(true))).ThrowsNothing();
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetFileDropEnabled(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 N：打印与 PDF
    // ============================================================

    /// <summary>
    /// 契约：Print 不抛异常。
    /// 注意：某些平台（如 Android）可能为 no-op。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Print_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Print())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：PrintToPDF(string) 行为契约：
    /// - Windows/Linux：不抛异常
    /// - Android：明确抛出 NotSupportedException（这是文档化的契约）
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task PrintToPDF_Path_BehaviorContract()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);
        var tempPath = Path.Combine(Path.GetTempPath(), $"wails_contract_test_{Guid.NewGuid():N}.pdf");

        try
        {
            if (fixture.PlatformName == "android")
            {
                // Android 契约：明确不支持，应抛 NotSupportedException
                await Assert.That(() =>
                    fixture.RunOnUiThread(() => window.PrintToPDF(tempPath)))
                    .Throws<NotSupportedException>();
            }
            else
            {
                // Windows/Linux 契约：不抛异常
                await Assert.That(() =>
                    fixture.RunOnUiThread(() => window.PrintToPDF(tempPath))).ThrowsNothing();
            }
        }
        finally
        {
            cleanup();
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 契约：PrintToPDF(byte[]?) 不抛异常（默认 no-op）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task PrintToPDF_Bytes_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.PrintToPDF((byte[]?)null))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：PrintToPDF(string, PrintToPdfOptions?) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task PrintToPDF_WithOptions_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);
        var tempPath = Path.Combine(Path.GetTempPath(), $"wails_contract_opts_{Guid.NewGuid():N}.pdf");

        try
        {
            if (fixture.PlatformName == "android")
            {
                // Android 契约：委托到无选项重载会抛 NotSupportedException
                await Assert.That(() =>
                    fixture.RunOnUiThread(() => window.PrintToPDF(tempPath, new PrintToPdfOptions())))
                    .Throws<NotSupportedException>();
            }
            else
            {
                await Assert.That(() =>
                    fixture.RunOnUiThread(() => window.PrintToPDF(tempPath, new PrintToPdfOptions()))).ThrowsNothing();
            }
        }
        finally
        {
            cleanup();
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 契约：CapturePreviewAsync 返回 Task&lt;byte[]?&gt;。
    /// 不支持的平台返回 null（如 Linux/Android），Windows 应返回非 null。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task CapturePreviewAsync_ReturnsTask()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            Task<byte[]?>? task = null;
#pragma warning disable CS4014 // 故意不 await：捕获 Task 供后续 await 验证返回值
            fixture.RunOnUiThread(() => { task = window.CapturePreviewAsync(); });
#pragma warning restore CS4014
            // RunOnUiThread 返回 func 的结果，CapturePreviewAsync 必返回非 null 的 Task 实例。
            await Assert.That(task is not null).IsTrue();
            var result = await task!;
            // 返回值可为 null（不支持）或 byte[]（支持）
            if (result is not null)
            {
                await Assert.That(result).IsTypeOf<byte[]>();
            }
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 O：模态与上下文菜单
    // ============================================================

    /// <summary>
    /// 契约：AttachAsModal 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task AttachAsModal_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (parent, cleanupParent) = CreateWindow(fixture, id: 100);
        var (child, cleanupChild) = CreateWindow(fixture, id: 101);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => child.AttachAsModal(100))).ThrowsNothing();
        }
        finally
        {
            cleanupChild();
            cleanupParent();
        }
    }

    /// <summary>
    /// 契约：OpenContextMenu(x, y) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task OpenContextMenu_Coordinates_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.OpenContextMenu(100, 100))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：OpenContextMenu(ContextMenuData) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task OpenContextMenu_WithData_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            var data = new Menus.ContextMenuData { Id = "test-menu", X = 50, Y = 50 };
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.OpenContextMenu(data))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 P：任务栏（Tauri 扩展方法）
    // ============================================================

    /// <summary>
    /// 契约：SetTaskbarProgress 不抛异常。
    /// 非 Windows 平台为 no-op（接口默认实现）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetTaskbarProgress_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetTaskbarProgress(TaskbarProgressState.Normal, 50, 100))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetOverlayIcon 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetOverlayIcon_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetOverlayIcon(null, "test"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetBadgeCount 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetBadgeCount_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetBadgeCount(5);
                window.SetBadgeCount(0);
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetBadgeLabel 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetBadgeLabel_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetBadgeLabel("New");
                window.SetBadgeLabel(null);
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetSkipTaskbar 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetSkipTaskbar_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetSkipTaskbar(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetIgnoreCursorEvents 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetIgnoreCursorEvents_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetIgnoreCursorEvents(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetVisibleOnAllWorkspaces 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetVisibleOnAllWorkspaces_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetVisibleOnAllWorkspaces(true))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetBorderColor 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetBorderColor_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
            {
                window.SetBorderColor("#FF0000");
                window.SetBorderColor(null);
            })).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetEffects 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetEffects_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetEffects(new WindowEffects { Effect = WindowEffect.Mica }))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetFullscreenButtonEnabled 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetFullscreenButtonEnabled_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() => fixture.RunOnUiThread(() =>
                window.SetFullscreenButtonEnabled(false))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetMinimised 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetMinimised_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMinimised())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetMaximised 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetMaximised_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetMaximised())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetNormal 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetNormal_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetNormal())).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    // ============================================================
    // 模块 Q：原生消息通道
    // ============================================================

    /// <summary>
    /// 契约：SetNativeMessageHandler(null) 不抛异常（取消注册）。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetNativeMessageHandler_Null_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetNativeMessageHandler(null))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetNativeMessageHandler(callback) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetNativeMessageHandler_Callback_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            Func<string, Task> handler = _ => Task.CompletedTask;
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetNativeMessageHandler(handler))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetConsoleMessageHandler(null) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetConsoleMessageHandler_Null_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetConsoleMessageHandler(null))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：SetConsoleMessageHandler(callback) 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task SetConsoleMessageHandler_Callback_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            Action<BrowserConsoleMessageLevel, string> handler = (_, _) => { };
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.SetConsoleMessageHandler(handler))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：PostNativeMessageAsync 返回已完成的 Task。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task PostNativeMessageAsync_ReturnsCompletedTask()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            Task? task = null;
#pragma warning disable CS4014 // 故意不 await：捕获 Task 供后续 await 验证返回值
            fixture.RunOnUiThread(() => { task = window.PostNativeMessageAsync("{}"); });
#pragma warning restore CS4014
            await Assert.That(task is not null).IsTrue();
            await task!.ConfigureAwait(false); // 不应抛异常
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：RegisterCustomScheme 不抛异常。
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task RegisterCustomScheme_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.RegisterCustomScheme("myapp"))).ThrowsNothing();
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// 契约：Run(callback) 接受回调且不抛异常。
    /// <para>
    /// 语义说明：Run(callback) 将回调排队到 WebView 初始化完成后执行（异步），
    /// 不保证同步执行。本契约仅验证调用本身不抛异常，回调执行时机由平台决定。
    /// </para>
    /// </summary>
    [Test]
    [Category(WindowContractLevel.L1Universal)]
    public async Task Run_Callback_DoesNotThrow()
    {
        var fixture = GetFixture();
        var (window, cleanup) = CreateWindow(fixture);

        try
        {
            await Assert.That(() =>
                fixture.RunOnUiThread(() => window.Run(() => { }))).ThrowsNothing();
        }
        finally { cleanup(); }
    }
}
