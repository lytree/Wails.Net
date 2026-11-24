# macOS 平台实现 × Wails v3 差异审计（2026-11-17）

> **对照基线**：Wails v3 `v3.1.0`（2026-11-01 weekly，见 [基线复核 §14](wails-v3-beta-comparison.md)）
> **对照对象**：上游 `v3/pkg/application/*darwin*`（`webview_window_darwin.go|.m`、`application_darwin.go`、`menu_darwin.go`、`systemtray_darwin.go`、`dialogs_darwin.go`、`global_shortcut_darwin.go` 等，共 18k 行）↔ 本仓库 `src/Wails.Net.Application.MacOS/`（13 文件 / 9,862 行）
> **取证方式**：上游 `v3.1.0` 源码树逐文件比对 + 本仓库代码静态检查（`#if MACOS` 分支）
> **重要前提**：本仓库 CI **无 macOS 作业**（`.github/workflows/ci.yml` 仅 Linux/Windows），Windows 主机不编译 `#if MACOS` 分支。本审计全部结论来自**静态对照，未经 macOS 实机编译/运行验证**；结论中的"未实现"指代码中不存在对应路径，不等于"一定报错"。

---

## 1. 结论摘要

**仍存在差异，且不是零星细节**。macOS 实现（2026-11-13/14 落地，`937472f` 等）已把**窗口/菜单/对话框/剪贴板/notch/Dock 进度**这条主线做到与上游同构，但**应用生命周期层（NSApplicationDelegate）与若干 Wails v3.1 新增能力（按钮状态、TabbingMode、Liquid Glass、托盘锚定窗口、全局热键）尚未跟进**。

| 分类 | 数量 | 说明 |
|------|------|------|
| P1 用户可见功能缺失/行为错误 | 8 | 置顶失效、关闭事件不可取消、Dock 点击不恢复、文件上传无效、进程回收不恢复、缩放读值恒 1.0、托盘窗口全缺、窗口级快捷键不生效 |
| P2 选项/能力未落地 | 13 | 按钮状态、TabbingMode、Liquid Glass、工具栏、隐形标题栏、窗口级圆角、窗口级 WebviewPreferences、自定义 scheme、菜单分节、托盘菜单回调、全局热键、SMAppService、若干 getter 默认实现 |
| 非差异（上游 macOS 同样未实现） | 3 | `window.open`、`setIgnoreMouseEvents`、剪贴板 HTML/图片/文件（我方反而超出） |

---

## 2. P1：用户可见功能缺失或行为错误

