using Wails.Net.Application.Menus;
using IWebviewWindowImpl = Wails.Net.Application.Windows.IWebviewWindowImpl;

// 注意：命名空间刻意保持为 Wails.Net.Application.Tests（而非 .Menus）。
// 若引入 Wails.Net.Application.Tests.Menus，会遮蔽 Wails.Net.Application.Menus，
// 使同程序集中 `Menus.Menu` 形式的引用解析失败（CS0234）。
namespace Wails.Net.Application.Tests;

/// <summary>
/// 菜单模型与平台实现之间的挂载/转发契约测试。
/// <para>
/// 这批测试守护的是"<c>Menu.Append / Remove / Clear / Update</c> 是否真的到达平台"这条链路。
/// 在补齐 <see cref="Menu.Impl"/> 挂载点之前，这些方法全部是空操作：
/// 修改菜单对象模型后原生菜单毫无变化，只能整棵重建再 <c>SetApplicationMenu</c>。
/// </para>
/// <para>
/// 用假的 <see cref="IMenuImpl"/> 记录调用即可在无 GUI 环境下验证，不依赖任何平台。
/// </para>
/// </summary>
public sealed class MenuPlatformWiringTests
{
    [Test]
    public async Task Append_WithAttachedImpl_ForwardsToPlatform()
    {
        var menu = new Menu();
        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        var item = menu.AddMenuItem("打开");

        await Assert.That(impl.Added.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(impl.Added[0], item)).IsTrue();
        await Assert.That(impl.Positions[0]).IsEqualTo(0);
        await Assert.That(ReferenceEquals(item.Parent, menu)).IsTrue();
    }

    [Test]
    public async Task Append_WithoutImpl_OnlyUpdatesModel()
    {
        // 未挂载平台实现时（例如菜单只用于 JSON 传输），不得抛异常，也不能凭空产生副作用。
        var menu = new Menu();

        var item = menu.AddMenuItem("打开");

        await Assert.That(menu.Items.Count).IsEqualTo(1);
        await Assert.That(item.Parent is not null).IsTrue();
        await Assert.That(item.OwningImpl is null).IsTrue();
    }

    [Test]
    public async Task AttachImpl_SetsParentChainForWholeTree()
    {
        var menu = new Menu();
        var file = menu.AddSubmenu("文件");
        var recent = file.AddSubmenu("最近");
        var open = recent.AddMenuItem("打开");

        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        // 反序列化得到的菜单树不会经过 Append，父指针必须在挂载时补齐，
        // 否则深层菜单项无法通过 OwningImpl 找到容器实现。
        await Assert.That(ReferenceEquals(file.Parent, menu)).IsTrue();
        await Assert.That(ReferenceEquals(recent.Parent, file)).IsTrue();
        await Assert.That(ReferenceEquals(open.Parent, recent)).IsTrue();
        await Assert.That(ReferenceEquals(open.OwningImpl, impl)).IsTrue();
    }

    [Test]
    public async Task Update_RecursesIntoSubmenusHoldingTheirOwnImpl()
    {
        var menu = new Menu();
        var file = menu.AddSubmenu("文件");
        var open = file.AddMenuItem("打开");

        var rootImpl = new RecordingMenuImpl();
        menu.AttachImpl(rootImpl);

        var fileImpl = new RecordingMenuImpl();
        file.AttachImpl(fileImpl);

        menu.Update();

        // 根实现收到直接子项（子菜单本身）的更新。
        await Assert.That(rootImpl.Updated.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(rootImpl.Updated[0], file)).IsTrue();

        // 子菜单自持实现时，其子项由该实现更新（Windows 的每个 popup 都有自己的 Win32Menu）。
        await Assert.That(fileImpl.Updated.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(fileImpl.Updated[0], open)).IsTrue();
    }

    [Test]
    public async Task Update_WithoutChildImpl_DoesNotRecurse()
    {
        var menu = new Menu();
        var file = menu.AddSubmenu("文件");
        file.AddMenuItem("打开");

        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        menu.Update();

        // 子菜单没有独立实现（macOS/Linux 走整树重建）时，根实现已覆盖全部后代，
        // 递归只会造成重复重建。
        await Assert.That(impl.Updated.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(impl.Updated[0], file)).IsTrue();
    }

