# Wails.Net DevRelease Demo

> 演示 Wails.Net 项目的 **Debug / Release 模式** 与 **前后端项目分布**，
> 对应 Tauri v2 / Wails v3 的运行模式。

## 项目结构

```
Wails.Net.Demo.DevRelease/
├── Program.cs                              # 后端入口（C# 顶层语句，含 Debug/Release 模式判断）
├── Wails.Net.Demo.DevRelease.csproj        # 后端 .NET 项目文件
├── app.manifest                            # Windows 应用清单（DPI 感知）
├── appsettings.json                        # .NET 运行时配置
├── wails.json                              # Wails.Net 项目元信息（前后端配置、bundle、钩子）
├── Properties/
│   └── launchSettings.json                 # F5 调试 profile（4 个：Win Debug/Release + WSL2 Debug/Release）
├── Services/
│   └── DevReleaseService.cs                # 绑定服务（[Binding] 暴露给前端）
├── Plugins/                                # （可放置自定义插件）
└── frontend/                               # 前端项目根（Vite + pnpm）
    ├── index.html                          # 入口 HTML
    ├── app.js                              # 前端逻辑（通过 window.wails 调用后端）
    └── styles.css                          # 样式
```

## Debug / Release 模式

| 模式 | 触发条件 | 行为差异 |
|------|---------|----------|
| **Debug** | `WAILS_DEBUG=true` / `--debug` 参数 / `DOTNET_ENVIRONMENT=Development` | • 窗口标题追加 "(Debug)"<br>• 自动打开 WebView2 DevTools<br>• 日志级别 = Debug<br>• 顶部显示橙色 Debug 标识<br>• 前端可观察详细日志 |
| **Release** | 上述条件均不满足 | • 窗口标题无 Debug 标记<br>• 关闭 DevTools 自动打开<br>• 日志级别 = Information<br>• 顶部显示绿色 Release 标识<br>• 性能优化（去除调试符号） |

## 运行方式

### 方式一：通过 Wails.Net CLI（推荐）

```bash
# Debug 模式（参照 Tauri `tauri dev` / Wails v3 `wails dev`）
wails dev --project examples/Wails.Net.Demo.DevRelease/Wails.Net.Demo.DevRelease.csproj

# Release 模式（参照 Tauri `tauri build` / Wails v3 `wails build`）
wails build --project examples/Wails.Net.Demo.DevRelease/Wails.Net.Demo.DevRelease.csproj

# 全平台构建
wails build --project examples/Wails.Net.Demo.DevRelease --all-platforms
```

### 方式二：通过 .NET CLI

```bash
# Debug 模式
dotnet run --project examples/Wails.Net.Demo.DevRelease -c Debug

# Release 模式
dotnet run --project examples/Wails.Net.Demo.DevRelease -c Release
```

### 方式三：通过 IDE F5 调试

在 Visual Studio / Rider 中选择：

- `DevRelease (Windows · Debug)` — 启动 Debug 模式
- `DevRelease (Windows · Release)` — 启动 Release 模式
- `DevRelease (WSL2 · Linux · Debug)` — WSL2 Linux Debug
- `DevRelease (WSL2 · Linux · Release)` — WSL2 Linux Release

F5 启动时 `launchSettings.json` 自动设置 `WAILS_DEBUG` 等环境变量。

## 与 Tauri / Wails 对照

| 维度 | Tauri v2 | Wails v3 | **本 Demo** |
|------|----------|----------|-------------|
| 开发命令 | `tauri dev` | `wails dev` | `wails dev` |
| 发布命令 | `tauri build` | `wails build` | `wails build` |
| DevTools 自动打开 | ❌ 手动 F12 | ✅ 默认 | ✅ Debug 模式自动 |
| WAILS_DEBUG 环境变量 | ❌ | ✅（`wails dev` 设置） | ✅（`wails dev` 与 F5 profile 都设置） |
| launchSettings.json | ❌（VSCode launch.json） | ❌（GoLand Run Config） | ✅ .NET 惯例 |
| 前后端项目分布 | `src/` + `src-tauri/` | `frontend/` | **`frontend/` + 后端根** |

## 演示要点

启动后可以看到：

1. **顶部 Banner**：根据模式显示橙色（Debug）或绿色（Release）
2. **模式信息卡片**：展示 PID、.NET 版本、启动时间、运行时长、调用次数、操作系统
3. **AddAsync 卡片**：演示异步绑定方法
4. **ThrowError 卡片**：演示错误处理路径（CallError 协议）
5. **窗口操作卡片**：通过 `wails.window.*` 命令控制窗口

## 进一步阅读

- [项目结构与调试/发布模式指南](../docs/development/project-structure-and-modes.md)
- [构建与打包指南](../docs/development/build-and-pack.md)
- [功能对比：Wails.Net vs Tauri 2 vs Wails 3](../docs/comparison-with-tauri2-wails3.md)
- [快速入门](../docs/getting-started.md)

---

**最后更新**：2026-08-03
