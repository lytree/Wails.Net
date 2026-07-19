# Wails.Net.Demo.SystemTray

演示 Wails.Net 系统托盘（SystemTray）能力。

## 特性

- 通过 `ISystemTrayManager.CreateSystemTray` 创建托盘实例
- 使用 `Menu` 构建右键菜单（显示窗口 / 隐藏窗口 / 发送通知 / 退出）
- 订阅 `ISystemTrayImpl.OnTrayClick` 处理托盘左键点击事件
- 通过框架 `NotificationService` 发送托盘通知
- 通过 `TrayPlugin` 暴露 `tray.*` 命令到前端
- `TrayLogService` 维护事件历史并广播 `tray:clicked` / `tray:event` 事件到前端

## 关键 API

| API | 说明 |
|-----|------|
| `app.SystemTrayManager` | 获取系统托盘管理器（`ISystemTrayManager`） |
| `manager.CreateSystemTray(byte[] icon)` | 创建托盘实例 |
| `manager.SetTooltip / SetLabel / SetMenu / Show / Hide` | 设置托盘属性与可见性 |
| `tray.OnTrayClick` | 托盘左键点击事件回调 |
| `TrayHolder.ActiveTray` | 持有活动托盘实例，供 `TrayPlugin` 命令操作 |
| `tray.setTooltip / setLabel / show / hide / isVisible` | 前端可调用的插件命令 |
| `TrayLogService.GetTrayEvents / ClearEvents / SendTrayNotification` | 绑定方法 |

## 跨平台图标

本 Demo 使用 1x1 PNG 字节数组（内联在 `Program.cs` 中）作为托盘图标，避免依赖 `System.Drawing.Common`（Windows-only）。

实际应用中可从嵌入资源或文件加载真实图标。

## 运行

```bash
# Windows
dotnet run --project examples/Wails.Net.Demo.SystemTray/Wails.Net.Demo.SystemTray.csproj -f net10.0-windows10.0.19041.0

# Linux
dotnet run --project examples/Wails.Net.Demo.SystemTray/Wails.Net.Demo.SystemTray.csproj -f net10.0
```

## 交互测试

1. 启动后任务栏通知区域出现托盘图标
2. 左键点击托盘 → 主窗口显示并聚焦，事件历史新增一条 `click` 记录
3. 右键点击托盘 → 弹出菜单
   - 「显示窗口」/「隐藏窗口」控制主窗口可见性
   - 「发送通知」调用框架 `NotificationService` 发送系统通知
   - 「退出」关闭应用
4. 前端按钮调用 `tray.*` 命令操作托盘属性
5. 前端调用 `TrayLogService.SendTrayNotification` 绑定方法发送通知
