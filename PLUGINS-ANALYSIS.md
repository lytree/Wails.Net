# Wails.Net 插件全景分析报告

> 分析日期：2026-08-07 ｜ 数据来源：`src/Wails.Net.Application/Plugins/` 源码 + `docs/plugins.md` + `docs/architecture/plugin-system.md`
> 结论先行：**当前共 47 个内置插件（39 桌面 + 7 移动端 + 1 Android 专属），注册约 300 个前端可调用命令**，全部基于 `IPlugin` 契约按需启用。

---

## 1. 总体概况

| 维度 | 数值 |
|------|------|
| 插件总数 | **47**（39 桌面 + 7 移动端 + 1 Android 专属） |
| 命令总数 | ≈300（桌面 282 + 移动端 19 + Android 专属 2） |
| 代码位置 | `src/Wails.Net.Application/Plugins/BuiltIn/`（桌面）、`Plugins/Mobile/`（移动端）、`src/Wails.Net.Application.Android/Mobile/`（Android 专属） |
| 启用方式 | 按需 `builder.UsePlugin<T>()`，**无默认全量注册** |
| 前端访问 | `wails.call('<plugin>.<action>', [args])`（vite 项目经 `@wails-net/runtime` 类型化封装） |
| 插件契约 | `IPlugin`：`Name` + `ConfigureServices`（注册 DI）+ `Configure`（注册命令） |

### 平台支持总览

| 类别 | 插件数 | 支持平台 | 说明 |
|------|--------|---------|------|
| 桌面通用插件 | 39 | Windows / Linux / macOS | 核心库 `Wails.Net.Application`（net10.0），平台能力经抽象接口注入（Clipboard / SystemTray / Menu / Browser / Autostart 等） |
| 移动端插件 | 7 | Android（仅 `net10.0-android36.0`） | 位于 `Plugins.Mobile` 命名空间，Windows/Linux 上调用返回 `PlatformNotSupportedException` |
| Android 专属 | 1 | Android | `AndroidRuntimePlugin`（`device.*` / `toast.*`） |
| 特殊限制 | — | Keychain 仅 Windows/macOS | Linux 无 `IPlatformKeychain` 实现 |

> 平台实现分布：Windows 侧 17 个实现文件（Clipboard/Keychain/SystemTray/Menu/Taskbar/Browser/Autostart/Environment/KeyBinding/Theme/WebviewWindow/ContextMenu/SystemEventWatcher）；Linux 侧 14 个（无 Keychain）。macOS 为骨架实现（`Wails.Net.Application.MacOS`）。

---

## 2. 桌面插件（39 个，`Plugins/BuiltIn/`）

### 2.1 系统 / 应用类（10 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `ApplicationPlugin` | `application` | 12 | 应用生命周期与信息：退出/隐藏/显示、名称/版本/描述、图标、暗色模式、强调色、屏幕查询、关于对话框 | `quit` `hide` `show` `getName` `getVersion` `setIcon` `isDarkMode` `getAccentColor` `getPrimaryScreen` `getScreens` `showAboutDialog` |
| `AppInfoPlugin` | `app` | 4 | 应用元信息（精简版） | `getName` `getVersion` `getDescription` `getTauriVersion` |
| `OsInfoPlugin` | `os` / `system` | 13 | 操作系统信息（双前缀兼容）：平台/架构/主机名/语言/版本/类型/时区 | `platform` `arch` `hostname` `locale` `version` `type` `timezone` |
| `ProcessPlugin` | `process` | 4 | 进程控制 | `exit` `restart` `relaunch` `getPid` |
| `PowerManagementPlugin` | `power-management` | 3 | 电源管理：阻止休眠 | `requestWakeLock` `releaseWakeLock` `isWakeLockHeld` |
| `AutostartPlugin` | `autostart` | 3 | 开机自启动 | `enable` `disable` `isEnabled` |
| `LogPlugin` | `log` | 7 | 结构化日志（与 `ILogger` 桥接，支持 P1-3 浏览器 console 双向转发） | `debug` `info` `warn` `error` `trace` `log` `logStructured` |
| `LocalizationPlugin` | `localization` | 5 | 国际化：语言切换与翻译加载 | `setLocale` `getLocale` `t` `registerTranslations` `getAvailableLocales` |
| `LocalhostPlugin` | `localhost` | 8 | 本地回环 HTTP 服务器 | `start` `stop` `getUrl` `isRunning` `setRoot` `addRoute` `removeRoute` `listRoutes` |
| `CliPlugin` | `cli` | 1 | CLI 参数匹配 | `getMatches` |

