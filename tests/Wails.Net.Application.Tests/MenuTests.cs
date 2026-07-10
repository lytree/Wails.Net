using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Tests;

/// <summary>
/// Menu 和 MenuItem 的单元测试（TUnit）。
/// </summary>
public sealed class MenuTests
{
    [Test]
    public async Task Constructor_Default_CreatesEmptyMenu()
    {
        // 安排与操作
        var menu = new Menu();

        // 断言
        await Assert.That(menu.Items).IsNotNull();
        await Assert.That(menu.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_WithLabel_SetsLabel()
    {
        // 安排与操作
        var menu = new Menu("File");

        // 断言
        await Assert.That(menu.Label).IsEqualTo("File");
    }

    [Test]
    public async Task AddMenuItem_AddsItemToCollection()
    {
        // 安排
        var menu = new Menu();

        // 操作
        var item = menu.AddMenuItem("Open");

        // 断言
        await Assert.That(menu.Items.Count).IsEqualTo(1);
        await Assert.That(item.Label).IsEqualTo("Open");
        await Assert.That(menu.Items[0]).IsSameReferenceAs(item);
    }

    [Test]
    public async Task AddCheckboxMenuItem_SetsCheckboxProperties()
    {
        // 安排
        var menu = new Menu();

        // 操作
        var item = menu.AddCheckboxMenuItem("Auto-save", true);

        // 断言
        await Assert.That(item.IsCheckbox).IsTrue();
        await Assert.That(item.Checked).IsTrue();
        await Assert.That(item.Label).IsEqualTo("Auto-save");
    }

    [Test]
    public async Task AddSubmenu_ReturnsMenuWithIsSubMenuTrue()
    {
        // 安排
        var menu = new Menu();

        // 操作
        var submenu = menu.AddSubmenu("Recent");

        // 断言
        await Assert.That(submenu.IsSubMenu).IsTrue();
        await Assert.That(submenu.Label).IsEqualTo("Recent");
    }

    [Test]
    public async Task AddSeparator_AddsSeparatorItem()
    {
        // 安排
        var menu = new Menu();

        // 操作
        menu.AddSeparator();

        // 断言
        await Assert.That(menu.Items.Count).IsEqualTo(1);
        await Assert.That(menu.Items[0].IsSeparator).IsTrue();
    }

    [Test]
    public async Task Clear_RemovesAllItems()
    {
        // 安排
        var menu = new Menu();
        menu.AddMenuItem("Open");
        menu.AddMenuItem("Close");
        await Assert.That(menu.Items.Count).IsEqualTo(2);

        // 操作
        menu.Clear();

        // 断言
        await Assert.That(menu.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GenerateID_ReturnsUniqueIDs()
    {
        // 操作
        var id1 = MenuItem.GenerateID();
        var id2 = MenuItem.GenerateID();

        // 断言
        await Assert.That(id1).IsNotEqualTo(id2);
    }
}
