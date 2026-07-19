# 前端 JavaScript API 参考

> 本文档全面描述 Wails.Net 项目注入 Webview 的前端 JavaScript 运行时 API。
> 全部 API 通过 `window.wails` 全局对象暴露，由 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 在运行时动态生成。
> 传输层实现位于 [transport.template.js](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/Resources/transport.template.js)。

## 概述

Wails.Net 在 Webview 启动时注入两段 JavaScript 代码：

1. **标志对象** `window._wails` — 暴露运行时元信息（平台、调试模式、Server 模式）。
2. **API 对象** `window.wails` — 包含全部前端可调用 API，按命名空间组织。

所有 API 调用最终通过 `window._wailsInvoke(method, params)` 经 HTTP `POST /wails/<method>` 发送到后端 `MessageProcessor`，并返回 `Promise`。事件订阅通过 `window._wailsOnEvent(eventName, callback)` 注册本地回调，由后端 `ExecuteScriptAsync` 调用 `window._wailsEmitEvent(name, data)` 触发。

### 命名空间总览

| 命名空间 | 说明 | 对应参考 |
|---------|------|---------|
| `wails` | 顶层对象，包含 `call` 与所有子命名空间 | — |
| `wails.bindings` | 按绑定 ID 调用后端方法 | Wails v3 bindings |
| `wails.events` | 事件订阅与发布 | Wails v3 Events |
| `wails.channels` | 双向流式通道（订阅/推送/关闭） | Wails v3 Channels |
| `wails.window` | 当前窗口操作 | Tauri v2 window / Wails v3 Window |
| `wails.tray` | 系统托盘 | Tauri v2 tray / Wails v3 SystemTray |
| `wails.windows` | 多窗口管理 | Tauri v2 getAllWindows |
| `wails.screen` | 屏幕信息 | Wails v3 Screen |
| `wails.clipboard` | 剪贴板 | Tauri v2 clipboard-manager |
| `wails.dialog` | 对话框 | Wails v3 Dialog |
| `wails.menu` | 菜单 | Wails v3 Menu |
| `wails.application` | 应用级操作 | Wails v3 App |
| `wails.stronghold` | 加密安全存储 | Tauri v2 plugin-stronghold |
| `wails.scope` | 文件系统范围 | Tauri v2 plugin-persisted-scope |
| `wails.localhost` | 本地 HTTP 服务 | Tauri v2 plugin-localhost |
| `wails.fswatch` | 文件监听 | Tauri v2 plugin-fs-watch |
| `wails.system` | 系统信息 | Tauri v2 plugin-os |
| `wails.power` | 电源管理 | Tauri v2 plugin-os |
| `wails.process` | 进程管理 | — |
| `wails.fs` | 文件系统 | Tauri v2 plugin-fs |
| `wails.shell` | Shell 调用 | Tauri v2 plugin-shell |
| `wails.notification` | 系统通知 | Tauri v2 plugin-notification |
| `wails.store` | 持久化键值存储 | Tauri v2 plugin-store |
| `wails.log` | 日志（P1-3：与 `ILogger` 双向桥接） | Tauri v2 plugin-log |
| `wails.updater` | 自动更新（P1-8：多 Provider） | Wails v3 Updater |
| `wails.dpi` | DPI 缩放查询与设置 | — |
| `wails.deeplink` | 深度链接注册与解析 | Tauri v2 plugin-deep-link |
| `wails.windowstate` | 窗口状态持久化 | — |
| `wails.positioner` | 窗口定位（9 种方位） | Tauri v2 plugin-positioner |
| `wails.fileassoc` | 文件关联 | — |
| `wails.i18n` | 国际化（语言切换） | Tauri v2 plugin-localization |
| `wails.shortcut` | 全局快捷键 | Tauri v2 plugin-global-shortcut |
| `wails.autostart` | 开机自启动 | Tauri v2 plugin-autostart |
| `wails.opener` | 安全打开 URL/文件 | Tauri v2 plugin-opener |
| `wails.appinfo` | 应用信息（名称/版本/路径） | — |
| `wails.path` | 路径操作 | — |
| `wails.upload` | 文件上传 | — |
| `wails.websocket` | WebSocket 连接 | — |
| `wails.cookie` | Cookie 管理 | — |
| `wails.sql` | SQLite 数据库 | Tauri v2 plugin-sql |
| `wails.biometric` | 生物识别（仅 Android） | Tauri v2 plugin-biometric |
| `wails.nfc` | NFC 读写（仅 Android） | — |
| `wails.barcode-scanner` | 条码/二维码扫描（仅 Android） | — |
| `wails.haptics` | 触觉反馈（仅 Android） | Tauri v2 plugin-haptics |
| `wails.device` / `wails.toast` | Android 设备信息 / Toast 提示（仅 Android） | Wails v3 androidDeviceInfo / androidShowToast |
| `wails.MenuRole` | MenuRole 常量枚举（21 个值，供 `menu.addRoleItem` 使用） | Wails v3 Role 常量 |

### 运行时标志对象