### 2.2 文件类（5 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `FileSystemPlugin` | `filesystem`（兼容 `fs.*`） | 21 | 文件读写/目录操作/元数据；**内置沙箱路径穿越防护**（构造注入 `sandboxRoot`）；短名 + 前端 API 名双套注册 | `read` `write` `readTextFile` `writeBinaryFile` `copy` `rename` `stat` `mkdir` `rmdir` `readDir` `readDirRecursive` |
| `FsWatchPlugin` | `fs-watch` | 5 | 文件系统变更监听 | `watch` `unwatch` `unwatchAll` `listWatches` `isWatching` |
| `PathPlugin` | `path` | 11 | 系统特殊目录查询 | `appDataDir` `appConfigDir` `appLogDir` `appCacheDir` `downloadDir` `documentDir` `homeDir` `tempDir` `configDir` `dataDir` `runtimeDir` |
| `DialogPlugin` | `dialog` | 7 | 原生对话框 | `message` `warning` `error` `question` `openFile` `saveFile` `openMultipleFiles` |
| `FileAssociationPlugin` | `file-association` | 3 | 文件扩展名关联注册 | `register` `unregister` `getRegistered` |

### 2.3 网络类（5 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `HttpPlugin` | `http` | 5 | 异步 HTTP 客户端 | `fetch` `get` `post` `put` `delete` |
| `WebSocketPlugin` | `websocket` | 5 | WebSocket 连接管理 | `connect` `send` `sendBinary` `close` `getState` |
| `UploadPlugin` | `upload` | 4 | 文件上传/下载（含进度） | `download` `upload` `downloadWithProgress` `uploadWithProgress` |
| `CookiePlugin` | `cookie` | 4 | WebView Cookie 管理 | `get` `set` `delete` `clear` |
| `DeepLinkPlugin` | `deep-link` | 3 | 自定义协议深度链接 | `register` `unregister` `getCurrent` |

### 2.4 窗口类（5 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `WindowPlugin` | `window` | **54** | 窗口全操作（命令量最大的插件）：标题/尺寸/位置/状态/全屏/置顶/DevTools/缩放/导航/打印/PDF/JS 执行/CSS 注入/透明度/任务栏/角标/查询 | `setTitle` `setSize` `setPosition` `minimize` `maximize` `setAlwaysOnTop` `setFullscreen` `setFrameless` `openDevTools` `setZoom` `goBack` `reload` `setURL` `setHTML` `print` `printToPDF` `execJS` `injectCSS` `setOpacity` `getSize` `isFullscreen` 等 |
| `WindowsPlugin` | `windows` | 5 | 多窗口管理 | `getCurrent` `getAll` `getByName` `getById` `emit` |
| `WindowStatePlugin` | `window-state` | 3 | 窗口状态持久化 | 保存/恢复/清除 |
| `ScreenPlugin` | `screen` | 2 | 屏幕查询 | `getAll` `getPrimary` |
| `PositionerPlugin` | `positioner` | 5 | 窗口定位（9 种方位） | `move` `center` `moveRelativeTo` `moveToCursor` `getPosition` |

### 2.5 数据类（5 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `SqlPlugin` | `sqlite` | 10 | SQLite 数据库操作（Microsoft.Data.Sqlite） | `execute` `query` `scalar` `createTable` `dropTable` `getTables` `insert` `update` `delete` `select` |
| `StorePlugin` | `store` | 7 | 键值持久化（Memory / JsonFile 双后端） | `get` `set` `delete` `has` `keys` `clear` `watch` |
| `StrongholdPlugin` | `stronghold` | 8 | 加密敏感数据存储 | `unlock` `lock` `saveSecret` `getSecret` `deleteSecret` `listKeys` `isUnlocked` `changePassword` |
| `ClipboardPlugin` | `clipboard` | 7 | 剪贴板读写（文本/HTML/图片） | `getText` `setText` `getHTML` `setHTML` `getImage` `setImage` `clear` |
| `KeychainPlugin` | `keychain` | 3 | 系统钥匙串（**仅 Windows/macOS**，Linux 无实现） | `setPassword` `getPassword` `deletePassword` |

