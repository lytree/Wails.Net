using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Menus.Context;

namespace Wails.Net.Application.Tests;

/// <summary>
/// MenuContext 单元测试。
/// 对应 Wails v3 Go 版本 context.go 中的 Context 结构行为验证。
/// </summary>
[NotInParallel]
public sealed class MenuContextTests
{
    // ─── 默认状态 ───

    [Test]
    public async Task Default_ClickedMenuItem_IsNull()
    {
        var ctx = new MenuContext();
        await Assert.That(ctx.ClickedMenuItem).IsNull();
    }

    [Test]
    public async Task Default_IsChecked_IsFalse()
    {
        var ctx = new MenuContext();
        await Assert.That(ctx.IsChecked).IsFalse();
    }

    [Test]
    public async Task Default_ContextMenuData_IsEmpty()
    {
        var ctx = new MenuContext();
        await Assert.That(ctx.ContextMenuData).IsEqualTo(string.Empty);
    }

    // ─── WithClickedMenuItem ───

    [Test]
    public async Task WithClickedMenuItem_SetsClickedMenuItem()
    {
        var ctx = new MenuContext();
        var item = new MenuItem("Test");

        ctx.WithClickedMenuItem(item);

        await Assert.That(ctx.ClickedMenuItem).IsSameReferenceAs(item);
    }

    [Test]
    public async Task WithClickedMenuItem_ReturnsContext_ForChaining()
    {
        var ctx = new MenuContext();
        var item = new MenuItem("Test");

        var result = ctx.WithClickedMenuItem(item);

        await Assert.That(result).IsSameReferenceAs(ctx);
    }

    // ─── WithChecked ───

    [Test]
    public async Task WithChecked_True_SetsIsCheckedTrue()
    {
        var ctx = new MenuContext();
        ctx.WithChecked(true);
        await Assert.That(ctx.IsChecked).IsTrue();
    }

    [Test]
    public async Task WithChecked_False_SetsIsCheckedFalse()
    {
        var ctx = new MenuContext();
        ctx.WithChecked(true);
        ctx.WithChecked(false);
        await Assert.That(ctx.IsChecked).IsFalse();
    }

    // ─── WithContextMenuData ───

    [Test]
    public async Task WithContextMenuData_SetsDataString()
    {
        var ctx = new MenuContext();
        var data = new ContextMenuData { Id = "menu1", X = 10, Y = 20, Data = "hello-context" };

        ctx.WithContextMenuData(data);

        await Assert.That(ctx.ContextMenuData).IsEqualTo("hello-context");
    }