`window._wails` 由 [RuntimeGenerator.GenerateFlags](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 生成，字段对应 [RuntimeOptions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeOptions.cs)：

```javascript
window._wails = {
  platform: "windows",   // "windows" | "linux" | "server"
  isDebug: true,          // 调试模式
  isServerMode: false     // 是否为无 GUI 的 Server 模式
};
```

## 核心方法

### wails.call(name, args)

通过绑定名称调用后端方法。对应 [RuntimeGenerator.cs GenerateApi](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `call` 字段。

**签名**

```javascript
wails.call(name: string, args?: any[]): Promise<any>
```

**参数**

| 参数 | 类型 | 说明 |
|------|------|------|
| `name` | string | 绑定方法名，格式为 `ServiceName.MethodName` |
| `args` | any[] | 调用参数数组，默认为 `[]` |

**返回值**

`Promise` 解析为后端方法的返回值；若后端返回错误则被 reject。

**示例**

```javascript
// 调用后端 GreetingService.Greet 方法，参数为 "张三"
const result = await wails.call("GreetingService.Greet", ["张三"]);
console.log(result); // 例如："你好，张三！"
```

### wails.bindings.call(bindingId, args)

通过绑定 ID 调用后端方法。绑定 ID 为后端生成的 FNV-1a 32 位哈希。

**签名**

```javascript
wails.bindings.call(bindingId: number|string, args?: any[]): Promise<any>
```

**参数**

| 参数 | 类型 | 说明 |
|------|------|------|
| `bindingId` | number \| string | 绑定 ID（FNV-1a 哈希） |
| `args` | any[] | 调用参数数组 |

**示例**

```javascript
// 按绑定 ID 调用（ID 由源生成器在编译期生成）
const result = await wails.bindings.call(0x12345678, ["参数1", 42]);
```

## 事件 API (wails.events)

事件 API 提供本地事件订阅与跨窗口事件发布能力。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `events` 命名空间。

### wails.events.on(eventName, callback)

订阅本地事件。事件由后端通过 `ExecuteScriptAsync` 调用 `window._wailsEmitEvent(name, data)` 推送。

**签名**

```javascript
wails.events.on(eventName: string, callback: (data: any) => void): () => void
```

**参数**

| 参数 | 类型 | 说明 |
|------|------|------|
| `eventName` | string | 事件名称 |
| `callback` | function | 事件回调，接收事件数据 |

**返回值**

返回取消订阅函数，调用该函数可移除当前回调。

**示例**

```javascript
// 订阅 "userLoggedIn" 事件
const unsubscribe = wails.events.on("userLoggedIn", (data) => {
  console.log("用户登录：", data.userId);
});

// 之后取消订阅
unsubscribe();
```

### wails.events.emit(eventName, data)

向后端发布事件，由后端 `EventProcessor` 广播到所有窗口。

**签名**

```javascript
wails.events.emit(eventName: string, data?: any): Promise<void>
```

**参数**

| 参数 | 类型 | 说明 |
|------|------|------|
| `eventName` | string | 事件名称 |
| `data` | any | 事件数据，可省略 |

**示例**

```javascript
// 发布 "buttonClicked" 事件
await wails.events.emit("buttonClicked", { buttonId: 42 });
```

## 窗口 API (wails.window)

操作当前窗口。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `window` 命名空间，所有方法均返回 `Promise`。

### 窗口大小与位置

#### wails.window.setTitle(title)

设置窗口标题。

```javascript
await wails.window.setTitle("我的应用 - 首页");
```

#### wails.window.setSize(width, height)

设置窗口尺寸。

```javascript
await wails.window.setSize(1280, 720);
```

#### wails.window.setMinSize(width, height)

设置窗口最小尺寸。

```javascript
await wails.window.setMinSize(640, 480);
```

#### wails.window.setMaxSize(width, height)

设置窗口最大尺寸。

```javascript
await wails.window.setMaxSize(1920, 1080);
```

#### wails.window.setPosition(x, y)

设置窗口位置。

```javascript
await wails.window.setPosition(100, 200);
```

#### wails.window.centre()

将窗口居中显示。

```javascript
await wails.window.centre();
```

### 窗口状态控制

#### wails.window.close()

关闭窗口。

```javascript
await wails.window.close();
```

#### wails.window.minimize()

最小化窗口。

```javascript
await wails.window.minimize();
```

#### wails.window.maximize()

最大化窗口。

```javascript
await wails.window.maximize();
```

#### wails.window.unminimize()

取消最小化。

```javascript
await wails.window.unminimize();
```

#### wails.window.unmaximize()

取消最大化。

```javascript
await wails.window.unmaximize();
```

#### wails.window.show()

显示窗口。

```javascript
await wails.window.show();
```

#### wails.window.hide()

隐藏窗口。

```javascript
await wails.window.hide();
```

#### wails.window.focus()

使窗口获得焦点。

```javascript
await wails.window.focus();
```

#### wails.window.restore()

恢复窗口状态。

```javascript
await wails.window.restore();
```

#### wails.window.unFullscreen()

退出全屏模式。

```javascript
await wails.window.unFullscreen();
```

### 窗口外观

#### wails.window.setAlwaysOnTop(onTop)

设置窗口是否置顶。

```javascript
await wails.window.setAlwaysOnTop(true);
```

#### wails.window.setFullscreen(fullscreen)

设置窗口全屏状态。

```javascript
await wails.window.setFullscreen(true);
```

#### wails.window.setResizable(resizable)

设置窗口是否可调整大小。

```javascript
await wails.window.setResizable(false);
```

#### wails.window.setFrameless(frameless)

设置窗口是否无边框。

```javascript
await wails.window.setFrameless(true);
```

#### wails.window.setOpacity(opacity)

设置窗口不透明度。

```javascript
await wails.window.setOpacity(0.9);
```

#### wails.window.getOpacity()

获取窗口不透明度。

```javascript
const opacity = await wails.window.getOpacity();
console.log(opacity); // 0.9
```

#### wails.window.setAlwaysOnTop / setEffects(effects)

设置窗口视觉效果（如 Windows 的 Mica/Acrylic 效果）。

```javascript
await wails.window.setEffects({ effect: "mica", tint: "#000000" });
```

#### wails.window.setBorderColor(color)

设置窗口边框颜色。

```javascript
await wails.window.setBorderColor("#0078D4");
```

### Webview 操作

#### wails.window.execJS(js)

在当前窗口的 Webview 中执行任意 JavaScript 代码。

```javascript
await wails.window.execJS("document.title = '新标题'");
```

#### wails.window.setURL(url)

设置 Webview 加载的 URL。

```javascript
await wails.window.setURL("https://example.com");
```

#### wails.window.setHTML(html)

设置 Webview 内容为 HTML 字符串。

```javascript
await wails.window.setHTML("<h1>Hello</h1>");
```

#### wails.window.goBack()

Webview 后退。

```javascript
await wails.window.goBack();
```

#### wails.window.goForward()

Webview 前进。

```javascript
await wails.window.goForward();
```

#### wails.window.reload()

Webview 重新加载。

```javascript
await wails.window.reload();
```

#### wails.window.injectCSS(css)

向当前页面注入 CSS 样式。

```javascript
await wails.window.injectCSS("body { background: #fff; }");
```

#### wails.window.registerCustomScheme(scheme)

注册自定义 URL Scheme。

```javascript
await wails.window.registerCustomScheme("wailsapp");
```

### 窗口状态查询

#### wails.window.getSize()

获取窗口尺寸。

```javascript
const { width, height } = await wails.window.getSize();
```

#### wails.window.getPosition()

获取窗口位置。

```javascript
const { x, y } = await wails.window.getPosition();
```

#### wails.window.getURL()

获取当前 Webview 加载的 URL。

```javascript
const url = await wails.window.getURL();
```

#### wails.window.getZoom()

获取当前缩放比例。

```javascript
const zoom = await wails.window.getZoom();
```

#### wails.window.isFullscreen()

是否处于全屏状态。

```javascript
const fs = await wails.window.isFullscreen();
```

#### wails.window.isMaximised()

是否处于最大化状态。

```javascript
const max = await wails.window.isMaximised();
```

#### wails.window.isMinimised()

是否处于最小化状态。

```javascript
const min = await wails.window.isMinimised();
```

#### wails.window.isVisible()

窗口是否可见。

```javascript
const visible = await wails.window.isVisible();
```

#### wails.window.isFocused()

窗口是否获得焦点。

```javascript
const focused = await wails.window.isFocused();
```

### 缩放

#### wails.window.setZoom(zoom)

设置缩放比例。

```javascript
await wails.window.setZoom(1.5);
```

#### wails.window.zoomIn()

放大。

```javascript
await wails.window.zoomIn();
```

#### wails.window.zoomOut()

缩小。

```javascript
await wails.window.zoomOut();
```

#### wails.window.zoomReset()

重置缩放。

```javascript
await wails.window.zoomReset();
```

### 开发者工具

#### wails.window.openDevTools()

打开开发者工具。

```javascript
await wails.window.openDevTools();
```

#### wails.window.closeDevTools()

关闭开发者工具。

```javascript
await wails.window.closeDevTools();
```

### 打印与预览

#### wails.window.print()

打开打印对话框。

```javascript
await wails.window.print();
```

#### wails.window.printToPDF(path, options?)

将当前页面打印为 PDF 文件。

**签名**

```javascript
wails.window.printToPDF(path: string, options?: object|null): Promise<void>
```

**示例**

```javascript
await wails.window.printToPDF("C:\\temp\\page.pdf", {
  landscape: false,
  printBackground: true
});
```

#### wails.window.capturePreview()

捕获当前页面预览图。

```javascript
const preview = await wails.window.capturePreview();
```

### 任务栏与平台特性

#### wails.window.setTaskbarProgress(state, completed, total)

设置任务栏进度。`state` 可为 `"none"`、`"indeterminate"`、`"normal"`、`"error"`、`"paused"`。

```javascript
await wails.window.setTaskbarProgress("normal", 50, 100);
```

#### wails.window.setOverlayIcon(iconBytes, description)

设置任务栏覆盖图标。

```javascript
await wails.window.setOverlayIcon(iconBytes, "上传中");
```

#### wails.window.setSkipTaskbar(skip)

设置是否在任务栏中隐藏窗口。

```javascript
await wails.window.setSkipTaskbar(true);
```

#### wails.window.setIgnoreCursorEvents(ignore)

设置窗口是否忽略鼠标事件（穿透点击）。

```javascript
await wails.window.setIgnoreCursorEvents(true);
```

#### wails.window.setBadgeCount(count)

设置窗口角标数字（macOS Dock / Linux 部分 WM 支持）。

```javascript
await wails.window.setBadgeCount(5);
```

#### wails.window.setBadgeLabel(label)

设置窗口角标文本。

```javascript
await wails.window.setBadgeLabel("New");
```

#### wails.window.setVisibleOnAllWorkspaces(visible)

设置窗口是否在所有工作区可见。

```javascript
await wails.window.setVisibleOnAllWorkspaces(true);
```

#### wails.window.setFileDropEnabled(enabled)

启用或禁用文件拖放支持。

```javascript
await wails.window.setFileDropEnabled(true);
```

## 托盘 API (wails.tray)

系统托盘操作。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `tray` 命名空间。所有方法返回 `Promise`。

### wails.tray.setIcon(iconData)

设置托盘图标。

```javascript
await wails.tray.setIcon(iconBytes); // 图标二进制数据
```

### wails.tray.setLabel(label)

设置托盘文本标签（macOS/Linux 支持）。

```javascript
await wails.tray.setLabel("运行中");
```

### wails.tray.setMenu(menu)

设置托盘右键菜单。

```javascript
await wails.tray.setMenu([
  { label: "打开主窗口", onClick: "showMainWindow" },
  { label: "退出", onClick: "quit" }
]);
```

### wails.tray.setTooltip(tooltip)

设置鼠标悬停提示。

```javascript
await wails.tray.setTooltip("我的应用 - 正在运行");
```

### wails.tray.destroy()

销毁托盘图标。

```javascript
await wails.tray.destroy();
```

### wails.tray.isVisible()

托盘是否可见。

```javascript
const visible = await wails.tray.isVisible();
```

### wails.tray.show()

显示托盘。

```javascript
await wails.tray.show();
```

### wails.tray.hide()

隐藏托盘。

```javascript
await wails.tray.hide();
```

## 窗口管理 API (wails.windows)

多窗口管理 API。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `windows` 命名空间。

### wails.windows.getCurrent()

获取当前窗口信息。

```javascript
const win = await wails.windows.getCurrent();
console.log(win.id, win.title);
```

### wails.windows.getAll()

获取所有窗口列表。

```javascript
const all = await wails.windows.getAll();
all.forEach(w => console.log(w.id, w.title));
```

### wails.windows.getByName(name)

按名称获取窗口。

```javascript
const win = await wails.windows.getByName("settings");
```

### wails.windows.getById(id)

按 ID 获取窗口。

```javascript
const win = await wails.windows.getById(2);
```

### wails.windows.emit(eventName, data, targetWindowId?)

向指定窗口（或全部窗口）发送事件。

**签名**

```javascript
wails.windows.emit(eventName: string, data: any, targetWindowId?: number|null): Promise<void>
```

**示例**

```javascript
// 向 ID 为 2 的窗口发送事件
await wails.windows.emit("refreshData", { ts: Date.now() }, 2);

// 广播到所有窗口
await wails.windows.emit("themeChanged", { theme: "dark" }, null);
```

## 屏幕 API (wails.screen)

### wails.screen.getAll()

获取所有显示器信息。

```javascript
const screens = await wails.screen.getAll();
screens.forEach(s => console.log(s.id, s.width, s.height));
```

## 剪贴板 API (wails.clipboard)

剪贴板操作。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `clipboard` 命名空间。

### wails.clipboard.setText(text)

写入纯文本。

```javascript
await wails.clipboard.setText("Hello, World!");
```

### wails.clipboard.getText()

读取纯文本。

```javascript
const text = await wails.clipboard.getText();
```

### wails.clipboard.setHTML(html, fallbackText?)

写入 HTML 内容（可附纯文本回退）。

**签名**

```javascript
wails.clipboard.setHTML(html: string, fallbackText?: string): Promise<void>
```

**示例**

```javascript
await wails.clipboard.setHTML("<b>加粗内容</b>", "加粗内容");
```

### wails.clipboard.getHTML()

读取 HTML 内容。

```javascript
const html = await wails.clipboard.getHTML();
```

### wails.clipboard.setFiles(files)

写入文件列表到剪贴板。

```javascript
await wails.clipboard.setFiles(["C:\\path\\to\\file1.txt", "C:\\path\\to\\file2.txt"]);
```

### wails.clipboard.getFiles()

读取剪贴板中的文件列表。

```javascript
const files = await wails.clipboard.getFiles();
```

## 对话框 API (wails.dialog)

系统对话框。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `dialog` 命名空间。

### wails.dialog.openFile(options?)

打开文件选择对话框。

**签名**

```javascript
wails.dialog.openFile(options?: object): Promise<string|string[]|null>
```

**示例**

```javascript
const filePath = await wails.dialog.openFile({
  title: "选择文件",
  filters: [{ name: "图片", extensions: ["png", "jpg"] }],
  multiple: false
});
```

### wails.dialog.saveFile(options?)

打开保存文件对话框。

```javascript
const path = await wails.dialog.saveFile({
  title: "保存为",
  defaultPath: "untitled.txt",
  filters: [{ name: "文本文件", extensions: ["txt"] }]
});
```

### wails.dialog.message(title, message, type?)

显示消息对话框。`type` 可为 `"info"`、`"warning"`、`"error"`，默认 `"info"`。

**签名**

```javascript
wails.dialog.message(title: string, message: string, type?: string): Promise<void>
```

**示例**

```javascript
await wails.dialog.message("提示", "操作已完成", "info");
```

### wails.dialog.question(title, message, buttons?)

显示询问对话框，返回用户选择的按钮文本。

**签名**

```javascript
wails.dialog.question(title: string, message: string, buttons?: string[]): Promise<string>
```

**示例**

```javascript
const answer = await wails.dialog.question(
  "确认",
  "确定要删除这条记录吗？",
  ["是", "否"]
);
if (answer === "是") {
  // 执行删除
}
```

## 菜单 API (wails.menu)

菜单管理。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `menu` 命名空间。

### wails.menu.setApplicationMenu(menu)

设置应用主菜单。

```javascript
await wails.menu.setApplicationMenu([
  {
    label: "文件",
    submenu: [
      { id: "file.new", label: "新建" },
      { id: "file.open", label: "打开" },
      { id: "file.quit", label: "退出" }
    ]
  }
]);
```

### wails.menu.setContextMenu(menu)

设置右键上下文菜单。

```javascript
await wails.menu.setContextMenu([
  { id: "copy", label: "复制" },
  { id: "paste", label: "粘贴" }
]);
```

### wails.menu.updateMenuItem(id, properties)

更新菜单项属性。

**签名**

```javascript
wails.menu.updateMenuItem(id: string, properties: object): Promise<void>
```

**示例**

```javascript
await wails.menu.updateMenuItem("file.open", {
  label: "打开文件...",
  enabled: false,
  checked: false
});
```

### wails.menu.popup(menu, x?, y?)

在指定坐标弹出菜单。

**签名**

```javascript
wails.menu.popup(menu: object, x?: number, y?: number): Promise<void>
```

**示例**

```javascript
await wails.menu.popup([
  { id: "cut", label: "剪切" },
  { id: "copy", label: "复制" }
], 200, 300);
```

### wails.menu.addRoleItem(parentId, role, label?)

向指定父菜单追加一个 MenuRole 角色菜单项，返回新菜单项 ID（字符串形式的 uint）。对应 [MenuPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/MenuPlugin.cs) 与 [MenuRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRole.cs)。

**签名**

```javascript
wails.menu.addRoleItem(parentId: string, role: string, label?: string): Promise<string>
```

**参数**

| 参数 | 类型 | 说明 |
|------|------|------|
| `parentId` | string | 父菜单项 ID（字符串形式的 uint） |
| `role` | string | MenuRole 常量，使用 `wails.MenuRole.*`（如 `wails.MenuRole.Copy`） |
| `label` | string? | 自定义标签，留空时由平台实现提供默认本地化文本 |

**示例**

```javascript
const itemId = await wails.menu.addRoleItem(parentId, wails.MenuRole.Copy);
const aboutId = await wails.menu.addRoleItem(parentId, wails.MenuRole.About, "关于本应用");
```

### wails.menu.addStandardEditMenu(parentId)

向指定父菜单追加标准编辑菜单项（Undo / Redo / Separator / Cut / Copy / Paste / SelectAll）。对应 [Menu.AddStandardEditMenu](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/Menu.cs)。

```javascript
await wails.menu.addStandardEditMenu(parentId);
```

### wails.menu.addStandardWindowMenu(parentId)

向指定父菜单追加标准窗口菜单项（Minimize / Maximize / Separator / CloseWindow）。

```javascript
await wails.menu.addStandardWindowMenu(parentId);
```

### wails.menu.addStandardHelpMenu(parentId, metadata?, label?)

向指定父菜单追加标准帮助菜单项（About）。`metadata` 用于定制关于对话框内容。

**签名**

```javascript
wails.menu.addStandardHelpMenu(
  parentId: string,
  metadata?: { Name?, Version?, ShortVersion?, Authors?, Copyright?, License?, Website?, WebsiteLabel?, Comments? } | null,
  label?: string
): Promise<void>
```

**示例**

```javascript
await wails.menu.addStandardHelpMenu(parentId, {
  Name: "MyApp",
  Version: "1.0.0",
  Copyright: "© 2026",
  Website: "https://example.com"
});
```

### wails.MenuRole — MenuRole 常量

[RuntimeGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 注入 `wails.MenuRole` 常量对象，包含全部 21 个角色枚举值字符串，供 `wails.menu.addRoleItem` 的 `role` 参数使用：

```javascript
wails.MenuRole = {
  None: "None", Separator: "Separator",
  Copy: "Copy", Cut: "Cut", Paste: "Paste", SelectAll: "SelectAll",
  Undo: "Undo", Redo: "Redo",
  Minimize: "Minimize", Maximize: "Maximize", Fullscreen: "Fullscreen",
  CloseWindow: "CloseWindow", Zoom: "Zoom",
  About: "About", Quit: "Quit",
  Hide: "Hide", HideOthers: "HideOthers", ShowAll: "ShowAll",
  Services: "Services", BringAllToFront: "BringAllToFront",
  ToggleFullScreen: "ToggleFullScreen"
};
```

> macOS 专属角色（`Hide` / `HideOthers` / `ShowAll` / `Services` / `BringAllToFront` / `Zoom` / `ToggleFullScreen`）在 Windows / Linux 上静默 no-op，由 [MenuRoleHelper.IsMacOSExclusive](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRoleHelper.cs) 在运行时判定。

## 应用 API (wails.application)

应用级操作。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `application` 命名空间。

### wails.application.quit()

退出应用。

```javascript
await wails.application.quit();
```

### wails.application.hide()

隐藏应用（macOS 上隐藏到 Dock）。

```javascript
await wails.application.hide();
```

### wails.application.show()

显示应用。

```javascript
await wails.application.show();
```

### wails.application.getName()

获取应用名称。

```javascript
const name = await wails.application.getName();
```

### wails.application.setIcon(iconData)

设置应用图标。

```javascript
await wails.application.setIcon(iconBytes);
```

### wails.application.isDarkMode()

查询是否为暗色模式。

```javascript
const dark = await wails.application.isDarkMode();
```

### wails.application.getAccentColor()

获取系统强调色。

```javascript
const color = await wails.application.getAccentColor();
// 例如 "#0078D4"
```

### wails.application.setTheme(theme)

设置应用主题。`theme` 可为 `"system"`、`"light"`、`"dark"`。

```javascript
await wails.application.setTheme("dark");
```

### wails.application.onThemeChanged(callback)

注册系统主题变更回调。

```javascript
const unsub = await wails.application.onThemeChanged((isDark) => {
  console.log("主题已变更：", isDark ? "暗色" : "亮色");
});
```

## 加密存储 API (wails.stronghold)

加密安全存储。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `stronghold` 命名空间，参考 Tauri v2 plugin-stronghold。

所有方法均支持可选的 `vaultPath` 参数以指定不同的保险库文件，省略时使用默认保险库。

### wails.stronghold.unlock(password, vaultPath?)

使用密码解锁保险库。

**签名**

```javascript
wails.stronghold.unlock(password: string, vaultPath?: string|null): Promise<void>
```

**示例**

```javascript
await wails.stronghold.unlock("my-password-123");
```

### wails.stronghold.lock(vaultPath?)

锁定保险库。

```javascript
await wails.stronghold.lock();
```

### wails.stronghold.saveSecret(key, value, vaultPath?)

保存加密密钥。

**签名**

```javascript
wails.stronghold.saveSecret(key: string, value: string, vaultPath?: string|null): Promise<void>
```

**示例**

```javascript
await wails.stronghold.saveSecret("api.token", "sk-xxxx-yyyy");
```

### wails.stronghold.getSecret(key, vaultPath?)

读取加密密钥。

```javascript
const token = await wails.stronghold.getSecret("api.token");
```

### wails.stronghold.deleteSecret(key, vaultPath?)

删除加密密钥。

```javascript
await wails.stronghold.deleteSecret("api.token");
```

### wails.stronghold.listKeys(vaultPath?)

列出所有密钥名称。

```javascript
const keys = await wails.stronghold.listKeys();
```

### wails.stronghold.isUnlocked(vaultPath?)

查询保险库是否已解锁。

```javascript
const unlocked = await wails.stronghold.isUnlocked();
```

### wails.stronghold.changePassword(oldPassword, newPassword, vaultPath?)

修改保险库密码。

**签名**

```javascript
wails.stronghold.changePassword(
  oldPassword: string,
  newPassword: string,
  vaultPath?: string|null
): Promise<void>
```

**示例**

```javascript
await wails.stronghold.changePassword("old-pwd", "new-pwd");
```

## 文件系统范围 API (wails.scope)

文件系统范围持久化管理。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `scope` 命名空间，参考 Tauri v2 plugin-persisted-scope。

`scopePath` 参数可选，用于指定不同的范围持久化文件路径，省略时使用默认范围。

### wails.scope.addPath(path, scopePath?)

向范围中添加路径。

**签名**

```javascript
wails.scope.addPath(path: string, scopePath?: string|null): Promise<void>
```

**示例**

```javascript
await wails.scope.addPath("C:\\Users\\me\\Documents");
```

### wails.scope.removePath(path, scopePath?)

从范围中移除路径。

```javascript
await wails.scope.removePath("C:\\Users\\me\\Documents");
```

### wails.scope.listPaths(scopePath?)

列出范围内的所有路径。

```javascript
const paths = await wails.scope.listPaths();
```

### wails.scope.clear(scopePath?)

清空范围。

```javascript
await wails.scope.clear();
```

### wails.scope.isAllowed(path, scopePath?)

查询路径是否在范围内。

```javascript
const allowed = await wails.scope.isAllowed("C:\\Users\\me\\Documents\\file.txt");
```

### wails.scope.save(scopePath?)

将当前范围持久化到磁盘。

```javascript
await wails.scope.save();
```

### wails.scope.load(scopePath?)

从磁盘加载范围。

```javascript
await wails.scope.load();
```

## 本地 HTTP API (wails.localhost)

嵌入式本地 HTTP 服务器。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `localhost` 命名空间，参考 Tauri v2 plugin-localhost。

所有方法均以 `port` 作为服务器标识。

### wails.localhost.start(port, rootDir?)

启动本地 HTTP 服务器。

**签名**

```javascript
wails.localhost.start(port: number, rootDir?: string|null): Promise<void>
```

**示例**

```javascript
await wails.localhost.start(8080, "C:\\www\\static");
```

### wails.localhost.stop(port)

停止指定端口的服务器。

```javascript
await wails.localhost.stop(8080);
```

### wails.localhost.getUrl(port)

获取服务器 URL。

```javascript
const url = await wails.localhost.getUrl(8080);
// 例如 "http://localhost:8080/"
```

### wails.localhost.isRunning(port)

查询服务器是否运行中。

```javascript
const running = await wails.localhost.isRunning(8080);
```

### wails.localhost.setRoot(port, rootDir)

设置服务器根目录。

```javascript
await wails.localhost.setRoot(8080, "C:\\www\\new-root");
```

### wails.localhost.addRoute(port, route, method)

添加自定义路由。

```javascript
await wails.localhost.addRoute(8080, "/api/status", "GET");
```

### wails.localhost.removeRoute(port, route)

移除自定义路由。

```javascript
await wails.localhost.removeRoute(8080, "/api/status");
```

### wails.localhost.listRoutes(port)

列出所有自定义路由。

```javascript
const routes = await wails.localhost.listRoutes(8080);
```

## 文件监听 API (wails.fswatch)

文件系统监听。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `fswatch` 命名空间，参考 Tauri v2 plugin-fs-watch。

### wails.fswatch.watch(path, recursive?, extensions?)

开始监听指定路径。

**签名**

```javascript
wails.fswatch.watch(
  path: string,
  recursive?: boolean,
  extensions?: string[]|null
): Promise<string> // 返回监听 ID
```

**示例**

```javascript
const watchId = await wails.fswatch.watch("C:\\Logs", true, [".log"]);
```

### wails.fswatch.unwatch(id)

停止指定的监听。

```javascript
await wails.fswatch.unwatch(watchId);
```

### wails.fswatch.unwatchAll()

停止所有监听。

```javascript
await wails.fswatch.unwatchAll();
```

### wails.fswatch.listWatches()

列出所有监听 ID。

```javascript
const ids = await wails.fswatch.listWatches();
```

### wails.fswatch.isWatching(id)

查询指定监听是否活跃。

```javascript
const active = await wails.fswatch.isWatching(watchId);
```

## 系统信息 API (wails.system)

系统信息查询。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `system` 命名空间，参考 Tauri v2 plugin-os。

### wails.system.platform()

获取平台标识。

```javascript
const p = await wails.system.platform(); // 例如 "windows" | "linux"
```

### wails.system.arch()

获取 CPU 架构。

```javascript
const arch = await wails.system.arch(); // 例如 "x86_64" | "aarch64"
```

### wails.system.hostname()

获取主机名。

```javascript
const host = await wails.system.hostname();
```

### wails.system.version()

获取操作系统版本。

```javascript
const ver = await wails.system.version(); // 例如 "10.0.22631"
```

### wails.system.type()

获取操作系统类型。

```javascript
const type = await wails.system.type(); // 例如 "windows" | "linux"
```

### wails.system.locale()

获取系统语言区域。

```javascript
const locale = await wails.system.locale(); // 例如 "zh-CN"
```

### wails.system.timezone()

获取系统时区。

```javascript
const tz = await wails.system.timezone(); // 例如 "Asia/Shanghai"
```

## 电源 API (wails.power)

电源管理。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `power` 命名空间。

### wails.power.requestWakeLock()

请求唤醒锁，阻止系统进入睡眠状态。

```javascript
await wails.power.requestWakeLock();
```

### wails.power.releaseWakeLock()

释放唤醒锁。

```javascript
await wails.power.releaseWakeLock();
```

### wails.power.isWakeLockHeld()

查询唤醒锁是否持有中。

```javascript
const held = await wails.power.isWakeLockHeld();
```

## 进程 API (wails.process)

进程管理。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `process` 命名空间。

### wails.process.exit(code?)

退出进程。

**签名**

```javascript
wails.process.exit(code?: number): Promise<void> // code 默认为 0
```

**示例**

```javascript
await wails.process.exit(0);
```

### wails.process.restart()

重启应用进程。

```javascript
await wails.process.restart();
```

### wails.process.getPid()

获取当前进程 PID。

```javascript
const pid = await wails.process.getPid();
```

## 文件系统 API (wails.fs)

文件系统操作。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `fs` 命名空间，参考 Tauri v2 plugin-fs。

### wails.fs.readTextFile(path)

读取文本文件内容。

```javascript
const text = await wails.fs.readTextFile("C:\\path\\to\\file.txt");
```

### wails.fs.writeTextFile(path, content)

写入文本文件。

**签名**

```javascript
wails.fs.writeTextFile(path: string, content: string): Promise<void>
```

**示例**

```javascript
await wails.fs.writeTextFile("C:\\temp\\out.txt", "Hello!");
```

### wails.fs.readBinaryFile(path)

读取二进制文件。

```javascript
const bytes = await wails.fs.readBinaryFile("C:\\path\\to\\image.png");
```

### wails.fs.writeBinaryFile(path, data)

写入二进制文件。

**签名**

```javascript
wails.fs.writeBinaryFile(path: string, data: ArrayBuffer|Uint8Array): Promise<void>
```

**示例**

```javascript
await wails.fs.writeBinaryFile("C:\\temp\\out.bin", new Uint8Array([1,2,3]));
```

### wails.fs.exists(path)

判断路径是否存在。

```javascript
const exists = await wails.fs.exists("C:\\temp\\file.txt");
```

### wails.fs.mkdir(path, recursive?)

创建目录。

**签名**

```javascript
wails.fs.mkdir(path: string, recursive?: boolean): Promise<void>
```

**示例**

```javascript
await wails.fs.mkdir("C:\\temp\\new\\dir", true);
```

### wails.fs.remove(path)

删除文件或目录。

```javascript
await wails.fs.remove("C:\\temp\\file.txt");
```

### wails.fs.rename(oldPath, newPath)

重命名/移动文件或目录。

```javascript
await wails.fs.rename("C:\\temp\\old.txt", "C:\\temp\\new.txt");
```

### wails.fs.copy(src, dst)

复制文件或目录。

```javascript
await wails.fs.copy("C:\\temp\\a.txt", "C:\\temp\\b.txt");
```

### wails.fs.readDir(path)

读取目录内容。

```javascript
const entries = await wails.fs.readDir("C:\\temp");
entries.forEach(e => console.log(e.name, e.isDirectory));
```

## Shell API (wails.shell)

Shell 调用。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `shell` 命名空间，参考 Tauri v2 plugin-shell。

### wails.shell.execute(command, args?, cwd?)

执行命令行程序。

**签名**

```javascript
wails.shell.execute(
  command: string,
  args?: string[],
  cwd?: string|null
): Promise<{ stdout: string, stderr: string, code: number }>
```

**示例**

```javascript
const result = await wails.shell.execute("cmd", ["/c", "dir"], "C:\\");
console.log(result.stdout);
```

### wails.shell.open(path)

使用系统默认程序打开文件或目录。

```javascript
await wails.shell.open("C:\\Users\\me\\Documents");
```

### wails.shell.openUrl(url)

使用系统默认浏览器打开 URL。

```javascript
await wails.shell.openUrl("https://github.com");
```

## 通知 API (wails.notification)

系统通知。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `notification` 命名空间，参考 Tauri v2 plugin-notification。

### wails.notification.show(title, body)

显示系统通知。

**签名**

```javascript
wails.notification.show(title: string, body: string): Promise<void>
```

**示例**

```javascript
await wails.notification.show("下载完成", "文件已保存到 C:\\Downloads");
```

### wails.notification.requestPermission()

请求通知权限。

```javascript
const granted = await wails.notification.requestPermission();
```

### wails.notification.hasPermission()

查询是否已获得通知权限。

```javascript
const ok = await wails.notification.hasPermission();
```

## 存储 API (wails.store)

持久化键值存储。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `store` 命名空间，参考 Tauri v2 plugin-store。

### wails.store.get(key)

读取键值。

```javascript
const value = await wails.store.get("lastLogin");
```

### wails.store.set(key, value)

写入键值。

**签名**

```javascript
wails.store.set(key: string, value: any): Promise<void>
```

**示例**

```javascript
await wails.store.set("lastLogin", Date.now());
```

### wails.store.delete(key)

删除键值。

```javascript
await wails.store.delete("lastLogin");
```

### wails.store.keys()

列出所有键。

```javascript
const keys = await wails.store.keys();
```

### wails.store.clear()

清空所有键值。

```javascript
await wails.store.clear();
```

### wails.store.has(key)

判断键是否存在。

```javascript
const exists = await wails.store.has("lastLogin");
```

## 日志 API (wails.log)

日志记录。对应 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 中的 `log` 命名空间，参考 Tauri v2 plugin-log。所有日志最终通过 `Microsoft.Extensions.Logging` 输出到后端日志管线。

### wails.log.debug(message)

记录 Debug 级别日志。

```javascript
await wails.log.debug("进入 onInit 函数");
```

### wails.log.info(message)

记录 Info 级别日志。

```javascript
await wails.log.info("用户登录成功");
```

### wails.log.warn(message)

记录 Warn 级别日志。

```javascript
await wails.log.warn("配置文件缺失，使用默认值");
```

### wails.log.error(message)

记录 Error 级别日志。

```javascript
await wails.log.error("请求后端 API 失败：" + err.message);
```

### wails.log.trace(message)

记录 Trace 级别日志。

```javascript
await wails.log.trace("state = " + JSON.stringify(state));
```

## 通道 API (wails.channels)

双向流式通信通道，对应 Wails v3 Channels。由 [ChannelManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Channels/ChannelManager.cs) 管理，支持长连接、消息推送、双向消息流。

通道适用于：进度推送、流式数据、长轮询替代、自定义协议通信。

### wails.channels.open(name)

打开或创建一个双向通道。

**签名**

```javascript
wails.channels.open(name: string): Promise<Channel>
```

**Channel 对象**

| 方法 | 签名 | 说明 |
|------|------|------|
| `onMessage(cb)` | `(data: any) => void` | 注册消息接收回调 |
| `onClose(cb)` | `() => void` | 注册通道关闭回调 |
| `send(data)` | `(data: any) => Promise<void>` | 向后端发送消息 |
| `close()` | `() => Promise<void>` | 关闭通道 |

**示例**

```javascript
const channel = await wails.channels.open("progress");

// 接收后端推送
channel.onMessage((data) => {
  console.log("进度更新：", data.percent);
});

channel.onClose(() => {
  console.log("通道已关闭");
});

// 向后端发送消息
await channel.send({ command: "pause" });

// 关闭通道
await channel.close();
```

### wails.channels.close(name)

关闭指定通道。

```javascript
await wails.channels.close("progress");
```

### wails.channels.list()

列出当前所有活跃通道。

```javascript
const names = await wails.channels.list();
// 例如：["progress", "logs", "notifications"]
```

## 更新 API (wails.updater)

自动更新检查与下载（P1-8：多 Provider）。对应 [UpdaterPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/UpdaterPlugin.cs)，后端使用 `UpdaterService` 链式查询多个 Provider（Http/GitHub/GitLab/自定义）。

### wails.updater.check()

检查是否有可用更新。返回更新信息或 `null`（无更新）。

**返回值**

```typescript
interface UpdateInfo {
  version: string;        // 新版本号
  releaseNotes?: string;  // 发行说明
  releaseUrl?: string;     // 发行页面 URL
  downloadUrl: string;     // 下载 URL
  size?: number;           // 文件大小（字节）
  date?: string;           // 发布日期（ISO 8601）
  providerName: string;    // 提供该结果的 Provider 名称
}
```

**示例**

```javascript
const update = await wails.updater.check();
if (update) {
  console.log(`发现新版本 ${update.version}（来源：${update.providerName}）`);
  console.log(`发行说明：${update.releaseNotes}`);
} else {
  console.log("已是最新版本");
}
```

### wails.updater.download(update)

下载指定更新的安装包。返回下载进度回调的取消函数。

**签名**

```javascript
wails.updater.download(update: UpdateInfo): Promise<DownloadResult>
```

**示例**

```javascript
const update = await wails.updater.check();
if (update) {
  const result = await wails.updater.download(update);
  console.log(`已下载到：${result.path}（大小：${result.size} 字节）`);
}
```

### wails.updater.install(path)

安装已下载的更新包。

```javascript
await wails.updater.install(downloadResult.path);
```

### wails.updater.getCurrentVersion()

获取当前应用版本号。

```javascript
const version = await wails.updater.getCurrentVersion();
console.log(version); // 例如 "1.0.0"
```

### wails.updater.providers()

列出已注册的 Provider 名称（按检查顺序）。

```javascript
const names = await wails.updater.providers();
// 例如 ["GitHubUpdateProvider", "HttpUpdateProvider"]
```

## 深度链接 API (wails.deeplink)

深度链接（Custom URL Scheme）注册与解析。对应 [DeepLinkPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/DeepLinkPlugin.cs)，参考 Tauri v2 plugin-deep-link。

### wails.deeplink.register(scheme)

注册自定义 URL Scheme（如 `myapp://`）。

**签名**

```javascript
wails.deeplink.register(scheme: string): Promise<void>
```

**示例**

```javascript
// 注册 myapp:// scheme
await wails.deeplink.register("myapp");
// 此后系统会将以 myapp:// 开头的 URL 转发给本应用
```

### wails.deeplink.unregister(scheme)

取消注册的 URL Scheme。

```javascript
await wails.deeplink.unregister("myapp");
```

### wails.deeplink.getCurrent()

获取当前启动应用时所使用的深度链接（如果有）。

```javascript
const url = await wails.deeplink.getCurrent();
// 例如 "myapp://open?file=123" 或 null
```

### wails.deeplink.onOpenUrl(callback)

订阅深度链接事件。当系统将 URL 转发给应用时触发。

**签名**

```javascript
wails.deeplink.onOpenUrl(callback: (url: string) => void): () => void
```

**返回值**

返回取消订阅函数。

**示例**

```javascript
const unsubscribe = wails.deeplink.onOpenUrl((url) => {
  console.log("收到深度链接：", url);
  // 例如：解析 url 并导航到对应页面
  const params = new URLSearchParams(url.split("?")[1]);
  const fileId = params.get("file");
  if (fileId) {
    openFile(fileId);
  }
});

// 之后取消订阅
unsubscribe();
```

### wails.deeplink.isRegistered(scheme)

查询指定 Scheme 是否已注册。

```javascript
const registered = await wails.deeplink.isRegistered("myapp");
```

## DPI 缩放 API (wails.dpi)

DPI 缩放查询与设置。对应 [DpiScalePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/DpiScalePlugin.cs)。

### wails.dpi.getScale()

获取当前显示器的 DPI 缩放比例。

```javascript
const scale = await wails.dpi.getScale();
// 例如 1.0（100%）、1.25（125%）、1.5（150%）、2.0（200%）
```

### wails.dpi.setScale(scale)

设置应用的 DPI 缩放比例。

```javascript
await wails.dpi.setScale(1.5);
```

## 应用信息 API (wails.appinfo)

应用信息查询。对应 [AppInfoPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/AppInfoPlugin.cs)。

### wails.appinfo.getName()

获取应用名称。

```javascript
const name = await wails.appinfo.getName();
```

### wails.appinfo.getVersion()

获取应用版本号。

```javascript
const version = await wails.appinfo.getVersion();
```

### wails.appinfo.getPath()

获取应用相关路径。

```javascript
const paths = await wails.appinfo.getPath();
// 例如 { executable: "C:\\...\\MyApp.exe", dataDir: "...", tempDir: "..." }
```

## 路径 API (wails.path)

跨平台路径操作。对应 [PathPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/PathPlugin.cs)。

### wails.path.join(...segments)

拼接路径。

```javascript
const fullPath = await wails.path.join("C:\\Users", "me", "Documents", "file.txt");
```

### wails.path.normalize(path)

规范化路径。

```javascript
const normalized = await wails.path.normalize("C:\\Users\\..\\Users\\me");
// "C:\\Users\\me"
```

### wails.path.dirname(path)

获取父目录。

```javascript
const dir = await wails.path.dirname("C:\\Users\\me\\file.txt");
// "C:\\Users\\me"
```

### wails.path.basename(path)

获取文件名。

```javascript
const name = await wails.path.basename("C:\\Users\\me\\file.txt");
// "file.txt"
```

### wails.path.extname(path)

获取扩展名。

```javascript
const ext = await wails.path.extname("file.tar.gz");
// ".gz"
```

### wails.path.sep()

获取当前平台的路径分隔符。

```javascript
const sep = await wails.path.sep();
// Windows: "\\"，Linux: "/"
```

## 移动端 API（仅 Android）

以下命名空间仅在 `net10.0-android36.0` 目标下可用。在其他平台调用会返回 `PlatformNotSupportedException`。

### 生物识别 API (wails.biometric)

对应 [BiometricPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/Mobile/BiometricPlugin.cs)，调用 Android BiometricPrompt。

#### wails.biometric.checkAvailability()

查询设备是否支持生物识别。

```javascript
const result = await wails.biometric.checkAvailability();
// { available: true, type: "fingerprint" | "face" | "none" }
```

#### wails.biometric.authenticate(reason)

发起生物识别认证。

```javascript
const ok = await wails.biometric.authenticate("请验证指纹以登录");
```

### NFC API (wails.nfc)

对应 [NfcPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/Mobile/NfcPlugin.cs)。

#### wails.nfc.read()

开始 NFC 读取。

```javascript
const data = await wails.nfc.read();
// { id: string, payload: ArrayBuffer, tech: string }
```

#### wails.nfc.write(payload)

写入 NFC 标签。

```javascript
await wails.nfc.write(new Uint8Array([1, 2, 3]));
```

#### wails.nfc.cancel()

取消正在进行的 NFC 操作。

```javascript
await wails.nfc.cancel();
```

### 条码扫描 API (wails.barcode-scanner)

对应 [BarcodeScannerPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/Mobile/BarcodeScannerPlugin.cs)，调用 Android CameraX / ML Kit。

#### wails.barcode-scanner.scan(options?)

启动条码/二维码扫描。

```javascript
const result = await wails.barcode-scanner.scan({
  formats: ["qr_code", "ean_13"],
  prompt: "将条码对准取景框"
});
// { format: "qr_code", value: "https://example.com", raw: ArrayBuffer }
```

#### wails.barcode-scanner.cancel()

取消扫描。

```javascript
await wails.barcode-scanner.cancel();
```

### 触觉反馈 API (wails.haptics)

对应 [HapticsPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/Mobile/HapticsPlugin.cs)。

#### wails.haptics.vibrate(duration?)

触发振动。

```javascript
await wails.haptics.vibrate(200);  // 振动 200ms
```

#### wails.haptics.notification(type)

触发通知类型振动。

```javascript
await wails.haptics.notification("success");  // "success" | "warning" | "error"
```

#### wails.haptics.cancel()

取消正在进行的振动。

```javascript
await wails.haptics.cancel();
```

### Android 运行时 API (wails.device / wails.toast)

对应 [AndroidRuntimePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs)，提供 Android 平台专属的设备信息与 Toast 提示能力。对应 Wails v3 `messageprocessor_android.go` 中的 `androidDeviceInfo` / `androidShowToast`。

#### wails.device.info()

获取 Android 设备信息。通过 `Android.OS.Build` 读取硬件信息。

```javascript
const info = await wails.call('device.info', []);
// {
//   platform: "android",
//   manufacturer: "Google",
//   brand: "google",
//   model: "Pixel 7",
//   device: "panther",
//   version: "14",
//   sdkInt: 34
// }
```

#### wails.toast.show(message)

显示 Android Toast 提示（短时长）。通过 `Toast.MakeText` 显示。

```javascript
await wails.call('toast.show', [{ message: "已保存" }]);
```

**权限**：插件注册 `android-runtime:default` 权限集，包含 `android-runtime:allow-device-info` 与 `android-runtime:allow-toast`，需在 Capability 中声明后才能调用。

## 内部机制

以下三个全局函数由 [transport.template.js](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/Resources/transport.template.js) 提供，属于运行时内部实现，前端代码通常不直接调用，但理解其工作原理有助于高级用法与调试。

### window._wailsInvoke(method, params)

所有 `wails.*` API 调用的统一底层通道。返回 `Promise`，对应后端 `MessageProcessor`。

**工作流程**

1. 生成自增调用 ID `id`（从 1 开始）。
2. 构造消息 `{ id: String, type: method, payload: params }`。
3. 通过 `fetch("/wails/" + method, { method: "POST", ... })` 发送。
4. 在 `_pending[id]` 中保存 resolver。
5. 响应返回后解包嵌套的 `{ result: { result, error } }` 结构。

**签名**

```javascript
window._wailsInvoke(method: string, params: object): Promise<any>
```

**示例**

```javascript
// 直接调用底层通道（与 wails.window.setTitle 等价）
const result = await window._wailsInvoke("window.setTitle", { title: "新标题" });
```

### window._wailsOnEvent(eventName, callback)

注册本地事件回调。返回取消订阅函数。`wails.events.on` 即直接调用此函数。

**签名**

```javascript
window._wailsOnEvent(
  eventName: string,
  callback: (data: any) => void
): () => void
```

**示例**

```javascript
const unsubscribe = window._wailsOnEvent("myCustomEvent", (data) => {
  console.log("收到事件：", data);
});
// 取消订阅
unsubscribe();
```

### window._wailsEmitEvent(eventName, data)

触发本地事件回调。**通常由后端调用**，前端一般不直接调用。前端若调用此函数，会同步触发所有注册到 `eventName` 的本地回调。

**签名**

```javascript
window._wailsEmitEvent(eventName: string, data: any): void
```

**示例**

```javascript
// 前端手动触发本地事件（仅本地，不会发送到后端）
window._wailsEmitEvent("myCustomEvent", { foo: "bar" });
```

> **注意**：若需将事件广播到其他窗口或让后端处理，应使用 `wails.events.emit()` 而非 `_wailsEmitEvent()`。

## 参考资源

- 后端生成入口：[RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs)
- 生成选项：[RuntimeOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeOptions.cs)
- 传输层模板：[transport.template.js](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/Resources/transport.template.js)
- 运行时模板：[runtime.template.js](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/Resources/runtime.template.js)
- 桌面运行时：[DesktopRuntime.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/DesktopRuntime.cs)
- Server 模式运行时：[ServerRuntime.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/ServerRuntime.cs)
- 项目架构文档：[architecture.md](file:///f:/Code/Dotnet/Wails.Net/docs/architecture.md)
- 传输层与 IPC：[transport-and-ipc.md](file:///f:/Code/Dotnet/Wails.Net/docs/architecture/transport-and-ipc.md)
