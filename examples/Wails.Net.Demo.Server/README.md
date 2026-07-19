# Wails.Net.Demo.Server — 无 GUI 服务模式

## 功能演示

使用 `ServerPlatformApp`（无 GUI 的 no-op 实现）在容器化或无头环境运行 Wails.Net 应用。

- 启动 Server 模式（不创建窗口）
- 接收 HTTP 请求（通过 AssetServer + Service Route）
- 后台定时任务（`BackgroundTaskService` 使用 `Timer` 模拟）
- 优雅关闭（Ctrl+C 触发 Host 停止）

## 与普通 Demo 的区别

- 不调用 `app.CreateWebviewWindow` —— 没有任何 GUI 窗口
- 通过 `builder.UsePlatform<ServerPlatformApp>()` 显式指定 Server 平台
- 后台任务由 `[Binding]` 方法暴露：`GetStatus` / `GetProcessedCount` / `StartProcessing` / `StopProcessing`
- HTTP API 通过 `app.RegisterService(handler, new ServiceOptions { Route = "/api/status" })` 挂载

## 运行

```bash
dotnet run --project examples\Wails.Net.Demo.Server\Wails.Net.Demo.Server.csproj
```

启动后控制台输出端口信息，另开终端测试：

```bash
curl http://localhost:<port>/api/status
curl http://localhost:<port>/index.html
```

## 文件结构

```
Wails.Net.Demo.Server/
├── Program.cs                     # 入口：UsePlatform<ServerPlatformApp> + RegisterService
├── Services/
│   └── BackgroundTaskService.cs   # [Binding] 方法 + Timer 后台任务
├── frontend/
│   ├── index.html                 # 静态状态页（无 JS 交互）
│   └── styles.css
├── appsettings.json
├── app.manifest
└── Wails.Net.Demo.Server.csproj
```
