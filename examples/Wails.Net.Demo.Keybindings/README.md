# Wails.Net.Demo.Keybindings

演示 Wails.Net 全局快捷键（KeyBinding）能力。

## 特性

- 通过 `Application.KeyBindingManager.RegisterKeyBinding` 注册后端全局热键
- 启用 `GlobalShortcutPlugin`，前端可通过 `globalshortcut.register / unregister` 命令动态注册
- 后端触发时广播 `keybinding:pressed` 事件，前端触发时广播 `globalshortcut:pressed` 事件
- `KeybindingLogService` 记录所有触发历史，支持统计与清空

## 关键 API

| API | 说明 |
|-----|------|
| `Application.KeyBindingManager` | 全局快捷键管理器（`IKeyBindingManager`） |
| `manager.RegisterKeyBinding(accelerator, callback)` | 注册后端全局热键 |
| `manager.UnregisterKeyBinding(accelerator)` | 注销热键 |
| `globalshortcut.register / unregister / isRegistered` | 前端插件命令 |
| `keybinding:pressed` 事件 | 后端热键触发时广播 |
| `globalshortcut:pressed` 事件 | 前端注册的热键触发时广播 |
| `KeybindingLogService.RecordFrontendPress / GetHistory / ClearHistory / GetCountByAccelerator` | 绑定方法 |

## 加速键格式

支持以下格式（用 `+` 分隔）：

- 修饰键：`Ctrl` / `Control`、`Alt` / `Option`、`Shift`、`Win` / `Super` / `Meta`
- 主键：`A-Z`、`0-9`、`F1-F24`、`Space`、`Enter`、`Tab`、`Esc`、`Delete`、`Home`、`End`、`PageUp`、`PageDown`、`Left`、`Up`、`Right`、`Down`

示例：`Ctrl+C`、`Alt+F4`、`Ctrl+Shift+P`、`F9`、`Win+D`

## 平台实现

- **Windows**：`Win32KeyBindingManager` 通过 CsWin32 调用 `RegisterHotKey` / `UnregisterHotKey` API
- **Linux**：通过 GTK Accelerator 解析注册

## 运行

```bash
# Windows
dotnet run --project examples/Wails.Net.Demo.Keybindings/Wails.Net.Demo.Keybindings.csproj -f net10.0-windows10.0.19041.0

# Linux
dotnet run --project examples/Wails.Net.Demo.Keybindings/Wails.Net.Demo.Keybindings.csproj -f net10.0
```

## 交互测试

1. 启动后按下 `Ctrl+Alt+T` 触发测试事件，历史新增一条 `backend` 记录
2. 按下 `Ctrl+Alt+H` 隐藏窗口，再按 `Ctrl+Alt+S` 显示窗口
3. 按下 `F9` 触发纯功能键热键
4. 在前端输入 `Ctrl+Shift+P` 点击「注册」，按下该组合键后历史新增一条 `frontend` 记录
5. 点击「查询统计」查看各快捷键触发次数分布
