# Wails.Net.Demo.Updater

演示 Wails.Net 应用更新（Updater）能力，重点是 **P1-8 多 Provider 链式检查机制**。

## 特性

- 注册三个自定义 `IUpdateProvider`：
  - `mock-stable`：返回 1.0.0（无更新）
  - `mock-beta`：返回 1.1.0（有更新）
  - `mock-rc`：返回 1.0.5（有更新）
- `UpdaterService` 按 Provider 注册顺序依次尝试，首个返回非 null 清单者胜出
- 启用 `UpdaterPlugin`，前端可通过 `updater.check / updater.download / updater.install` 命令调用
- `UpdaterService.CheckForUpdatesAsync` 在发现新版本时广播 `wails:updater:update-available` 事件
- `UpdaterDemoService` 通过 `[Binding]` 方法暴露：切换 Provider 链、设置当前版本、触发检查/下载

## 关键 API

| API | 说明 |
|-----|------|
| `UpdaterService.AddProvider / ClearProviders / Providers` | 多 Provider 链管理 |
| `UpdaterService.CurrentVersion` | 当前应用版本号 |
| `UpdaterService.CheckForUpdatesAsync` | 按链顺序检查，返回胜出 Provider 的清单 |
| `UpdaterService.DownloadUpdateAsync(manifest)` | 下载更新包 |
| `UpdaterService.InstallUpdateAsync(path, manifest)` | 安装更新包 |
| `IUpdateProvider.Name / CheckAsync` | Provider 接口 |
| `UpdateManifest` | 更新清单（Version / ReleaseNotes / DownloadURL / Checksum / Signature / ProviderName） |
| `updater.check / updater.download / updater.install` | 前端插件命令 |
| `UpdaterDemoService.SwitchProviderChain / SetCurrentVersion / CheckForUpdatesAsync / DownloadUpdateAsync / GetProviders / GetHistory` | 绑定方法 |

## 更新事件

| 事件名 | 说明 |
|--------|------|
| `wails:updater:update-available` | 发现新版本 |
| `wails:updater:no-update` | 已是最新版本 |
| `wails:updater:download-started` | 下载开始 |
| `wails:updater:download-progress` | 下载进度 |
| `wails:updater:download-complete` | 下载完成 |
| `wails:updater:download-error` | 下载错误 |
| `wails:updater:install-started` | 安装开始 |
| `wails:updater:install-complete` | 安装完成 |
| `wails:updater:install-error` | 安装错误 |
| `wails:updater:update-applied` | 更新已应用 |

## 运行

```bash
# Windows
dotnet run --project examples/Wails.Net.Demo.Updater/Wails.Net.Demo.Updater.csproj -f net10.0-windows10.0.19041.0

# Linux
dotnet run --project examples/Wails.Net.Demo.Updater/Wails.Net.Demo.Updater.csproj -f net10.0
```

## 交互测试

1. 默认 Provider 链为 `stable`（返回 1.0.0，无更新）
2. 切换到 `beta` 预设，点击「检查更新」→ 显示远端 1.1.0，有更新
3. 切换到 `chain` 预设（stable → rc → beta），由于 stable 首先返回非 null，胜出者为 stable（无更新）
4. 修改「当前版本」为 1.2.0，切换到 `beta`，点击「检查更新」→ 远端 1.1.0 < 1.2.0，无更新
5. 「下载更新」按钮调用 `DownloadUpdateAsync`，本 Demo URL 不可达，会触发 `download-error` 事件

## 实际应用提示

实际应用中应替换为真实 Provider：
- `HttpUpdateProvider`：从自建 HTTP 服务获取清单
- `GitHubUpdateProvider`：从 GitHub Releases 获取
- `GitLabUpdateProvider`：从 GitLab Releases 获取

并配置 `UpdaterConfig`：
- `UpdateURL`：清单 URL
- `Headers`：自定义请求头
- `AutoDownload` / `AutoInstall`：自动下载/安装
- `VerifySignature` / `TrustedPublicKey`：minisign 签名验证
