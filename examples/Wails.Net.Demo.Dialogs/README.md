# Wails.Net Demo - Dialogs

演示内置 DialogPlugin 提供的各类原生对话框，并通过绑定方法记录用户操作历史。

## 功能

- 消息对话框（`dialog.message` - 信息提示）
- 警告对话框（`dialog.warning`）
- 错误对话框（`dialog.error`）
- 询问对话框（`dialog.question` - 返回 1=Yes / 0=No）
- 打开文件对话框（`dialog.openFile` - 支持 filters）
- 保存文件对话框（`dialog.saveFile`）
- 多文件选择对话框（`dialog.openMultipleFiles`）
- 操作历史（`DialogHistoryService.GetHistory` / `RecordAction` / `ClearHistory`）

## 运行

```bash
dotnet run --project examples/Wails.Net.Demo.Dialogs/Wails.Net.Demo.Dialogs.csproj
```
