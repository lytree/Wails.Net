# ADR-0001: 技术选型决策

- 状态：已接受（2026-07-13 修订：扩展目标平台至 Android，详见 [ADR-0002](0002-android-platform.md)）
- 日期：2026-07-13
- 决策者：Wails.Net 团队

## 背景

Wails v3（Go 实现，参考版本 `v3.0.0-alpha.102`）需要在 .NET 平台上重新实现，目标平台为 Windows、Linux 与 Android（macOS/iOS 暂不实现）。这是一次跨语言移植，不仅仅是语法转换，更需要在以下约束下做出整套技术决策：

1. **运行时**：必须基于现代 .NET，避免使用停止支持的旧运行时（如 .NET Framework、.NET Core 3.1）。
2. **宿主模型**：需要统一的生命周期管理、依赖注入、配置和日志体系，避免各子系统各自为政。
3. **跨平台一致性**：Windows 与 Linux 必须共享同一套抽象，平台实现位于独立项目中。
4. **互操作策略**：禁止使用 C++/CLI 混合模式；Windows 上禁用 `PInvoke.*` 系列 NuGet 包；Linux 上禁用已过时的 `Xamarin.Forms`、`GtkSharp`。
5. **CLI 工具**：需要现代、官方维护的命令行解析库。
6. **测试体系**：需要支持源生成器、并行执行、与现代 SDK 兼容的测试框架（.NET 10 SDK 已不再支持 VSTest 模式的 `dotnet test`）。
7. **脚本任务**：需要一种安全、可在 Windows 与 Linux 上统一运行的脚本语言，禁止使用 Python。
8. **AOT 兼容性**：未来需要支持 Native AOT 与裁剪（trimming），需评估各选型对反射的依赖程度。

技术选型一旦确定即为项目约束，后续阶段不可更改。详见 [AGENTS.md](../../AGENTS.md) 中 "1.1 技术选型" 章节。

## 决策

采用如下技术栈，所有版本号由 [Directory.Packages.props](../../Directory.Packages.props) 通过 NuGet Central Package Management (CPM) 集中管理：

| 领域 | 选型 | 版本 | 关键理由 |
|------|------|------|---------|
| 运行时 | .NET 10 (`net10.0`) | — | LTS 之前的最现代版本，支持最新 C# 与 AOT |
| 宿主 | `Microsoft.Extensions.Hosting` | 10.0.0 | Generic Host 统一生命周期，借鉴 ASP.NET Core |
| DI | `Microsoft.Extensions.DependencyInjection` | 10.0.0 | 官方 DI 容器，与 Host 原生集成 |
| 配置 | `Microsoft.Extensions.Configuration` | 10.0.0 | `appsettings.json` 与环境变量统一抽象 |
| 选项 | `Microsoft.Extensions.Options` | 10.0.0 | `IOptions<T>` 强类型配置 |
| 日志 | `Microsoft.Extensions.Logging` | 10.0.0 | `ILogger<T>` 抽象，可对接多 sink |
| Windows Webview | `Microsoft.Web.WebView2` | 1.0.3240.44 | Edge Chromium 内核，官方长期支持 |
| Win32 互操作 | `Microsoft.Windows.CsWin32` | 0.3.298 | 源生成器生成 Win32 P/Invoke，**禁用 `PInvoke.*`** |
| Linux GTK | `GirCore` | 0.8.0 | 通过 GObject 内省调用 GTK4/WebKitGTK，**禁用 GtkSharp/Xamarin.Forms** |
| CLI 解析 | `System.CommandLine` | 2.0.9 | 官方维护，**禁用 `McMaster.Extensions.CommandLineUtils`** |
| 测试框架 | `TUnit` | 1.58.0 | 原生支持 MTP，**禁用 MSTest/xUnit/NUnit** |
| 脚本语言 | F# (`.fsx`) | — | 通过 `dotnet fsi script.fsx` 运行，**严禁 Python** |

**架构融合策略**（必须遵守）：

- **Host / DI / Config / Logging** → 学习 ASP.NET Core（`Microsoft.Extensions.*` 全栈）
- **Runtime / Window / IPC** → 学习 Wails v3（对象模型、IPC、多窗口、事件总线）
- **Plugin / Security / Capability** → 学习 Tauri v2（插件能力、权限模型、安全设计）