    [Test]
    public async Task Remove_ForwardsToPlatformAndClearsParent()
    {
        var menu = new Menu();
        var open = menu.AddMenuItem("打开");
        var close = menu.AddMenuItem("关闭");

        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        menu.Remove(open);

        await Assert.That(menu.Items.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(menu.Items[0], close)).IsTrue();
        await Assert.That(impl.Removed.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(impl.Removed[0], open)).IsTrue();
        await Assert.That(open.Parent is null).IsTrue();
    }

    [Test]
    public async Task Remove_UnknownItem_DoesNotTouchPlatform()
    {
        // 移除不在集合中的项不应下发到平台，否则原生菜单会误删同 ID 的项。
        var menu = new Menu();
        menu.AddMenuItem("打开");

        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        menu.Remove(new MenuItem("不存在"));

        await Assert.That(impl.Removed.Count).IsEqualTo(0);
        await Assert.That(menu.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Clear_RemovesEveryItemFromPlatform()
    {
        var menu = new Menu();
        menu.AddMenuItem("打开");
        menu.AddMenuItem("关闭");

        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        menu.Clear();

        await Assert.That(impl.Removed.Count).IsEqualTo(2);
        await Assert.That(menu.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OwningImpl_StopsAtNearestAttachedAncestor()
    {
        var menu = new Menu();
        var file = menu.AddSubmenu("文件");
        var open = file.AddMenuItem("打开");

        var rootImpl = new RecordingMenuImpl();
        menu.AttachImpl(rootImpl);

        // 叶子项与中间子菜单都没有自己的实现，应上溯到根。
        await Assert.That(ReferenceEquals(open.OwningImpl, rootImpl)).IsTrue();

        var fileImpl = new RecordingMenuImpl();
        file.AttachImpl(fileImpl);

        // 中间子菜单挂载实现后，其子项应就近解析到它。
        await Assert.That(ReferenceEquals(open.OwningImpl, fileImpl)).IsTrue();
    }

    [Test]
    public async Task OwningImpl_RoutesItemUpdateToContainer()
    {
        // 模拟 menu.updateMenuItem 命令的行为：改完模型后把刷新请求交给容器实现。
        var menu = new Menu();
        var item = menu.AddMenuItem("打开");

        var impl = new RecordingMenuImpl();
        menu.AttachImpl(impl);

        item.Label = "打开文件";
        item.IsDisabled = true;
        item.OwningImpl?.UpdateMenuItem(item);

        await Assert.That(impl.Updated.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(impl.Updated[0], item)).IsTrue();
    }

    /// <summary>
    /// 记录调用序列的假平台实现。
    /// </summary>
    private sealed class RecordingMenuImpl : IMenuImpl
    {
        /// <summary>已追加的菜单项。</summary>
        public List<MenuItem> Added { get; } = new();

        /// <summary>追加时的位置参数。</summary>
        public List<int> Positions { get; } = new();

        /// <summary>已移除的菜单项。</summary>
        public List<MenuItem> Removed { get; } = new();

        /// <summary>已更新（通知刷新）的菜单项。</summary>
        public List<MenuItem> Updated { get; } = new();

        /// <inheritdoc />
        public void Show()
        {
        }

        /// <inheritdoc />
        public void Hide()
        {
        }

        /// <inheritdoc />
        public void AddMenuItem(MenuItem item, int position)
        {
            Added.Add(item);
            Positions.Add(position);
        }

        /// <inheritdoc />
        public void RemoveMenuItem(MenuItem item) => Removed.Add(item);

        /// <inheritdoc />
        public void UpdateMenuItem(MenuItem item) => Updated.Add(item);

        /// <inheritdoc />
        public void AddSubmenu(Menu submenu, int position)
        {
        }

        /// <inheritdoc />
        public void Destroy()
        {
        }

        /// <inheritdoc />
        public void SetLabel(string label)
        {
        }

        /// <inheritdoc />
        public void SetEnabled(bool enabled)
        {
        }

        /// <inheritdoc />
        public void SetChecked(bool @checked)
        {
        }

        /// <inheritdoc />
        public void SetAccelerator(string accelerator)
        {
        }

        /// <inheritdoc />
        public void SetBitmap(byte[]? bitmap)
        {
        }

        /// <inheritdoc />
        public void ApplyRole(MenuItem item, IWebviewWindowImpl? window)
        {
        }
    }
}