| # | 差异 | 上游依据（`v3.1.0`） | 我方现状 | 影响 |
|---|------|----------------------|----------|------|
| 1 | **`AlwaysOnTop` 初始值未生效** | `webview_window_options.go` `effectiveMacWindowLevel()`：`AlwaysOnTop` ⇒ `MacWindowLevelFloating`，在 `run()` 里一次性解析 | `MacOSWebviewWindow.cs:805` 只在显式调用 `SetAlwaysOnTop` 时设 `Level`；全文件无 `_options.AlwaysOnTop` 读取 | 以 `AlwaysOnTop=true` 创建的窗口**不置顶**（Windows/Linux 均正确，`LinuxWebviewWindow.cs:365`） |
| 2 | **关闭不可取消 / `WindowClosing` 从不派发** | `webview_window_darwin.m:479` `windowShouldClose:` → 发 `EventWindowShouldClose` 并返回 `false`；`webview_window_close_darwin.go` 用 `windowIsHidden` / `windowShouldUnconditionallyClose` 区分"隐藏窗口不触发关闭事件"（#4389）与 `Close()` 强制关闭 | `MacOSWebviewWindow.cs:1570` 只订阅 `WillClose` → 直接 `DispatchWindowEvent(WindowClosed)`（1583）。无 `WindowClosing` 派发、无 hidden 窗口豁免、`_forceClose` 标志只用于自证 | 前端 `Window.OnClose` 无法阻止关闭；`Hide()` 后关闭会误触发关闭事件序列 |
| 3 | **无 `NSApplicationDelegate`** | `application_darwin_delegate.m` 实现 30+ 委托方法，关键 4 条：`applicationShouldHandleReopen`（Dock 点击恢复隐藏窗口，#4583）、`applicationShouldTerminate`（`ShouldQuit()` 为 false 时返回 `NSTerminateCancel`）、`openFile`/`openURL`（`HandleOpenFile`/`HandleOpenURL`）、`workspaceWillSleep`/`DidWake`（`SystemWillSleep`/`SystemDidWake`） | `MacOSPlatformApp.cs` 无任何 `NSApplicationDelegate` 实现；`MacOptions.ApplicationShouldTerminateAfterLastWindowClosed`（`Options/MacOptions.cs:24`）**只声明未使用** | Dock 图标点击不恢复窗口；Cmd+Q 绕过 `Application.ShouldQuit`；文件关联/自定义 URL scheme 打开应用时参数丢失；睡眠唤醒无事件 |
| 4 | **窗口级快捷键（非菜单加速键）不生效** | `webview_window_darwin.m:86/102` `keyDown:` / `performKeyEquivalent:` → `processWindowKeyDownEvent` / `processWindowKeyEquivalent` → `KeyBindingManager` 派发 | `MacOSKeyBindingManager.cs` 只有 `RegisterKeyBinding` 登记（`:85`），macOS 侧无任何按键捕获（`MacOSWebviewWindow.cs`/`MacOSPlatformApp.cs` 无 `KeyDown`/`LocalMonitor`）。仅菜单项 `KeyEquivalent`（`MacOSMenu.cs:397`）可用 | `Application.KeyBindingManager.Add("Cmd+K")` 这类非菜单绑定在 macOS **永不触发** |
| 5 | **`<input type="file">` 文件选择无反应** | `webview_window_darwin.m:979` `runOpenPanelWithParameters:` → `NSOpenPanel` + `beginSheetModalForWindow` | `MacUiDelegate`（`MacOSWebviewWindow.cs:2495`）只覆盖 alert/confirm/prompt/mediaCapture，无 `RunOpenPanel` | 网页表单文件上传、富文本插图在 macOS 完全不可用 |
| 6 | **WebContent 进程被回收后白屏不恢复** | `webview_window_darwin.m:970` `webViewWebContentProcessDidTerminate:` → 派发事件并 `[webView reload]`；上游注释点明"睡眠后 jetsam 常见" | `MacNavigationDelegate`（`:2409`）无该回调 | 合盖睡眠/内存压力后窗口停在无响应空白页，只能重启应用 |
| 7 | **`GetZoom()` 恒为 1.0** | `webview_window_darwin.go` `getZoom()` 读 `webView.magnification` | `MacOSWebviewWindow.cs:1022`、`:1025` 硬编码 `return 1.0f/1.0f`（`SetZoom` 只写不读） | JS `Window.GetZoom` 永远返回 1，缩放状态无法回读 |
| 8 | **托盘锚定窗口（menu-bar app 主形态）整体缺失** | `systemtray_darwin.go`：`positionWindow()`、`bounds()`、`showMenu()`、`lastClickedScreenBounds()`、`systrayMenuDisplayedCallback`；Go 侧 `SystemTray.AttachWindow/WindowOffset/WindowDebounce` | `MacOSSystemTray.cs` 仅 `SetLabel`（`:109`）、图标/菜单、点击/双击/右键事件（`:68-77`、`:246-274`）。`AttachWindow/ShowWindow/HideWindow/ToggleWindow/PositionWindow/WindowOffset/WindowDebounce/OpenMenu/ShowMenu/GetBounds/SetIconPosition` 全部走 `ISystemTrayImpl` 默认 no-op | 托盘点击弹窗、窗口贴托盘定位在 macOS 不可用（此形态是 macOS 常见用法） |

---

## 3. P2：选项与能力未落地

### 3.1 窗口选项层（`Options/`）