### 2.6 UI / 快捷方式类（7 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `GlobalShortcutPlugin` | `globalshortcut` | 4 | 全局快捷键 | `register` `unregister` `unregisterAll` `isRegistered` |
| `MenuPlugin` | `menu` | 9 | 应用菜单/上下文菜单/弹出菜单 + MenuRole 角色菜单（P2-1：`addRoleItem`/标准菜单） | `setApplicationMenu` `getApplicationMenu` `setContextMenu` `popup` `updateMenuItem` `addRoleItem` `addStandardEditMenu` `addStandardWindowMenu` `addStandardHelpMenu` |
| `TrayPlugin` | `tray` | 8 | 系统托盘（唯一在 ConfigureServices 注册 DI 服务 `TrayHolder` 的插件） | `setIcon` `setLabel` `setMenu` `setTooltip` `destroy` `show` `hide` `isVisible` |
| `NotificationPlugin` | `notification` | 6 | 系统通知（双别名：`isPermissionGranted`/`hasPermission`） | `show` `showWithId` `cancel` `requestPermission` `isPermissionGranted` `hasPermission` |
| `OpenerPlugin` | `opener` | 5 | 安全打开 URL/文件（**带白名单校验**） | `openUrl` `openPath` `revealInFolder` `isUrlAllowed` `verifyUrl` |
| `DpiScalePlugin` | `dpi-scale` | 3 | DPI 缩放查询与设置 | `getScaleFactor` `setZoomFactor` `reset` |
| `ShellPlugin` | `shell` | 4 | 命令执行（**带白名单**） | `execute` `executeAsync` `open` `openUrl` |

### 2.7 更新 / 权限类（2 个）

| 插件 | 前缀 | 命令数 | 功能 | 主要命令 |
|------|------|-------|------|---------|
| `UpdaterPlugin` | `updater` | 4 | 自动更新（P1-8 多 Provider：Http / GitHub / GitLab / 自定义 `IUpdateProvider`；minisign 签名校验） | `check` `download` `install` `checkAndDownload` |
| `PersistedScopePlugin` | `persisted-scope` | 7 | 持久化文件范围管理（对齐 Tauri v2 权限模型） | `addPath` `removePath` `listPaths` `clear` `isAllowed` `save` `load` |

---

## 3. 移动端插件（7 个，`Plugins/Mobile/`，仅 Android）

> 位于 `Wails.Net.Application.Plugins.Mobile` 命名空间，仅在 `net10.0-android36.0` 目标下可用；Windows/Linux 上调用返回 `PlatformNotSupportedException`。平台后端在 `src/Wails.Net.Application.Android/Mobile/`，通过委托注入解耦 Activity 生命周期。

| 插件 | 前缀 | 命令 | 底层 Android API |
|------|------|------|------------------|
| `BiometricPlugin` | `biometric` | `checkAvailability` `authenticate` | `BiometricManager`（API 29+）/ `BiometricPrompt`（API 28+） |
| `NfcPlugin` | `nfc` | `read` `write` `cancel` | `NfcAdapter` + `Activity.OnNewIntent` |
| `BarcodeScannerPlugin` | `barcode-scanner` | `scan` `cancel` | `Intent.ActionGetContent` + 第三方扫码应用 |
| `HapticsPlugin` | `haptics` | `vibrate` `cancel` `notification` | `Vibrator`（API 26+ 用 `VibrationEffect`） |
| `CameraPlugin` | `camera` | `checkAvailability` `capture` `cancel` | `MediaStore.ACTION_IMAGE_CAPTURE` |
| `GeolocationPlugin` | `geolocation` | `checkAvailability` `getCurrentPosition` `watchPosition` `clearWatch` | `LocationManager`（GPS/网络） |
| `PermissionsPlugin` | `permissions` | `check` `request` | `Context.CheckSelfPermission` / `RequestPermissions` |

## 4. Android 专属插件（1 个）

| 插件 | 前缀 | 命令 | 底层 API | 说明 |
|------|------|------|---------|------|
| `AndroidRuntimePlugin` | `device` / `toast` | `device.info`（设备信息）`toast.show`（Toast） | `Android.OS.Build` + `Toast.MakeText` | 位于 `Wails.Net.Application.Android.Mobile`，Android 平台运行时能力 |

