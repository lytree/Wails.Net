using TUnit.Core;
using Wails.Net.Runtime.Js;

namespace Wails.Net.Application.Tests.RuntimeJs;

/// <summary>
/// <see cref="RuntimeGenerator.GenerateContextMenuHook"/> 的单元测试（P1-4 Step 4）。
/// 验证自动生成的前端 contextmenu 事件钩子在不同 RuntimeOptions 下的关键行为：
/// <list type="bullet">
/// <item>IsDebug 标志被正确注入到 JS 代码中。</item>
/// <item>钩子注册了 'contextmenu' 事件监听器。</item>
/// <item>幂等标记 __wailsContextMenuHooked 防止重复注册。</item>
/// <item>读取 CSS 变量 --custom-contextmenu 和 --custom-contextmenu-data 的逻辑存在。</item>
/// <item>调用 _wailsInvoke("contextmenu", ...) 时携带正确的载荷字段。</item>
/// <item>支持 --default-contextmenu 策略（show/hide）。</item>
/// <item>Debug 构建下始终放行默认菜单。</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class RuntimeGeneratorContextMenuHookTests
{
    /// <summary>
    /// GenerateContextMenuHook 接受 null options 应抛出 ArgumentNullException。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_NullOptions_ThrowsArgumentNullException()
    {
        await Assert.That(() => RuntimeGenerator.GenerateContextMenuHook(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    /// <summary>
    /// Release 构建（IsDebug=false）应注入 isDebug = false。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_ReleaseBuild_InjectsIsDebugFalse()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("var isDebug = false;");
    }

    /// <summary>
    /// Debug 构建应注入 isDebug = true，processDefaultContextMenu 在 isDebug=true 时立即 return。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_DebugBuild_InjectsIsDebugTrue()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows", IsDebug = true };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("var isDebug = true;");
    }

    /// <summary>
    /// 钩子应注册 'contextmenu' 事件监听器。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_RegistersContextMenuEventListener()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("addEventListener('contextmenu'");
    }

    /// <summary>
    /// 钩子应包含幂等标记，防止重复注册。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_IncludesIdempotencyGuard()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("__wailsContextMenuHooked");
    }

    /// <summary>
    /// 钩子应读取 --custom-contextmenu CSS 变量。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_ReadsCustomContextMenuCssVariable()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("--custom-contextmenu");
    }

    /// <summary>
    /// 钩子应读取 --custom-contextmenu-data CSS 变量。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_ReadsCustomContextMenuDataCssVariable()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("--custom-contextmenu-data");
    }

    /// <summary>
    /// 钩子应支持 --default-contextmenu CSS 变量（show/hide 策略）。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_ReadsDefaultContextMenuPolicy()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("--default-contextmenu");
        await Assert.That(js).Contains("case 'show':");
        await Assert.That(js).Contains("case 'hide':");
    }

    /// <summary>
    /// 钩子应通过 _wailsInvoke("contextmenu", ...) 调用后端，载荷包含 id/x/y/data 字段。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_CallsWailsInvokeWithContextMenuPayload()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("_wailsInvoke(\"contextmenu\"");
        await Assert.That(js).Contains("id: id");
        await Assert.That(js).Contains("x: x");
        await Assert.That(js).Contains("y: y");
        await Assert.That(js).Contains("data: data");
    }

    /// <summary>
    /// 钩子应使用 event.clientX/clientY 作为视口坐标（clientX/clientY）。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_UsesClientXYAsViewportCoordinates()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("event.clientX");
        await Assert.That(js).Contains("event.clientY");
    }

    /// <summary>
    /// 钩子应在自定义菜单命中时调用 event.preventDefault()。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_CallsPreventDefaultOnCustomMenuHit()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言：在命中自定义菜单的分支中应调用 preventDefault
        var idx = js.IndexOf("if (customContextMenu)", StringComparison.Ordinal);
        await Assert.That(idx).IsGreaterThan(-1);
        var afterBranch = js.AsSpan(idx).ToString();
        await Assert.That(afterBranch).Contains("event.preventDefault()");
    }

    /// <summary>
    /// 钩子应考虑 contentEditable 元素保留默认菜单。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_HandlesContentEditable()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("isContentEditable");
    }

    /// <summary>
    /// 钩子应考虑选中文本（getSelection）时保留默认菜单。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_HandlesTextSelection()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("getSelection");
    }

    /// <summary>
    /// 钩子应考虑 input/textarea 元素时保留默认菜单。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_HandlesInputAndTextarea()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows" };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言
        await Assert.That(js).Contains("HTMLInputElement");
        await Assert.That(js).Contains("HTMLTextAreaElement");
    }

    /// <summary>
    /// Generate 方法应包含 contextmenu 钩子的关键标志（与 GenerateContextMenuHook 单独调用一致）。
    /// </summary>
    [Test]
    public async Task Generate_IncludesContextMenuHookInFullRuntime()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false };

        // 操作
        var full = RuntimeGenerator.Generate(options);

        // 断言：全量运行时包含钩子的关键标志
        await Assert.That(full).Contains("__wailsContextMenuHooked");
        await Assert.That(full).Contains("addEventListener('contextmenu'");
        await Assert.That(full).Contains("--custom-contextmenu");
        await Assert.That(full).Contains("--default-contextmenu");
    }

    /// <summary>
    /// Server 模式下仍应生成钩子代码（前端 Webview 仍可使用）。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_ServerMode_StillGeneratesHook()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "server", IsServerMode = true };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言：钩子代码本身不区分 Server/桌面模式，都生成
        await Assert.That(js).Contains("__wailsContextMenuHooked");
        await Assert.That(js).Contains("addEventListener('contextmenu'");
    }

    /// <summary>
    /// 平台标识不应影响钩子代码内容（钩子是平台无关的纯前端 JS）。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_DifferentPlatforms_ProducesSameHook()
    {
        // 安排
        var winOptions = new RuntimeOptions { Platform = "windows" };
        var linuxOptions = new RuntimeOptions { Platform = "linux" };

        // 操作
        var winJs = RuntimeGenerator.GenerateContextMenuHook(winOptions);
        var linuxJs = RuntimeGenerator.GenerateContextMenuHook(linuxOptions);

        // 断言：平台标识不出现 in 钩子代码中（钩子是平台无关的）
        await Assert.That(winJs).IsEqualTo(linuxJs);
    }

    /// <summary>
    /// Debug 构建下 processDefaultContextMenu 在策略检查之前立即 return，
    /// 即不读取 --default-contextmenu 变量。
    /// </summary>
    [Test]
    public async Task GenerateContextMenuHook_DebugBuild_SkipsDefaultPolicyCheck()
    {
        // 安排
        var options = new RuntimeOptions { Platform = "windows", IsDebug = true };

        // 操作
        var js = RuntimeGenerator.GenerateContextMenuHook(options);

        // 断言：Debug 分支应在读取 --default-contextmenu 之前 return
        var debugReturnIdx = js.IndexOf("if (isDebug)", StringComparison.Ordinal);
        var policyReadIdx = js.IndexOf("--default-contextmenu", StringComparison.Ordinal);
        await Assert.That(debugReturnIdx).IsGreaterThan(-1);
        await Assert.That(policyReadIdx).IsGreaterThan(-1);
        await Assert.That(debugReturnIdx).IsLessThan(policyReadIdx);
    }
}