| # | 差异 | 上游字段 | 我方现状 |
|---|------|----------|----------|
| 9 | **窗口按钮状态** | `WebviewWindowOptions.MinimiseButtonState` / `MaximiseButtonState` / `CloseButtonState` / `FullscreenButtonState`（`ButtonState`: Enabled/Disabled/**Hidden**，`webview_window_options.go:213-216`）+ macOS `setFullscreenButtonState` | 四个选项**完全不存在**；只有布尔 `SetClosable/SetMinimisable/SetMaximisable`（`MacOSWebviewWindow.cs:1294-1358`），无法表达"隐藏红绿灯"，也无法在无边框原生框场景按上游隐藏全部按钮 |
| 10 | **`TabbingMode`** | `MacWindow.TabbingMode`（Default/Automatic/Preferred/Disallowed，映射 `NSWindowTabbingMode`）+ 运行时 `setTabbingMode` | 选项与运行时 setter 均无（全仓无 `TabbingMode`） |
| 11 | **Liquid Glass 细节** | `MacWindow.LiquidGlass`（`Style`/`Material`/`CornerRadius`/`TintColor`/`GroupID`/`GroupSpacing`），`applyLiquidGlass()` 不可用时回退 translucent | 只有 `Backdrop=3` 这个整数（`WebviewWindowMacOptions.cs:22`），且 `ApplyWindowOptions`（`:380`）**未处理 3**——既不建 `NSGlassEffectView` 也不回退 translucent，等于静默忽略 |
| 12 | **标题栏工具栏** | `MacTitleBar.UseToolbar` / `ToolbarStyle` / `HideToolbarSeparator` / `ShowToolbarWhenFullscreen`（`NSToolbar`） | 四项选项已声明（`Options/MacOptions.cs:106-127`）但实现里**无 `NSToolbar`**，仅落地 `AppearsTransparent/Hide/HideTitle/FullSizeContent`（`MacOSWebviewWindow.cs:427-440`） |
| 13 | **`InvisibleTitleBarHeight`** | 隐形标题栏高度，供无边框窗口拖动 | 选项已声明（`WebviewWindowMacOptions.cs:86`），实现中无任何引用 |
| 14 | **窗口级圆角被忽略** | `MacWindow.CornerType` / `CornerRadius`（窗口级） | `MacOSWebviewWindow.cs:221/251/262/890` 只读**应用级** `macOptions.CornerType/CornerRadius`；`WebviewWindowMacOptions.CornerType/CornerRadius`（`:14`、`:19`）声明后未生效 |
| 15 | **窗口级 `WebviewPreferences`** | `MacWindow.WebviewPreferences`（每窗口一份） | 只有应用级 `MacOptions.WebviewPreferences`（`MacOptions.cs:72`），创建时读取（`MacOSWebviewWindow.cs:346`） |
| 16 | **自定义 URL scheme** | `RegisterCustomScheme` 契约 | 走接口默认 no-op；macOS 只注册内置 `wails://`（`MacUrlSchemeHandler`，`:2576`） |
| 17 | **部分 getter/setter 走默认实现** | `getScreen`、`isAlwaysOnTop`、`isNormal`、`isResizable`、`setRelativePosition`、`setFullscreenButtonState`、`setEffects`、`setBackgroundType` | 均未覆写：`GetScreen()` 返回 `null`（JS `Window.GetScreen` 不可用）、`IsAlwaysOnTop()` 恒 `false`、`IsResizable()` 恒 `false`、`SetRelativePosition` no-op（对照 `IWebviewWindowImpl.cs` 默认实现） |

### 3.2 菜单 / 托盘 / 系统能力

| # | 差异 | 上游依据 | 我方现状 |
|---|------|----------|----------|
| 18 | **菜单分节标题** | `menu_darwin.go` `menuSectionHeadersSupported()` + `newNSMenu` 分节处理 | `MacOSMenu.cs` 支持子菜单/分隔符/复选/单选/角色/加速键，无 section header |
| 19 | **托盘菜单显示回调与预点击拦截** | `systrayMenuDisplayedCallback`、`systrayPreClickCallback`（菜单打开前可改状态） | `MacOSSystemTray.cs` 只有点击类事件（`:68-77`） |
| 20 | **系统级全局热键** | `global_shortcut_darwin.go` Carbon `RegisterEventHotKey`（`GlobalShortcutManager`） | 我方的 `Wails.Net.Plugins.GlobalShortcut` 映射到应用内 `KeyBindingManager`，macOS 无系统级热键；Linux 有 X11 实现（`LinuxX11GlobalHotkeys.cs`），macOS 无对应实现 |
| 21 | **自启动策略** | `autostart_darwin.go`：`.app` bundle + macOS 13+ 走 `SMAppService`，否则 `LaunchAgent` | `MacOSAutostartManager.cs` **只有 LaunchAgent**（注释中说明是有意选择），无 `SMAppService` 分支 |

---

## 4. 已对齐（抽样确认，避免重复排查）

窗口：创建/居中/初始全屏/显示、`Frameless`、`CornerType=Square` 与 `=Custom`（应用级）、Backdrop 透明/半透明（`NSVisualEffectView`）、`Appearance`、`HasShadow`、`WindowLevel`、`CollectionBehavior`、`NSPanel` + `PanelPreferences`（4 项）、`InvisibleTitleBar` 之外的大多数标题栏项、最小/最大尺寸、位置/尺寸 DIP 换算（`MacDipCoordinates`）、多屏枚举、`SetContentProtection`、`SetOpacity`、`SetTranslucent`、`StartDrag`、文件拖放（`MacDropView` + 坐标转换）、右键菜单（`OpenContextMenu`）、打印/导出 PDF/预览截图、DevTools 开关、`ExecJS`/`SetHTML`、导航策略（外链走系统浏览器）、`wails://` 资源与 SPA 回退、`external` 消息通道 + 大消息 HTTP 回退、notch 窗口（形状绘制 + 滑入滑出动画 + 跟踪区）。

应用/系统：菜单（21 个 `MenuRole`、加速键解析、Services 菜单）、对话框（消息/打开/多选/保存/目录/过滤器/初始目录）、剪贴板（文本/HTML/图片/文件，**超出上游**）、屏幕信息、Dock 图标/徽标/进度、单实例（临时文件锁）、媒体捕获权限（相机/麦克风 Allow/Deny）、自启动（LaunchAgent）、主题变化事件（`MacSystemThemeObserver`）、`GetAccentColor`、`IsDarkMode`。

---

## 5. 非差异（上游 macOS 同样未实现，勿计入差距）

| 项 | 说明 |
|----|------|
| `window.open` / `target=_blank` | 上游 `webview_window_darwin.m` 无 `createWebViewWithConfiguration:`，同样不创建新窗口 |
| `SetIgnoreMouseEvents` | 上游仅 Linux/Windows 实现，macOS 无 `setIgnoresMouseEvents` 路径 |
| 剪贴板 HTML/图片/文件 | 上游 `clipboard_darwin.go` 仅有 `setText/text`；本项目实现了全量（不是差距，是超出） |

---

## 6. 建议修复顺序

1. **P1-3 NSApplicationDelegate**（一次补齐 Dock 恢复、Cmd+Q → `ShouldQuit`、openFile/openURL、睡眠唤醒；顺带让 `ApplicationShouldTerminateAfterLastWindowClosed` 生效）——单点改动覆盖 4 条用户可见问题。
2. **P1-2 关闭语义**（`windowShouldClose` 等价物：`WindowWillClose` 前先发 `WindowClosing` 并支持 `_forceClose`/hidden 豁免）。
3. **P1-1 `AlwaysOnTop` 初始值**、**P1-7 `GetZoom`**、**P1-5 `RunOpenPanel`**、**P1-6 WebContent 回收 reload**（均为 10~40 行的局部修补）。
4. **P1-8 托盘锚定窗口** + **P1-4 按键捕获**（工作量较大，建议单独一轮，并补 `MacOSSystemTray`/`MacOSKeyBindingManager` 单测）。
5. **P2 批次**：先补选项与运行时 setter（按钮状态、TabbingMode、窗口级圆角/WebviewPreferences、`GetScreen`/`IsAlwaysOnTop` 等 getter），再评估 Liquid Glass 与 `NSToolbar`。

> 修复前建议先在 macOS 实机跑一次 `dotnet build src/Wails.Net.Application.MacOS` + 冒烟，确认当前 `#if MACOS` 分支可编译——CI 目前不覆盖该分支（见 §1 前提）。