---

## 5. 使用说明（通用模式）

### 5.1 启用插件（后端 Program.cs）

```csharp
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 方式一：泛型（要求无参构造函数）
builder.UsePlugin<ClipboardPlugin>();
builder.UsePlugin<DialogPlugin>();

// 方式二：实例（可注入构造参数，如 FileSystemPlugin 的沙箱根）
builder.UsePlugin(new FileSystemPlugin(sandboxRoot: appDataPath));

// 方式三：程序集自动发现
builder.UsePluginsFromAssembly();
```

典型组合（见 `examples/Wails.Net.Demo/Program.cs`）：`WindowPlugin` + `WindowsPlugin` + `ApplicationPlugin` + `TrayPlugin` + `MenuPlugin` + `ScreenPlugin` + `LogPlugin` + `ClipboardPlugin` + `DialogPlugin` + `NotificationPlugin` + `OsInfoPlugin` + `StorePlugin` + `PathPlugin` + `AppInfoPlugin` + `UpdaterPlugin`。

### 5.2 前端调用（vite 项目）

```typescript
import { wails } from "@wails-net/runtime";

// 剪贴板
await wails.call('clipboard.setText', ['Hello']);          // 复制文本
const text = await wails.call('clipboard.getText', []);      // 读取文本

// 通知
await wails.call('notification.show', [{ title: '应用', body: '完成' }]);

// 对话框
const filePath = await wails.call('dialog.openFile', [{
  title: '选择文件', filters: [{ name: '图片', extensions: ['png', 'jpg'] }]
}]);

// 键值存储
await wails.call('store.set', ['username', '张三']);
const username = await wails.call('store.get', ['username']);

// 日志
await wails.call('log.info', ['应用启动']);

// 窗口操作
await wails.call('window.setTitle', [{ title: '新标题' }]);
```

> 无构建链静态 demo：`import { wails } from "./wails-runtime/index.js";`

### 5.3 插件配置（appsettings.json）

```json
{
  "Plugins": {
    "MyPlugin": { "MaxRetries": 3, "Timeout": "00:00:30" }
  }
}
```

插件内通过 `context.Configuration.GetSection("Plugins:MyPlugin")` 读取，或 `services.AddOptions<T>().Bind(...)` 强类型绑定。

### 5.4 Updater 多 Provider 示例

```csharp
builder.Services.AddSingleton<UpdaterService>(sp =>
{
    var service = new UpdaterService { CurrentVersion = "1.0.0" };
    service.AddProvider(new GitHubUpdateProvider("owner/repo"));   // GitHub Releases API
    service.AddProvider(new HttpUpdateProvider("https://example.com/update.json"));
    return service;
});
builder.UsePlugin<UpdaterPlugin>();
```

---

## 6. 注意事项与已知差异

1. **文档版本差异**：`docs/plugins.md`（47 插件）与源码一致；`docs/architecture/plugin-system.md` 仍写 42 插件/271 命令（2026-07-20 快照，未包含 Keychain/Cli 等新增插件），引用时注意。
2. **Linux 无 Keychain**：`WindowsKeychain` 是唯一 `IPlatformKeychain` 实现，`KeychainPlugin` 在 Linux 上不可用（macOS 骨架待实现）。
3. **移动端插件平台限制**：7 个移动端插件在 Windows/Linux 上调用抛 `PlatformNotSupportedException`；Android 上桌面插件不可用。
4. **安全设计**：`FileSystemPlugin`（沙箱）、`OpenerPlugin`（URL 白名单）、`ShellPlugin`（命令白名单）、`PersistedScopePlugin`（路径作用域）是权限控制的关键插件，生产环境务必配置。
5. **菜单角色**：`MenuPlugin` 的 macOS 专属角色（Hide/Quit/About 等）在其他平台静默 no-op；Android 无 `IMenuImpl`。
6. **双包发布未落地**：插件双包（NuGet `Wails.Net.Plugins.*` + npm `@wails-net/plugin-*`）模型已决策（2026-08-07），当前插件仍为框架内置，尚未拆分为独立可安装包（M1 Updater 示范插件待做）。
7. **命令别名**：部分插件注册双名（`fs.*` / `filesystem.*`、`os.*` / `system.*`、`notification.isPermissionGranted` / `hasPermission`），旧前端代码无需迁移。
