using System.Text.Json;
using Wails.Net.Application.Menus;

// 注意：命名空间刻意保持为 Wails.Net.Application.Tests（而非 .Menus）。
// 若引入 Wails.Net.Application.Tests.Menus，会遮蔽 Wails.Net.Application.Menus，
// 使同程序集中 `Menus.Menu` 形式的引用解析失败（CS0234）。
namespace Wails.Net.Application.Tests;

/// <summary>
/// 菜单 JSON 契约测试。
/// <para>
/// 菜单是少数需要经 IPC 往返的对象模型：前端通过 <c>menu.setApplicationMenu</c> 下发菜单，
/// 通过 <c>menu.getApplicationMenu</c> 回读菜单。此处锁定三条契约：
/// <list type="number">
/// <item><see cref="ContextMenu"/> 的派生类型鉴别符会被写出/还原（否则上下文菜单退化为普通菜单）；</item>
/// <item>缺少鉴别符时回退到基类 <see cref="Menu"/>（兼容前端手写 JSON）；</item>
/// <item>含点击回调的菜单必须可被序列化——否则真实应用（菜单项都带回调）回读时直接抛异常。</item>
/// </list>
/// </para>
/// </summary>
public sealed class MenuJsonTests
{
    /// <summary>
    /// 与命令层一致的 Web 默认序列化选项。
    /// </summary>
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Serialize_ContextMenu_WritesDerivedTypeDiscriminator()
    {
        var json = JsonSerializer.Serialize<Menu>(new ContextMenu { Label = "Edit" }, _options);

        await Assert.That(json).Contains("contextMenu");
    }

    [Test]
    public async Task Deserialize_ContextMenuJson_RestoresDerivedType()
    {
        var json = JsonSerializer.Serialize<Menu>(new ContextMenu { Label = "Edit" }, _options);

        var restored = JsonSerializer.Deserialize<Menu>(json, _options);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored).IsTypeOf<ContextMenu>();
        await Assert.That(restored!.Label).IsEqualTo("Edit");
    }

    [Test]
    public async Task Deserialize_MenuJsonWithoutDiscriminator_FallsBackToBaseMenu()
    {
        // 前端可能直接下发不带 $type 的普通菜单 JSON。
        const string json = """{"label":"File"}""";

        var restored = JsonSerializer.Deserialize<Menu>(json, _options);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.GetType()).IsEqualTo(typeof(Menu));
    }

    [Test]
    public async Task RoundTrip_TreeWithSubmenuAndFlags_PreservesStructure()
    {
        var menu = new Menu("App");
        var file = menu.AddSubmenu("文件");
        file.AddMenuItem("打开");
        file.AddSeparator();
        file.AddCheckboxMenuItem("只读", true);
        var role = file.AddRoleItem(MenuRole.Quit, "退出应用");
        role.Accelerator = "Ctrl+Q";

        var json = JsonSerializer.Serialize(menu, _options);
        var restored = JsonSerializer.Deserialize<Menu>(json, _options);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Label).IsEqualTo("App");
        await Assert.That(restored.Items.Count).IsEqualTo(1);

        var restoredFile = restored.Items[0];
        await Assert.That(restoredFile.Label).IsEqualTo("文件");
        await Assert.That(restoredFile.IsSubMenu).IsTrue();
        await Assert.That(restoredFile.Items.Count).IsEqualTo(4);

        await Assert.That(restoredFile.Items[0].Label).IsEqualTo("打开");
        await Assert.That(restoredFile.Items[1].IsSeparator).IsTrue();

        var checkbox = restoredFile.Items[2];
        await Assert.That(checkbox.IsCheckbox).IsTrue();
        await Assert.That(checkbox.Checked).IsTrue();

        var restoredRole = restoredFile.Items[3];
        await Assert.That(restoredRole.Role).IsEqualTo(MenuRole.Quit);
        await Assert.That(restoredRole.Label).IsEqualTo("退出应用");
        await Assert.That(restoredRole.Accelerator).IsEqualTo("Ctrl+Q");
    }

    [Test]
    public async Task Serialize_MenuWithClickCallbacks_DoesNotThrow()
    {
        // 真实应用的菜单项都带回调；menu.getApplicationMenu 需要能把这种菜单原样序列化回去。
        // 回调是运行时委托，不属于可序列化契约，应被忽略而不是抛 NotSupportedException。
        var menu = new Menu("App");
        menu.AddMenuItem("新建", () => { });
        menu.AddMenuItem("打开");
        menu.Items[1].CallbackWithContext = _ => { };

        Exception? captured = null;
        try
        {
            _ = JsonSerializer.Serialize(menu, _options);
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        await Assert.That(captured).IsNull();
    }

    [Test]
    public async Task Deserialize_MenuWithItems_DoesNotPopulateCallbacksOrImpl()
    {
        var menu = new Menu("App");
        menu.AddMenuItem("新建", () => { });

        var json = JsonSerializer.Serialize(menu, _options);
        var restored = JsonSerializer.Deserialize<Menu>(json, _options);

        await Assert.That(restored).IsNotNull();
        // 前端下发的菜单没有后端委托，反序列化后回调必须为空，由平台按 Role/ID 重新绑定。
        await Assert.That(restored!.Items[0].Callback is null).IsTrue();
        await Assert.That(restored.Items[0].CallbackWithContext is null).IsTrue();
        await Assert.That(restored.Items[0].Impl is null).IsTrue();
    }
}
