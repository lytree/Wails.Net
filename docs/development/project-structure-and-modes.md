# 项目结构与调试/发布模式

> 本文档参照 **Tauri v2** 和 **Wails v3 (Go)** 的项目组织方式，介绍 Wails.Net 桌面应用的前后端项目分布、
> 调试模式（`wails dev`）与发布模式（`wails build` / `wails publish` / `wails pack`）。
>
> - **更新日期**：2026-08-03
> - **适用版本**：Wails.Net `0.1.0-alpha.1` 及以上
> - **参考实现**：
>   - Wails v3 `v3.0.0-beta.4` 源码（`internal/project/project.go`、`cmd/wails3/dev.go`、`cmd/wails3/build.go`）
>   - Tauri v2 GA `2.11.5` CLI（`tauri dev` / `tauri build`）

---

## 目录

1. [与 Tauri v2 / Wails v3 的结构对照](#1-与-tauri-v2--wails-v3-的结构对照)
2. [前后端项目分布（约定布局）](#2-前后端项目分布约定布局)
3. [Debug 模式（开发与热更新）](#3-debug-模式开发与热更新)
4. [Release 模式（构建与分发）](#4-release-模式构建与分发)
5. [完整文件清单与职责说明](#5-完整文件清单与职责说明)
6. [CLI 命令速查](#6-cli-命令速查)
7. [典型工作流示例](#7-典型工作流示例)
8. [与 Tauri / Wails 差异与决策](#8-与-tauri--wails-差异与决策)

---

## 1. 与 Tauri v2 / Wails v3 的结构对照

Wails.Net 的项目布局与 Wails v3 保持**完全一致**（因为本项目是 Wails v3 的 .NET 10 移植实现），
并参考 Tauri v2 的 `tauri.conf.json` 风格扩展 `wails.json` 的 `bundle` 字段。

| 维度 | Tauri v2 | Wails v3 (Go) | **Wails.Net** |
|------|----------|---------------|---------------|
| 项目根 | `my-app/` | `my-app/` | `my-app/` |
| 前端源码 | `src/` | `frontend/` | **`frontend/`**（与 Wails v3 一致） |
| 前端构建产物 | `dist/`（Vite 默认） | `frontend/dist/`（嵌入到二进制） | **`frontend/dist/`**（嵌入到二进制） |
| 后端源码 | `src-tauri/src/` | 根目录（`main.go` / `app.go`） | **根目录**（`Program.cs` / `*.csproj`） |
| 后端构建产物 | `src-tauri/target/release/` | `build/bin/` | **`bin/Release/{tfm}/`** |
| 项目元信息 | `package.json` + `tauri.conf.json` | `package.json` + `wails.json` | **`package.json`（根 + 前端）** + **`wails.json`** |
| .NET 配置 | 无（Rust 用 `Cargo.toml`） | 无（Go 用 `go.mod`） | **`appsettings.json`**（继承 ASP.NET Core 风格） |
| 图标 / 资源 | `src-tauri/icons/` | `build/` | **`build/`**（与 Wails v3 一致） |
| F5 调试 | VSCode `launch.json` | GoLand Run Config | **`Properties/launchSettings.json`**（继承 .NET 惯例） |
| Windows 清单 | `src-tauri/tauri.conf.json` 内嵌 | — | **`app.manifest`**（继承 .NET 惯例） |
| 开发模式命令 | `tauri dev` | `wails dev` | **`wails dev`** |
| 发布模式命令 | `tauri build` | `wails build` | **`wails build`** |
| 全平台构建 | `tauri build --target universal` | `wails build -platform all` | **`wails build --all-platforms`** |
| DevTools 自动打开 | ❌（手动 F12） | ✅ | ✅（`wails dev --open-devtools`） |
| 前端开发服务器 | `vite dev` | `vite dev` | **`vite dev`**（自动检测 pnpm/npm） |

**关键决策**：
- Wails.Net 沿用 Wails v3 的 **`frontend/` 目录**而非 Tauri 的 `src-tauri/` 风格，因为：
  1. 本项目是 Wails v3 的 .NET 移植，结构与 Wails v3 保持一致便于 Go ↔ C# 双向对照
  2. 前后端都在项目根，IDE（F5 / Run）体验与 .NET 习惯一致
  3. 前端 `dist/` 嵌入与 Tauri/Wails 一致，平台实现无需修改
- 增加 **`Properties/launchSettings.json`**：.NET 开发者习惯的 F5 调试入口（继承 ASP.NET Core 风格）

---

## 2. 前后端项目分布（约定布局）

### 2.1 标准项目结构

```
my-app/                                  # 项目根（后端 .NET 工程）
├── Program.cs                           # 后端入口（C# 顶层语句，类 Wails v3 的 main.go）
├── MyApp.csproj                         # 后端 .NET 项目文件
├── app.manifest                         # Windows 应用清单（DPI 感知、Win11 兼容）
├── appsettings.json                     # .NET 配置（继承 ASP.NET Core 风格）
├── wails.json                           # Wails.Net 项目元信息（前后端配置、bundle、钩子）
├── Properties/
│   └── launchSettings.json              # F5 调试配置（Windows / WSL2 / Android 三个 profile）
├── Services/                            # 绑定服务（[Binding] 特性方法暴露给前端）
│   └── GreetingService.cs
├── Plugins/                             # 自定义插件（[Command] / [Event]）
│   └── CounterPlugin.cs
├── frontend/                            # 前端项目根（Vite + pnpm）
│   ├── package.json
│   ├── pnpm-lock.yaml
│   ├── tsconfig.json
│   ├── vite.config.ts
│   ├── index.html
│   ├── src/                             # 前端源码
│   │   ├── main.ts
│   │   ├── App.vue / App.tsx / App.svelte
│   │   └── components/
│   ├── public/                          # 静态资源（直接复制，不经过 Vite 处理）
│   └── dist/                            # 前端构建产物（由 `pnpm build` 生成，被 .NET 嵌入）
│       ├── index.html
│       └── assets/
└── build/                               # 图标、平台资源（与 Wails v3 一致）
    ├── appicon.png
    ├── windows/
    │   └── icon.ico
    └── linux/
        └── icon.png
```

### 2.2 关键目录职责

| 路径 | 职责 | Debug 时 | Release 时 |
|------|------|----------|------------|
| `frontend/src/` | 前端源码（TypeScript / JSX / Vue SFC 等） | Vite watch 模式热重载 | 通过 `pnpm build` 编译到 `frontend/dist/` |
| `frontend/dist/` | 前端构建产物 | 通常不存在（被忽略） | 嵌入到 .NET 二进制 |
| `Program.cs` | .NET 入口（Generic Host 启动） | dotnet watch 模式 | dotnet build -c Release |
| `Services/` | 绑定服务（[Binding] / [Command]） | 源生成器重新生成 | 源生成器一次性生成 |
| `wails.json` | 项目元信息 | CLI 读取 beforeDevCommand / dev hooks | CLI 读取 beforeBuildCommand / build hooks |
| `appsettings.json` | .NET 运行时配置 | .NET Configuration 加载 | 同上 |
| `Properties/launchSettings.json` | F5 调试 profile | IDE 启动 dotnet run | N/A |
| `app.manifest` | Windows 应用清单 | dotnet 编译时嵌入 PE | 同上 |
| `build/` | 图标、打包资源 | N/A | `wails pack` 读取 |

### 2.3 monorepo 布局（多 Demo 共用）

Wails.Net 仓库本身采用 monorepo 布局（`examples/*/frontend` 复用 `packages/*` 公共包），
与 Wails v3 + pnpm workspace 模式一致：

```
Wails.Net/                               # 仓库根（monorepo）
├── packages/                            # 可发布的公共包（@wails-net/runtime 等）
│   └── wails-net-runtime/
│       ├── package.json
│       └── src/
├── examples/                            # 演示应用（每个 Demo 是独立 .NET 项目）
│   ├── Wails.Net.Demo.React/
│   │   ├── frontend/                    # ←── pnpm workspace 子包
│   │   │   ├── package.json             #     "dependencies": { "@wails-net/runtime": "workspace:*" }
│   │   │   └── ...
│   │   ├── Program.cs
│   │   └── Wails.Net.Demo.React.csproj
│   ├── Wails.Net.Demo.Vue/              # ←── 另一个 Demo，独立的 .NET 项目
│   │   ├── frontend/
│   │   └── ...
│   └── Wails.Net.Demo.ModeShowcase/     # ←── Debug/Release 模式演示（本文档配套）
├── pnpm-workspace.yaml                  # workspace 根配置
└── package.json                         # 根 scripts（pnpm -r build 等）
```

---

## 3. Debug 模式（开发与热更新）

### 3.1 启动命令

```bash
# 推荐：使用 Wails.Net CLI（完整功能）
wails dev --project examples/Wails.Net.Demo.React/Wails.Net.Demo.React.csproj

# 简化：直接 dotnet watch（无前端 dev server 自动启动）
dotnet watch --project examples/Wails.Net.Demo.React/Wails.Net.Demo.React.csproj
```

`wails dev` 在 `dotnet watch` 的基础上，**并行启动前端开发服务器（`vite dev`）**，
并支持 `wails.json` 中的 `beforeDevCommand` / `afterDevCommand` 钩子。

### 3.2 Debug 模式架构

```
+----------------------------+        +----------------------------+
|  前端 dev server (Vite)    |        |  .NET 后端 (dotnet watch)  |
|  http://localhost:5173     |        |  wails.localhost / 资产服务 |
+----------------------------+        +----------------------------+
        ▲                                       ▲
        │ HMR / WS 推送                          │ IPC（HTTP + WebSocket）
        │                                       │
        └──────────────┐         ┌──────────────┘
                       │         │
                       ▼         ▼
                  +----------------------------+
                  |       WebView2 窗口        |
                  |   (Windows 平台)           |
                  |   wails.localhost:侦听端口  |
                  +----------------------------+
```

### 3.3 Debug 模式行为

| 行为 | Tauri v2 | Wails v3 | **Wails.Net** |
|------|----------|----------|---------------|
| 启动前端 dev server | ✅ 自动 | ✅ 自动 | ✅ 自动（`vite dev`，pnpm/npm 自动检测） |
| 启动后端进程 | ✅ `cargo run` | ✅ `go run` | ✅ `dotnet watch` |
| 前端变更 → HMR | ✅ Vite | ✅ Vite | ✅ Vite |
| 后端 C# 变更 → 重启 | ❌ 需手动 | ❌ 需手动 | ✅ `dotnet watch` 热重启 |
| CSS 实时注入（不刷新页） | ❌ | ✅ Live Reload | ✅ 通过 Vite HMR |
| DevTools 自动打开 | ❌ | ✅ 默认 | ✅ `--open-devtools` 显式 |
| 自定义钩子（`beforeDevCommand`） | ✅ `beforeDevCommand` | ✅ | ✅ |
| 自定义钩子（`afterDevCommand`） | ✅ | ✅ | ✅ |
| Ctrl+C 优雅退出 | ✅ | ✅ | ✅（同时关闭 vite + dotnet watch） |
| 端口自动分配（防冲突） | ✅ | ✅ | ✅（vite 自动挑选可用端口） |

### 3.4 wails.json 中的 dev 配置

```json
{
  "name": "my-app",
  "frontend": {
    "dir": "frontend",
    "devServerUrl": "http://localhost:5173",
    "installCommand": "pnpm install",
    "buildCommand": "pnpm build"
  },
  "beforeDevCommand": "echo '启动 dev 模式'",
  "afterDevCommand": "echo 'dev 模式已退出'"
}
```

**`wails dev` 执行流程**：

```
1. 加载 wails.json
2. 执行 beforeDevCommand（如有）
3. 启动前端 dev server（pnpm/npm dev，在 frontend.dir 下）
4. 启动 dotnet watch（监听 Program.cs / Services / *.cs 变更）
5. dotnet watch 退出时 → 自动关闭前端 dev server
6. 执行 afterDevCommand（如有）
```

### 3.5 launchSettings.json（F5 调试）

```json
{
  "profiles": {
    "my-app (Windows)": {
      "commandName": "Project",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "WAILS_DEBUG": "true"
      },
      "jsWebView2Debugging": true
    },
    "my-app (WSL2)": {
      "commandName": "Project",
      "environmentVariables": {
        "WAILS_DEBUG": "true",
        "DOTNET_ROLL_FORWARD": "Major"
      },
      "remoteDebuggedTarget": "WSL2",
      "distributionName": "Ubuntu-22.04"
    }
  }
}
```

`jsWebView2Debugging: true` 启用 WebView2 远程调试（可通过 Edge `edge://inspect` 连接）。

---

## 4. Release 模式（构建与分发）

### 4.1 构建命令

| 命令 | 输出 | 用途 |
|------|------|------|
| `wails build` | `bin/Release/{tfm}/my-app.exe` | 单平台可执行文件 |
| `wails build -c Debug` | `bin/Debug/{tfm}/my-app.exe` | 调试构建（保留调试符号、不优化） |
| `wails build --runtime win-x64` | `bin/Release/{tfm}/win-x64/publish/my-app.exe` | 跨平台发布（含 .NET 运行时） |
| `wails build --self-contained` | 同上（自包含） | 自包含发布（无需安装 .NET） |
| `wails build --all-platforms` | 各平台 bin/ | 多平台同时构建 |
| `wails publish` | 同 `wails build --runtime` 简写 | = `wails build --runtime` 的别名 |
| `wails pack` | `build/bin/{app}_{version}_{platform}.{ext}` | 平台分发包（msi / deb / rpm / appimage / apk） |

### 4.2 Release 模式架构

```
+--------------------+     +------------------------+     +------------------------+
|  前端 (pnpm build) | ──> │  frontend/dist/        │ ──> │  .NET 二进制            │
|  vite build        │     │  index.html, assets/   │     │  (嵌入为 Embedded       │
|                    │     │  (静态资源)             │     │   Resource / FileAsset) │
+--------------------+     +------------------------+     +------------------------+
                                                                      │
                                                                      ▼
                                                            +------------------------+
                                                            │  wails pack            │
                                                            │  Windows: .msi         │
                                                            │  Linux: .deb/.rpm/.AppImage
                                                            │  Android: .apk         │
                                                            +------------------------+
```

### 4.3 Release 模式行为

| 行为 | Tauri v2 | Wails v3 | **Wails.Net** |
|------|----------|----------|---------------|
| 前端构建 | ✅ `pnpm build` | ✅ `pnpm build` | ✅ `pnpm build`（`frontend.buildCommand`） |
| 前端嵌入 | ✅ `tauri-build` + `include_dir` | ✅ `//go:embed all:frontend/dist` | ✅ 源生成器（`Microsoft.Extensions.FileProviders.Embedded`） |
| 后端构建 | ✅ `cargo build --release` | ✅ `go build` | ✅ `dotnet build -c Release` |
| 自包含发布 | ✅ `--no-bundle` 关闭打包 | ❌ | ✅ `--self-contained` |
| 全平台构建 | ✅ `--target universal` | ✅ `-platform all` | ✅ `--all-platforms` |
| 平台分发包 | msi / dmg / deb / rpm / appimage | 同 Tauri | msi / deb / rpm / appimage / apk |
| 代码签名 | ✅ `tauri sign` | ❌ | ✅ `wails signer` + `wails pack` 集成 |
| CSP（生产） | ✅ `tauri.conf.json` | ✅ `wails.json` | ✅ `wails.json` `security.csp` |

### 4.4 wails.json 中的 build 配置

```json
{
  "frontend": {
    "dir": "frontend",
    "installCommand": "pnpm install",
    "buildCommand": "pnpm build",
    "outputDir": "dist"
  },
  "beforeBuildCommand": "pnpm test",
  "afterBuildCommand": "echo '构建完成'",
  "bundle": {
    "identifier": "com.example.myapp",
    "iconPath": "build",
    "windows": {
      "publisher": "My Company",
      "webviewInstallMode": true
    },
    "linux": {
      "maintainer": "My Company <support@example.com>",
      "debDependencies": ["libwebkit2gtk-4.1-0", "libgtk-3-0"]
    }
  }
}
```

**`wails build` 执行流程**：

```
1. 加载 wails.json
2. 执行 frontend.installCommand（如有，依赖未安装时）
3. 执行 frontend.buildCommand（构建 frontend/dist/）
4. 执行 beforeBuildCommand（如有）
5. dotnet build -c Release
   ├── 自动包含 frontend/dist/**/* 到 CopyToOutputDirectory
   ├── MSBuild 嵌入资源（<EmbeddedResource>）
   └── 源生成器生成 [Binding] 强类型调用器
6. 执行 afterBuildCommand（如有）
```

### 4.5 Debug vs Release 行为差异

| 维度 | Debug (`-c Debug`) | Release (`-c Release`) |
|------|---------------------|-------------------------|
| 优化 | ❌ 关闭 JIT 优化 | ✅ 启用 JIT 优化 / AOT 兼容 |
| 调试符号 | ✅ 嵌入 | ❌ 默认剥离（启用 `<DebugType>embedded</DebugType>` 可嵌入） |
| 前端嵌入 | ⚠️ FileAssetServer 运行时读取 | ✅ BundledAssetServer 嵌入为资源 |
| 性能 | 慢（30%~50% 性能损失） | 快（生产性能） |
| 启动时间 | 慢 | 快 |
| 二进制大小 | 较大（含调试信息） | 较小 |
| 调试器 | ✅ 可断点 | ⚠️ 需 SourceLink 映射源码 |
| 用途 | 日常开发 | 部署 / 分发 / 性能测试 |

### 4.6 Debug 模式判定 API（DebugMode）

框架提供统一的模式判定 API（`Wails.Net.Application.DebugMode`），模板与 `examples/` 下的全部 Demo 统一调用，避免每个项目各自复制一份模式检测代码。

```csharp
// 判定优先级：WAILS_DEBUG 环境变量 > --debug/-d 命令行参数 > DOTNET_ENVIRONMENT/ASPNETCORE_ENVIRONMENT
var isDebugMode = DebugMode.IsEnabled(args);

// 仅按 WAILS_DEBUG 环境变量判定（等价于 PlatformFactory.IsDebugEnabled 的语义）
var isEnvironmentDebug = DebugMode.IsEnvironmentEnabled();
```

典型用法（Program.cs）：

```csharp
var isDebugMode = DebugMode.IsEnabled(args);

// 日志级别按模式切换
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);

app.Options.OnAfterStart = () =>
{
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Title = isDebugMode ? "MyApp (Debug)" : "MyApp",
        // ...
    });
    if (isDebugMode)
    {
        _ = Task.Run(async () => { await Task.Delay(500); mainWindow.OpenDevTools(); });
    }
};
```

Demo 级行为对照：

| 行为 | Debug | Release |
|------|-------|---------|
| 日志级别 | Debug | Information |
| 窗口标题 | 追加 "(Debug)" | 无标记 |
| DevTools | 自动打开（延迟 500ms） | 不打开 |
| 触发方式 | `wails dev` 自动设置 `WAILS_DEBUG=true`；F5 由 launchSettings.json 控制 | `wails build` / 未设置 |

> 注：`examples/` 下全部 25 个 Demo 已统一适配本规范（含 wails.json、4-profile launchSettings.json 与 DebugMode API）。

---

## 5. 完整文件清单与职责说明

### 5.1 后端（.NET）

| 文件 | 必需 | 说明 |
|------|------|------|
| `Program.cs` | ✅ | C# 顶层语句入口；对应 Wails v3 `main.go` |
| `MyApp.csproj` | ✅ | .NET 项目文件；引用 `Wails.Net.Sdk` 或 `Wails.Net.Bundle.*` |
| `app.manifest` | Windows 必需 | Windows 应用清单（DPI 感知、UAC 级别） |
| `appsettings.json` | ✅ | .NET 配置（继承 ASP.NET Core 风格） |
| `wails.json` | ✅ | Wails.Net 项目元信息（前后端配置、bundle、钩子） |
| `Properties/launchSettings.json` | 推荐 | F5 调试 profile（Windows / WSL2 / Android） |
| `Services/*.cs` | 推荐 | 绑定服务（`[Binding]` / `[Command]` 方法暴露给前端） |
| `Plugins/*.cs` | 可选 | 自定义插件（细粒度命令分组） |
| `bin/` | ❌ | 构建产物（自动生成） |
| `obj/` | ❌ | MSBuild 中间产物（自动生成） |

### 5.2 前端（Vite + pnpm）

| 文件 | 必需 | 说明 |
|------|------|------|
| `frontend/package.json` | ✅（如使用 Vite） | 前端依赖与脚本 |
| `frontend/vite.config.ts` | ✅ | Vite 配置（dev server 端口、构建选项） |
| `frontend/tsconfig.json` | 推荐 | TypeScript 配置 |
| `frontend/index.html` | ✅ | 入口 HTML |
| `frontend/src/main.ts` 或 `.tsx`/`.vue` | ✅ | 前端入口 |
| `frontend/src/wails/*.ts` | 自动生成 | 由 `wails generate` 命令从 C# 绑定生成 TypeScript SDK |
| `frontend/dist/` | 自动生成 | `pnpm build` 产物，被 .NET 嵌入 |
| `frontend/node_modules/` | 自动生成 | pnpm 依赖 |

### 5.3 顶层 monorepo（多 Demo）

| 文件 | 必需 | 说明 |
|------|------|------|
| `pnpm-workspace.yaml` | ✅（monorepo） | pnpm workspace 配置 |
| `package.json`（根） | ✅（monorepo） | 根 scripts（`pnpm -r build` 等） |
| `Directory.Build.props` | ✅ | MSBuild 公共属性（TargetFramework、版本号） |
| `Directory.Packages.props` | ✅ | 中央包版本管理（CPM） |
| `nuget.config` | ✅ | NuGet 源配置 |

---

## 6. CLI 命令速查

```bash
# ============ 项目脚手架 ============
wails new my-app                          # 创建新项目（默认模板）
wails new my-app --template vue           # 指定模板（vue / react / svelte / blank）
wails new my-app --identifier com.x.y     # 设置 bundle identifier

# ============ 类型生成 ============
wails generate                            # 从 C# 绑定生成 TypeScript SDK 到 frontend/src/wails/

# ============ Debug 模式 ============
wails dev                                 # 启动 dev 模式（并行运行 vite dev + dotnet watch）
wails dev --no-hot-reload                 # 禁用 dotnet watch 热重载
wails dev --open-devtools                 # 自动打开 WebView2 DevTools
wails dev --skip-hooks                    # 跳过 wails.json 中的 beforeDevCommand/afterDevCommand
wails dev --frontend-only                 # 仅启动前端 dev server（后端需手动 dotnet run）

# ============ Release 模式 ============
wails build                               # 构建（默认 -c Release）
wails build -c Debug                      # 调试构建
wails build --runtime win-x64             # 跨平台发布（包含 .NET 运行时）
wails build --runtime linux-x64
wails build --self-contained              # 自包含（无需安装 .NET）
wails build --all-platforms               # 全平台构建
wails build --skip-frontend               # 跳过前端构建
wails build --skip-hooks                  # 跳过钩子

# ============ 打包分发 ============
wails pack                                # 平台分发包（msi/deb/rpm/appimage/apk）
wails pack --target nsis                  # Windows NSIS 安装包
wails pack --target deb                   # Linux .deb
wails pack --sign                         # 代码签名

# ============ 辅助命令 ============
wails doctor                              # 环境诊断
wails version                             # 显示版本
wails info                                # 显示项目信息
wails clean                               # 清理构建产物（含 frontend/dist/）
wails icon source.png                     # 生成多尺寸图标
wails signer key.pem                      # 生成签名密钥
wails platform list                       # 列出支持的平台
wails self-update                         # CLI 自我更新
```

---

## 7. 典型工作流示例

### 7.1 全新项目从零到发布

```bash
# 1. 创建项目
wails new my-app --template vue --identifier com.example.myapp
cd my-app

# 2. 开发（Debug 模式）
wails dev
# 浏览器自动打开（如配置），前端 HMR + 后端 dotnet watch

# 3. 本地试运行（Release 模式，但本机运行）
wails build -c Release
./bin/Release/net10.0-windows10.0.19041.0/my-app.exe

# 4. 跨平台发布
wails build --runtime win-x64 --self-contained
wails build --runtime linux-x64 --self-contained

# 5. 全平台构建
wails build --all-platforms

# 6. 打包分发
wails pack                              # 生成安装包到 build/bin/
wails pack --sign --key-file key.pem   # 代码签名
```

### 7.2 已有项目日常开发

```bash
# 拉取代码
git pull

# 启动开发
wails dev

# 编写代码：前端 HMR / 后端 dotnet watch 自动重启
# Ctrl+C 退出

# 提交前本地构建验证
wails build -c Debug
```

### 7.3 CI/CD 流水线

```yaml
# .github/workflows/release.yml
name: Release
on: [push]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - uses: pnpm/action-setup@v3
        with:
          version: 9
      - run: pnpm install --frozen-lockfile
      - run: pnpm -r build
      - run: wails build --all-platforms
      - run: wails pack --sign
      - uses: actions/upload-artifact@v4
        with:
          name: packages
          path: build/bin/
```

---

## 8. 与 Tauri / Wails 差异与决策

### 8.1 结构差异

| 差异点 | Tauri v2 | Wails v3 | Wails.Net 决策 |
|--------|----------|----------|----------------|
| 后端目录 | `src-tauri/`（独立 Rust crate） | 根目录 | **根目录**（与 Wails v3 一致） |
| 配置文件 | `tauri.conf.json` | `wails.json` | **`wails.json`**（融合 Tauri 的 `bundle` 字段） |
| 前端目录 | `src/` 或任意 | `frontend/` | **`frontend/`**（与 Wails v3 一致） |
| .NET 配置文件 | N/A | N/A | **`appsettings.json`**（继承 ASP.NET Core） |
| F5 调试 | VSCode `launch.json` | GoLand Run Config | **`Properties/launchSettings.json`**（继承 .NET） |

### 8.2 行为差异

| 差异点 | Tauri v2 | Wails v3 | Wails.Net |
|--------|----------|----------|-----------|
| 绑定调用 | Rust 宏展开（零反射） | 反射 | **源生成器**（零反射，AOT 友好） |
| 平台抽象 | `tauri::Manager` trait | 接口 + Manager 模式 | **接口 + Manager 模式**（与 Wails v3 一致） |
| 移动端 | iOS + Android | iOS + Android | **仅 Android**（iOS 暂不实现，参见 ADR-0004） |
| DevTools | 手动 F12 | 自动打开 | **`--open-devtools` 显式**（避免开发时频繁弹出） |
| 钩子 | `beforeDevCommand` | 同 | **同**（与 Wails v3 一致） |

### 8.3 主要决策记录

| 决策 | 理由 |
|------|------|
| 沿用 Wails v3 的 `frontend/` 目录而非 Tauri 的 `src/` | 保持与 Go 版本的兼容性；Go ↔ C# 双向对照方便 |
| 沿用 Wails v3 的项目根布局而非 Tauri 的 `src-tauri/` | .NET 开发者习惯在根目录运行 `dotnet run` |
| 增加 `Properties/launchSettings.json` | .NET 开发者习惯的 F5 调试入口；IDE 集成度高 |
| 沿用 Wails v3 的 `wails.json` 而非 Tauri 的 `tauri.conf.json` | 字段命名与 Wails v3 一致；前端迁移成本低 |
| 增加 `appsettings.json` | 继承 ASP.NET Core 配置体系，与 Generic Host 集成 |
| 沿用 Wails v3 的 `build/` 图标目录 | 与 Wails v3 工具链（`wails icon`）兼容 |

---

**最后更新**：2026-08-03
