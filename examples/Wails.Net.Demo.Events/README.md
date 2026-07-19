# Wails.Net Demo - Events

演示 Wails.Net 事件系统的前后端双向通信能力，包括后端 emit、前端 on、前端 emit、后端 On、取消订阅与一次性订阅。

## 功能

- 后端 emit `demo:tick` 事件（每秒一次）与 `demo:notification` 事件
- 前端订阅 `wails.events.on` 接收后端事件
- 前端 emit `frontend:event`，后端 `app.Events.On` 订阅并回发 `demo:echo`
- 取消订阅（调用 `on` 返回的取消函数）
- 一次性订阅（订阅后在回调中立即取消）
- 实时事件日志

## 运行

```bash
dotnet run --project examples/Wails.Net.Demo.Events/Wails.Net.Demo.Events.csproj
```