**互操作策略**：采用 Managed Wrappers（托管封装），Windows 通过 CsWin32 源生成器、Linux 通过 GirCore 调用原生库，不引入 C++/CLI。

## 后果

**正面影响**：

- 所有 `Microsoft.Extensions.*` 组件版本对齐，避免 ABI 不匹配；宿主、DI、配置、日志形成统一心智模型。
- CsWin32 与 GirCore 均为源生成器或绑定生成器路线，运行时反射面积极小，为 Native AOT 兼容奠定基础。
- TUnit 原生支持 Microsoft Testing Platform (MTP)，绕开已废弃的 VSTest，并行执行性能更好。
- F# 脚本与 .NET 运行时无缝集成，可在 Windows 与 WSL/Linux 上统一运行，避免引入 Python 运行时依赖。
- CPM 集中管理 NuGet 版本，杜绝各项目版本漂移。

**负面影响**：

- .NET 10 要求开发与 CI 环境安装最新 SDK；不支持早期 .NET 版本，对部分老用户不友好。
- GirCore 0.8.0 仍处于活跃演进阶段，部分 GTK4 API 绑定可能滞后，需要在 Linux 实现层手动补齐。
- CsWin32 对部分复杂 Win32 API（如带复杂结构体回调的 API）支持不完整，需要 `NativeMethods.txt` 显式声明。
- TUnit 社区生态较 MSTest/xUnit 小，第三方集成（如 IDE 测试适配器）需要单独配置。
- 全栈 `TreatWarningsAsErrors=true` 提升了代码质量门槛，但首次集成第三方库时可能需要补 `NoWarn`。

## 考虑过的替代方案

1. **.NET Framework 4.8**：放弃。已停止新特性演进，不支持 AOT、源生成器、`IIncrementalGenerator`，与现代生态脱节。
2. **.NET 8 / .NET 9**：放弃。.NET 10 引入了对 AOT、新 C# 特性（如 `field` 关键字）的更好支持；选择更现代版本以获得更长的支持窗口。
3. **`PInvoke.*` 系列（如 `PInvoke.Kernel32`）**：放弃。该项目维护节奏放缓，且部分 API 仍依赖运行时反射；CsWin32 由微软官方维护，源生成器路线更契合 AOT。
4. **GtkSharp / Xamarin.Forms**：放弃。GtkSharp 仅支持 GTK3，且维护几乎停滞；Xamarin.Forms 已被 .NET MAUI 取代，不适用于纯桌面壳项目。
5. **`McMaster.Extensions.CommandLineUtils`**：放弃。该库虽然成熟，但非官方维护；选择 `System.CommandLine` 与 .NET 团队路线对齐。
6. **MSTest / xUnit / NUnit**：放弃。三者均依赖 VSTest 适配器，而 .NET 10 SDK 不再支持 `dotnet test`（VSTest 模式）；TUnit 原生支持 MTP，通过 `dotnet run` 执行。
7. **Python 脚本**：放弃。引入 Python 运行时增加 CI 复杂度，且与 .NET 互操作困难；F# 脚本与 .NET 原生集成，可使用 `FSharp.Data`、`System.Net.Http` 等库完成数据解析与 HTTP 任务。
8. **Avalonia / MAUI Controls 作为跨平台 GUI**：放弃。Wails.Net 的 GUI 由 WebView 承载，仅需原生窗口壳与系统 API，引入完整 GUI 框架属于过度设计。**注**：此决策不排斥 .NET Android SDK 本身（见 [ADR-0002](0002-android-platform.md)），仅排斥 MAUI Controls 与 Avalonia 的 UI 抽象层。
9. **C++/CLI 混合模式**：放弃。无法跨平台（仅 Windows），且与现代 .NET 的 AOT 路线冲突。

## 相关文件

- [AGENTS.md — 技术选型](../../AGENTS.md)
- [Directory.Packages.props](../../Directory.Packages.props)
- [Directory.Build.props](../../Directory.Build.props)
