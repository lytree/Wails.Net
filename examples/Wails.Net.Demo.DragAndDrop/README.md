# Wails.Net.Demo.DragAndDrop

演示 Wails.Net 文件拖放能力。

## 特性

- 平台层在窗口收到文件拖放事件后广播 `KnownEvents.WindowFileDropped`（`wails:window:file:dropped`）事件
- 前端订阅事件后将路径传给 `FileDropService.RecordDrop` 绑定方法持久化
- `FileDropService` 提供历史查询、清空、统计、文件预览等绑定方法
- 通过 `window.setFileDropEnabled` 插件命令运行时启用/禁用拖放

## 关键 API

| API | 说明 |
|-----|------|
| `ApplicationOptions.DragAndDrop` | 应用级启用/禁用拖放（默认 true） |
| `WebviewWindow.SetFileDropEnabled(bool)` | 运行时启用/禁用单窗口拖放 |
| `window.setFileDropEnabled` | 前端插件命令，等价于 `SetFileDropEnabled` |
| `KnownEvents.WindowFileDropped` | 文件拖放事件名（`wails:window:file:dropped`） |
| `FileDropService.RecordDrop / GetHistory / ClearHistory / GetStats / ReadFilePreview` | 绑定方法 |

## 事件 Payload

平台层广播的 `wails:window:file:dropped` 事件 payload 为**文件路径字符串数组**：
```json
["C:\\path\\to\\file1.txt", "C:\\path\\to\\file2.png"]
```

前端通过 `wails.events.on('wails:window:file:dropped', callback)` 订阅。

## 运行

```bash
# Windows
dotnet run --project examples/Wails.Net.Demo.DragAndDrop/Wails.Net.Demo.DragAndDrop.csproj -f net10.0-windows10.0.19041.0

# Linux
dotnet run --project examples/Wails.Net.Demo.DragAndDrop/Wails.Net.Demo.DragAndDrop.csproj -f net10.0
```

## 交互测试

1. 启动后从资源管理器拖入若干文件到窗口
2. 「拖放区域」实时显示本次拖入的文件路径
3. 「拖放历史与统计」展示累计记录、扩展名分布、总大小
4. 点击「禁用拖放」后再次拖入文件，事件不再触发
5. 点击「启用拖放」恢复拖放能力
