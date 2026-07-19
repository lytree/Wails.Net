# Wails.Net Demo - Clipboard

演示内置 ClipboardPlugin 与自定义绑定方法的配合使用，包括复制、粘贴、计数统计与历史记录。

## 功能

- 复制文本（`clipboard.setText` 插件命令）
- 粘贴文本（`clipboard.getText` 插件命令）
- 复制次数统计（`ClipboardStatsService.GetCopyCount` 绑定方法）
- 重置计数（`ClipboardStatsService.ResetCount` 绑定方法）
- 最近 5 次复制记录（前端维护）

## 运行

```bash
dotnet run --project examples/Wails.Net.Demo.Clipboard/Wails.Net.Demo.Clipboard.csproj
```
