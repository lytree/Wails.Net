# Wails.Net

> 基于 .NET 10 的 [Wails v3](https://github.com/wailsapp/wails/tree/v3.0.0-alpha.102) 移植实现，专注于 Windows、Linux 和 Android 平台。

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20Android-lightgrey.svg)](#平台支持)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## 项目简介

Wails.Net 是一个使用 .NET 10 和 Web 前端技术构建跨平台桌面应用的框架。本项目是 Wails v3（Go 语言实现）的 .NET 移植版本，目标是在保留 Wails v3 架构精髓的同时，充分利用 .NET 生态系统的优势。

### 核心特性

- **跨平台**：Windows（WebView2）、Linux（WebKitGTK-6.0 via GirCore 0.8.0）、Android（.NET Android + WebView）
- **现代 .NET**：基于 .NET 10，支持 AOT、 trimming
- **托管封装策略**：使用原生互操作，避免重量级依赖
- **反射绑定**：自动将 C# 方法暴露给前端 JavaScript
- **类型安全事件系统**：泛型事件 + pre-emit 钩子
- **Server 模式**：支持无 GUI 的容器化部署
- **完整 CLI 工具**：项目脚手架、构建、TS 绑定生成
- **TypeScript 绑定生成器**：自动生成前端类型定义
- **插件系统**：内置 41 个插件（37 桌面 + 4 移动端，覆盖窗口/对话框/文件系统/HTTP/Shell/系统托盘等），借鉴 Tauri v2 权限模型
- **移动端扩展插件**：haptics（震动）、barcode-scanner（条码扫描）、nfc（NFC 读写）、biometric（生物识别）
- **Channel API**：前后端双向流式通信（IChannel + ChannelManager）
- **CSP 安全**：NonceInjector 注入 CSP nonce、IsolationInjector 隔离模式
- **DPI 缩放**：DpiScalePlugin 提供屏幕缩放因子查询与控制
- **平台检测**：PlatformFactory 6 级回退检测链，保证任何环境可降级运行
- **Cake Frosting 构建**：`build/` 项目（Cake Frosting）统一构建，支持三平台自包含打包

## 快速开始

### 环境要求

- .NET 10 SDK
- Windows：WebView2 Runtime（Windows 10/11 已预装）
- Linux：GTK4、WebKitGTK 6.0、libadwaita

### 安装

```bash
# 克隆仓库
git clone https://github.com/wailsapp/wails.net.git
cd wails.net

# 构建解决方案
dotnet build

# 运行测试
dotnet run --project tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj
```

## 通过 NuGet 使用 SDK

发布到 nuget.org 后，无需克隆源码即可在自有项目中使用。

### 方式一：平台聚合包（推荐）

在你的项目 `.csproj` 中添加一行 `PackageReference`：

```xml
<!-- Windows 平台 -->
<PackageReference Include="Wails.Net.Bundle.Windows" />

<!-- Linux 平台 -->
<PackageReference Include="Wails.Net.Bundle.Linux" />
```

聚合包是 meta-package，本身不输出程序集，会通过传递依赖引入对应平台所需的全部 Wails.Net 包（Application、平台实现、AssetServer、Runtime.Js、Errors、Events、SourceGenerators）。

### 方式二：项目模板快速创建

```bash
# 安装模板包
dotnet new install Wails.Net.Templates

# 创建新项目
dotnet new wails-net-app -n MyCompany.MyApp -o MyCompany.MyApp

cd MyCompany.MyApp
dotnet run
```

模板包含：`Program.cs`（含 `DesktopApplicationBuilder` 配置）、`Services/GreetingService.cs`（`[Binding]` 示例）、`frontend/` 前端三件套、`appsettings.json`、`app.manifest`（DPI 感知 PerMonitorV2）。

### 方式三：CLI 全局工具

```bash
# 全局安装 CLI 工具
dotnet tool install -g Wails.Net.Cli

# 环境诊断
wails-net doctor

# 生成 TypeScript 绑定
wails-net generate --assembly path/to/MyApp.dll --output bindings

# 脚手架新项目
wails-net new MyApp --template vue-ts

# 构建项目
wails-net build --project path/to/MyApp.csproj
```

详细的 NuGet 包清单与 SDK 使用方式见 [发布指南](docs/development/release-guide.md)。

### CLI 工具

```bash
# 构建 CLI 工具
dotnet build src/Wails.Net.Cli/Wails.Net.Cli.csproj

# 环境诊断
dotnet run --project src/Wails.Net.Cli -- doctor

# 生成 TypeScript 绑定
dotnet run --project src/Wails.Net.Cli -- generate --assembly path/to/MyApp.dll --output bindings

# 脚手架新项目
dotnet run --project src/Wails.Net.Cli -- new MyApp --template vue-ts

# 构建项目
dotnet run --project src/Wails.Net.Cli -- build --project path/to/MyApp.csproj
```


### 最小示例

```csharp
using Wails.Net.Application;
using Wails.Net.Application.Options;

var app = new Application(new ApplicationOptions
{
    Name = "MyApp",
    Services = { new GreetingService() }
});

app.Run();

public class GreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}
```

## 平台支持

| 平台 | Webview | 状态 |
|------|---------|------|
| Windows | WebView2 | 已实现（窗口/对话框/剪贴板/系统托盘/快捷键/自启动） |
| Linux | WebKitGTK 6.0 (GirCore) | 已实现（窗口/对话框/剪贴板/系统托盘/快捷键） |
| macOS | — | 暂不支持 |
| Android | Android.Webkit.WebView | 已实现（窗口/剪贴板/AssetServer/MainActivity） |
| iOS | — | 暂不支持 |

## 解决方案结构

```
Wails.Net/
├── src/
│   ├── Wails.Net.Application/          # 核心框架（平台无关）
│   ├── Wails.Net.Application.Windows/  # Windows 平台实现（WebView2 + CsWin32）
│   ├── Wails.Net.Application.Linux/    # Linux 平台实现（GirCore 0.8.0）
│   ├── Wails.Net.Application.Android/  # Android 平台实现（.NET Android + WebView）
│   ├── Wails.Net.AssetServer/          # 资源服务器（含 Security/Nonce/Isolation）
│   ├── Wails.Net.Runtime.Js/           # JS 运行时生成器
│   ├── Wails.Net.SourceGenerators/     # 源代码生成器（AOT 友好）
│   ├── Wails.Net.Generator/            # 代码生成器核心库（TS 绑定）
│   ├── Wails.Net.Errors/               # 错误类型
│   ├── Wails.Net.Events/               # 事件类型定义
│   ├── Wails.Net.Bundle.Windows/       # Windows 平台聚合包（meta-package）
│   ├── Wails.Net.Bundle.Linux/         # Linux 平台聚合包
│   ├── Wails.Net.Bundle.Android/       # Android 平台聚合包
│   ├── Wails.Net.Sdk/                  # 统一 SDK（multi-target 自动选择平台 Bundle）
│   ├── Wails.Net.Cli/                  # CLI 工具（dotnet tool）
│   └── Wails.Net.Templates/            # dotnet new 项目模板包
├── build/                              # Cake Frosting 构建项目（三平台自包含打包）
├── tests/                              # 单元测试（6 个测试项目）
├── examples/                           # 示例项目
└── docs/                               # 文档
```

## 技术栈

### 核心依赖

| 库 | 用途 |
|---|---|
| .NET 10 | 运行时与基类库 |
| System.Text.Json | JSON 序列化 |
| System.CommandLine | CLI 参数解析 |
| TUnit | 单元测试框架 |

### Windows 平台

| 库 | 用途 |
|---|---|
| Microsoft.Web.WebView2 | WebView2 托管 |
| Microsoft.Windows.CsWin32 | Win32 P/Invoke 源生成器（替代 PInvoke.*） |

### Linux 平台

| 库 | 用途 |
|---|---|
| GirCore.Gtk-4.0 (0.8.0) | GTK4 UI 工具包 |
| GirCore.WebKit-6.0 (0.8.0) | WebKitGTK 浏览器引擎 |
| GirCore.Gio-2.0 (0.8.0) | GIO（D-Bus、GSettings） |

## 开发指南

### 构建与测试

```bash
# 构建整个解决方案
dotnet build

# 运行核心测试（.NET 10 SDK 不再支持 dotnet test，使用 dotnet run）
dotnet run --project tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj

# 运行 Windows 平台测试（仅 Windows）
dotnet run --project tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj

# 运行 Linux 平台测试（在 WSL 或 Linux 上运行）
dotnet run --project tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj

# 运行 CLI 工具测试
dotnet run --project tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj
```

> **提示**：Linux 测试需在 Linux 或 WSL 环境中运行。在 Windows 上可通过 `wsl -d kali-linux -- bash -c "cd /mnt/f/Code/Dotnet/Wails.Net && dotnet run --project tests/Wails.Net.Application.Linux.Tests"` 运行。

### 三平台自包含打包（Cake Frosting）

```bash
# 使用 Cake Frosting 构建三平台自包含包
dotnet run --project build/Wails.Net.Build -- --target=Dist --platform=windows --rid=all
dotnet run --project build/Wails.Net.Build -- --target=Dist --platform=linux --rid=all
dotnet run --project build/Wails.Net.Build -- --target=Dist --platform=android --rid=all

# Dry-run 预览（不实际构建）
dotnet run --project build/Wails.Net.Build -- --target=Dist --dry-run --skip-frontend
```

### 编码规范

- 启用 `Nullable` 和 `ImplicitUsings`
- `TreatWarningsAsErrors=true`（警告视为错误）
- 使用 Central Package Management (CPM) 管理 NuGet 版本
- 所有公共 API 需有 XML 文档注释（中文）
- 单元测试使用 TUnit，断言必须 `await`

### 实施阶段

本项目按 8 个阶段递进实现，详见 [架构概览](docs/architecture.md) 与 [ADR](docs/README.md)：

1. ✅ **基础架构与项目骨架** — 接口定义、错误类型、事件类型
2. ✅ **绑定系统与事件系统** — 反射绑定、FNV-1a 哈希、事件订阅/发布
3. ✅ **传输层与消息处理器** — HTTP 传输、WebSocket 广播、消息协议
4. ✅ **窗口管理器与对话框** — 窗口生命周期、对话框系统
5. ✅ **Windows 平台实现** — WebView2 骨架、注册表主题检测、剪贴板、自启动、环境信息
6. ✅ **Linux 平台实现** — GirCore 0.8.0/GTK4 骨架、环境变量主题检测、剪贴板存根、XDG 自启动、环境信息
7. ✅ **CLI 工具与生成器** — 脚手架、TS 绑定生成、环境诊断、项目构建
8. ✅ **Android 平台实现与三平台自包含构建** — `android` 工作负载、`net10.0-android24.0` TFM、Single-Activity + Fragment、WebMessageListener IPC、Cake Frosting 构建

## 贡献

欢迎贡献！请阅读 [AGENTS.md](AGENTS.md) 了解协作规范。

## 许可证

MIT License — 详见 [LICENSE](LICENSE) 文件。

## 致谢

- [Wails](https://wails.io) — 原始 Go 版本项目
- [GirCore](https://github.com/gircore/gir.core) — GObject Introspection C# 绑定
- [TUnit](https://github.com/thomhurst/TUnit) — 现代 .NET 测试框架
