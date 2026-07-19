# Wails.Net Demo - Notifications

演示内置 NotificationPlugin 与框架 NotificationService 的系统通知能力，包括立即发送、延迟发送与历史记录。

## 功能

- 立即发送通知（`NotificationService.SendNotification` 绑定方法，封装框架通知服务）
- 延迟发送通知（`NotificationService.ScheduleNotification` 绑定方法，后端 Task.Delay 等待后发送）
- 通知历史（`NotificationService.GetHistory` / `ClearHistory` 绑定方法）
- 启用 NotificationPlugin，前端也可直接调用 `notification.show` 命令

## 运行

```bash
dotnet run --project examples/Wails.Net.Demo.Notifications/Wails.Net.Demo.Notifications.csproj
```
