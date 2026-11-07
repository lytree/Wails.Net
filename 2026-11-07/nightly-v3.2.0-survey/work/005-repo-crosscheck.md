# Wails.Net 现状逐项交叉核对（只读，未改任何源码）

核对范围：`src/`、`tests/` 中与四个上游 PR 对应的实现点。

## 1. PR #6502 Windows 应用级 SetIcon

| 上游行为 | Wails.Net 现状 | 结论 |
|----------|----------------|------|
| `setIcon` 应用到全部已创建窗口 | [WindowsPlatformApp.cs:405-415](../../../src/Wails.Net.Application.Windows/WindowsPlatformApp.cs#L405-L415) → `ApplyIconToWindows()`（`:978-995`） | 等价 |
| 保留最后一次图标，新建窗口自动套用 | `_appIconBytes`（`:147`、`:407`）在 `CreateWebviewWindow` 中应用（`:512-520`） | 等价（字节缓存而非 HICON 缓存） |
| `DestroyIcon(previousIcon)` / `destroy()` 释放图标 | `LoadIconFromBytes`（`:934-953`）每次写临时 .ico 后 `LoadImage`，全仓无 `DestroyIcon` | **差异（P2，资源句柄）** |
| `DisableIcon` 时临时清 `WS_SYSMENU` 规避标题栏图标 | 全仓无 `DisableIcon`（`WindowsOptions` 仅 WebviewUserDataPath / WebviewBrowserPath / AdditionalBrowserArgs / EnabledFeatures / DisabledFeatures / WndClass） | **不适用**（本移植无该选项，属既有功能差距，非本 PR 引入） |
| 图标应用在 `InvokeAsync`（主线程） | `SetIcon` 直接同步执行，经 `SendMessage(WM_SETICON)` | 等价（行为无差，时序不同） |
| 新增窗口在 `run()` 内套用 | 新建窗口在 `CreateWebviewWindow` 内、`Show()` 之前套用 | 等价 |

Wails.Net 已有测试：`WindowsPlatformAppTests.SetIcon_DoesNotThrow_WithNullIcon`（仅 null 路径）。

## 2. PR #6314 Linux 托盘随 Menu.Update 刷新

| 上游行为 | Wails.Net 现状 | 结论 |
|----------|----------------|------|
| 托盘 dbusmenu 布局在 `setMenu` 时一次性构建，`Menu.Update` 后需重建 | [LinuxSystemTray.cs:297-301](../../../src/Wails.Net.Application.Linux/LinuxSystemTray.cs#L297-L301) 仅持有 `LinuxContextMenu`；[LinuxContextMenu.cs:49-79](../../../src/Wails.Net.Application.Linux/LinuxContextMenu.cs#L49-L79) 在**每次弹出**时从源 `Menu.Items` 重建 GMenu 模型与 action group（`:65`→`BuildMenuModel`） | **已具备（架构差异）**：不存在“显示首个菜单直到重启”的陈旧问题 |
| gtk3 下被移除项 `gtkSignalToMenuItem` 条目泄漏 | Wails.Net 仅 GTK4（GirCore），[LinuxMenu.cs](../../../src/Wails.Net.Application.Linux/LinuxMenu.cs) 每次 `Add/Remove/Update` 整体重建 `_actions`/`_actionGroup`，无按 ID 保留的原生项映射 | 不适用 |
| 子菜单 `Update` 不刷新托盘；同一 Menu 被托盘与原生菜单共享时 impl 单槽冲突 | Wails.Net 托盘不占用 `MenuItem.Impl`（`LinuxContextMenu` 直接读 `Menu.Items`） | 不适用 |

## 3. PR #6077 AndroidOptions 字段注释

- 上游仅改 Go 注释（字段名本就是 `DisableOverscroll`）。
- Wails.Net `src/Wails.Net.Application/Options/` 只有 `WindowsOptions` / `LinuxOptions` / `MacOptions` / `WebviewWindowMacOptions` / `NotchWindowOptions` 等，**不存在 `AndroidOptions` 类型**，也无 `DisableScroll` / `DisableOverscroll` / `EnableZoom` 字段。

## 4. PR #6348 WebView2 生命周期测试（checkptr）

- 属 Go `internal/webview2` 的测试代码（`uintptr`→Go 指针转换）。
- Wails.Net 通过 WebView2 托管 SDK（Microsoft.Web.WebView2）调用，无自维护 COM vtable / `uintptr` 句柄模型，无对应测试。

## 5. 版本与文档面变更

- `v3/internal/version/version.txt`、`v3/internal/runtime/desktop/@wailsio/runtime/package.json`：nightly 版本号 bump（`3.2.0-nightly.20261103`），属上游发布流水线。
- `docs/mpress/**`、`website/**`、sponsors/contributors 图、showcase（Culma / Larvyn）、发布影片：上游站点与营销物料，与本移植无关。
