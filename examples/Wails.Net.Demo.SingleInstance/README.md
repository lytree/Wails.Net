# Wails.Net.Demo.SingleInstance

演示 Wails.Net 单实例锁（Single Instance）能力。

## 特性

- 通过 `ApplicationOptions.SingleInstance=true` 启用单实例模式
- 通过 `ApplicationOptions.SingleInstanceUniqueID` 自定义唯一标识（默认回退到应用名）
- 平台层调用 `IPlatformApp.AcquireSingleInstanceLock(uniqueId)` 获取锁
- 后续实例获取锁失败时，调用 `NotifySingleInstance(args)` 通知首实例后退出
- 首实例通过 `Application.OnSecondInstanceLaunch(Action<string[]>)` 注册回调
- 框架同时广播 `KnownEvents.SecondInstanceLaunched`（`wails:second-instance:launched`）事件到前端
- `SingleInstanceLogService` 通过 `[Binding]` 暴露历史查询、进程信息、统计

## 关键 API

| API | 说明 |
|-----|------|
| `ApplicationOptions.SingleInstance` | 启用单实例模式 |
| `ApplicationOptions.SingleInstanceUniqueID` | 单实例锁唯一标识 |
| `ApplicationOptions.SingleInstanceExitCode` | 非首实例退出码（默认 1） |
| `IPlatformApp.AcquireSingleInstanceLock(uniqueId)` | 平台层获取锁 |
| `IPlatformApp.NotifySingleInstance(args)` | 平台层通知首实例 |
| `Application.OnSecondInstanceLaunch(callback)` | 注册二次启动回调 |
| `Application.RaiseSecondInstanceLaunched(args)` | 触发事件与回调（由平台调用） |
| `KnownEvents.SecondInstanceLaunched` | 事件名（`wails:second-instance:launched`） |
| `SingleInstanceLogService.GetHistory / ClearHistory / GetCurrentProcessInfo / GetStats` | 绑定方法 |

## 配置

`appsettings.json`：
```json
{
  "Wails": {
    "ApplicationName": "Wails.Net Demo - SingleInstance",
    "SingleInstance": true,
    "SingleInstanceUniqueID": "wails-net-demo-singleinstance-7f3a9c2d"
  }
}
```

## 运行

```bash
# Windows
dotnet run --project examples/Wails.Net.Demo.SingleInstance/Wails.Net.Demo.SingleInstance.csproj -f net10.0-windows10.0.19041.0

# Linux
dotnet run --project examples/Wails.Net.Demo.SingleInstance/Wails.Net.Demo.SingleInstance.csproj -f net10.0
```

## 交互测试

1. 启动首个实例 → 主窗口显示，历史新增一条 `first` 记录
2. 在另一个终端运行同一可执行文件（可附加命令行参数如 `--foo bar`）
3. 第二个进程立即退出（退出码默认 1）
4. 首实例窗口被聚焦，历史新增一条 `second` 记录，包含第二个进程的命令行参数
5. 前端通过 `wails:second-instance:launched` 事件订阅实时刷新

## 平台实现

- **Windows**：`WindowsPlatformApp` 使用命名互斥体（`Mutex`）实现单实例锁，通过命名管道传递二次启动参数
- **Linux**：`LinuxPlatformApp` 使用 Unix 域套接字（或锁文件）实现，通过 socket 传递参数