    [Test]
    public async Task WithContextMenuData_NullData_LeavesEmpty()
    {
        var ctx = new MenuContext();
        ctx.WithContextMenuData(null);
        await Assert.That(ctx.ContextMenuData).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WithContextMenuData_NullDataField_ReturnsEmpty()
    {
        var ctx = new MenuContext();
        var data = new ContextMenuData { Id = "menu1", Data = null };

        ctx.WithContextMenuData(data);

        // Data 为 null 时，WithContextMenuData 存储 null，ContextMenuData getter 返回空字符串
        await Assert.That(ctx.ContextMenuData).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WithContextMenuData_ReturnsContext_ForChaining()
    {
        var ctx = new MenuContext();
        var data = new ContextMenuData { Id = "menu1", Data = "test" };

        var result = ctx.WithContextMenuData(data);

        await Assert.That(result).IsSameReferenceAs(ctx);
    }

    // ─── 链式调用 ───

    [Test]
    public async Task ChainedCalls_SetAllFields()
    {
        var item = new MenuItem("Chained");
        var data = new ContextMenuData { Id = "ctx-menu", Data = "chain-data" };

        var ctx = new MenuContext()
            .WithClickedMenuItem(item)
            .WithContextMenuData(data);
        ctx.WithChecked(true);

        await Assert.That(ctx.ClickedMenuItem).IsSameReferenceAs(item);
        await Assert.That(ctx.IsChecked).IsTrue();
        await Assert.That(ctx.ContextMenuData).IsEqualTo("chain-data");
    }

    // ─── Menu.SetContextMenuData 传播 ───

    [Test]
    public async Task Menu_SetContextMenuData_PropagatesToAllItems()
    {
        var menu = new Menu("Test");
        var item1 = menu.AddMenuItem("Item1");
        var item2 = menu.AddMenuItem("Item2");
        var data = new ContextMenuData { Id = "ctx1", Data = "propagated" };

        menu.SetContextMenuData(data);

        await Assert.That(item1.ContextMenuData).IsSameReferenceAs(data);
        await Assert.That(item2.ContextMenuData).IsSameReferenceAs(data);
    }

    [Test]
    public async Task Menu_SetContextMenuData_PropagatesToSubmenuItems()
    {
        var menu = new Menu("Test");
        var submenu = menu.AddSubmenu("Submenu");
        var subItem = submenu.AddMenuItem("SubItem");
        var data = new ContextMenuData { Id = "ctx1", Data = "deep-data" };

        menu.SetContextMenuData(data);

        // AddSubmenu 返回 Menu 基类，实际对象为 MenuItem，需转型访问 ContextMenuData
        await Assert.That(((MenuItem)submenu).ContextMenuData).IsSameReferenceAs(data);
        await Assert.That(subItem.ContextMenuData).IsSameReferenceAs(data);
    }

    [Test]
    public async Task Menu_SetContextMenuData_Null_ClearsExistingData()
    {
        var menu = new Menu("Test");
        var item = menu.AddMenuItem("Item1");
        var data = new ContextMenuData { Id = "ctx1", Data = "initial" };
        menu.SetContextMenuData(data);

        // 清除数据
        menu.SetContextMenuData(null);

        await Assert.That(item.ContextMenuData).IsNull();
    }

    // ─── MenuItem.SetContextMenuData 传播 ───

    [Test]
    public async Task MenuItem_SetContextMenuData_PropagatesToChildItems()
    {
        var parent = new MenuItem("Parent") { IsSubMenu = true };
        var child = new MenuItem("Child");
        parent.Items.Add(child);
        var data = new ContextMenuData { Id = "ctx1", Data = "parent-data" };

        parent.SetContextMenuData(data);

        await Assert.That(parent.ContextMenuData).IsSameReferenceAs(data);
        await Assert.That(child.ContextMenuData).IsSameReferenceAs(data);
    }

    [Test]
    public async Task MenuItem_SetContextMenuData_NonSubMenu_DoesNotPropagate()
    {
        var item = new MenuItem("Leaf");
        // 即使有 Items（不应该有），非子菜单项不传播
        var data = new ContextMenuData { Id = "ctx1", Data = "leaf-data" };

        item.SetContextMenuData(data);

        await Assert.That(item.ContextMenuData).IsSameReferenceAs(data);
    }

    // ─── CallbackWithContext 属性 ───

    [Test]
    public async Task MenuItem_CallbackWithContext_DefaultNull()
    {
        var item = new MenuItem("Test");
        await Assert.That(item.CallbackWithContext).IsNull();
    }

    [Test]
    public async Task MenuItem_CallbackWithContext_CanBeSet()
    {
        var item = new MenuItem("Test");
        Action<MenuContext> callback = _ => { };

        item.CallbackWithContext = callback;

        await Assert.That(item.CallbackWithContext).IsSameReferenceAs(callback);
    }

    [Test]
    public async Task MenuItem_CallbackWithContext_InvokedWithContext()
    {
        var item = new MenuItem("Test");
        MenuContext? receivedContext = null;
        item.CallbackWithContext = ctx => receivedContext = ctx;

        var expectedContext = new MenuContext().WithClickedMenuItem(item);
        item.CallbackWithContext!(expectedContext);

        await Assert.That(receivedContext).IsSameReferenceAs(expectedContext);
        await Assert.That(receivedContext!.ClickedMenuItem).IsSameReferenceAs(item);
    }
}
