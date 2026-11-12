# Wails.Net 文档

Wails.Net 是 Wails v3 (Go) 的 .NET 10 移植实现，融合 ASP.NET Core 的 Generic Host 与 Wails v3 的窗口/IPC 对象模型；插件、权限与能力模型为本项目自研设计。

本文档库采用三层结构：**架构 → 实现 → API/指南**，并提供 ADR 记录关键架构决策。

## 快速导航

- [快速入门](getting-started.md) - 从零开始构建第一个 Wails.Net 应用
- [架构概览](architecture.md) - 框架整体分层与核心模式速览
- [插件开发指南](plugins.md) - 内置插件清单与自定义插件开发
- [能力矩阵：Wails.Net vs Wails v3](comparison-with-wails-v3.md) - 两方能力矩阵、独有能力、差距与路线图
- [架构策略对比详情](fusion-strategy-comparison.md) - 按 Host/DI、Runtime/Window/IPC、Plugin/Security 三大主线的逐项对比
- [构建打包指南](development/build-and-pack.md) - 从开发到分发的完整流程（含 wails.json、签名、Android、CI/CD）

## 文档结构

### 架构文档（`architecture/`）

描述框架的分层设计、核心抽象与跨模块协作模式。

| 文档 | 内容 |
|------|------|
| [宿主层与应用生命周期](architecture/hosting-and-lifecycle.md) | `DesktopApplicationBuilder`、Generic Host 集成、`DesktopHostedService`、启动/关闭顺序 |
| [架构策略审计与优化清单](architecture/fusion-architecture-audit.md) | 基于当前代码审计 Host/DI、Runtime/IPC、Plugin/Security 三条架构主线，确认已落地调整并沉淀可执行优化项 |
| [架构策略审计（2026-08-18 时点快照）](architecture/architecture-audit-2026-08-18.md) | 以只读静态分析方式对当时代码做的全量审计结论，保留为历史时点对照 |
| [架构优化修改路线图](architecture/optimization-roadmap.md) | 按 Host/DI、Runtime/IPC、Plugin/Security 三大主线梳理优化目标、任务拆解、风险与验收标准 |
| [架构对标分析与统一 SDK 优化方案](architecture/benchmark-audit-and-unified-sdk-plan.md) | Wails v3 / ASP.NET Core 逐项 API 与功能对标矩阵；统一 SDK 方案（调试自动选平台、打包多平台编译）；可破坏性重构任务总表 |
| [绑定系统与命令调度](architecture/binding-and-command-system.md) | `BindingManager`、`CommandDispatcher`、源生成器调用器、FNV-1a 哈希、绑定/命令双轨路由 |
| [插件框架](architecture/plugin-system.md) | `IPlugin` 契约、`IPluginContext`、命令注册、生命周期、47 个内置插件（39 桌面 + 7 移动端 + 1 Android 运行时 / 40 个项目） |
| [平台抽象层](architecture/platform-abstraction.md) | `IPlatformApp`、`IWebviewWindowImpl`、Server 模式降级、Windows/Linux/Android 实现 |
| [传输层与 IPC 通信](architecture/transport-and-ipc.md) | `ITransport`、AssetServerTransport、原生 IPC、WebSocket 广播、`EventIPCTransport` 回退、消息格式 |
| [安全与权限模型](architecture/security-and-permissions.md) | CSP、URL 白名单、IPC 来源验证、`Capability` 自动加载、`PermissionManager`、Scope 校验 |
| [权限模式迁移指南](architecture/permission-migration-guide.md) | Compatibility → Strict 迁移步骤、capability 文件编写、CSP 默认值、常见问题 |
| [深度对比与重构加固方案](architecture/refactor-and-hardening-plan.md) | 对标 Wails v3 的深度差距分析、SEC-H 安全加固路线、允许破坏性更改的重构清单与实施批次 |
| [重构执行命令语言（RHCL）](architecture/refactor-command-language.md) | 驱动重构方案的 AI 执行命令语言：语法、状态文件、命令目录、守护规则、响应协议与提示词速查表 |

### 实现文档（`implementation/`）

描述各子系统的实现细节、关键代码路径与设计取舍。

| 文档 | 内容 |
|------|------|
| [窗口管理实现](implementation/window-management.md) | `WindowManager`、`Win32WebviewWindow`、多窗口、消息循环、`WindowPlugin` |
| [源代码生成器与代码生成](implementation/source-generators.md) | `BindingSourceGenerator`、`GeneratedBindingRegistry`、编译期强类型调用器、TypeScript 生成 |
| [资源服务器实现](implementation/asset-server.md) | `AssetServer` 基类、`FileAssetServer`/`BundledAssetServer`、双中间件管道、NonceInjector/IsolationInjector、SPA 路由回退、运行时 JS 注入 |
| [CLI 工具实现](implementation/cli-tool.md) | `Wails.Net.Cli`、`System.CommandLine`、脚手架、代码生成器 |
| [事件系统实现](implementation/event-system.md) | `EventProcessor`、事件订阅/发布、跨窗口广播、JS 回调、`senderWindowId` 传播 |

### API 参考（`api/`）

| 文档 | 内容 |
|------|------|
| [前端 JavaScript API 参考](api/frontend-api.md) | `@wails-net/runtime` 命名导出（`import { wails }`）、44 个命名空间、`call`/`events`/`window`/`app` 等模块 |

### 开发指南（`development/`）

| 文档 | 内容 |
|------|------|
| [项目结构与调试/发布模式](development/project-structure-and-modes.md) | 前后端项目分布、Debug 模式（`wails dev`）、Release 模式（`wails build` / `wails publish` / `wails pack`），与 Wails v3 对照 |
| [构建打包指南](development/build-and-pack.md) | 开发模式、构建、打包分发（zip/deb/rpm/appimage/nsis）、wails.json 配置、代码签名、Android 构建、CI/CD 示例 |
| [测试指南](development/testing-guide.md) | TUnit 使用、测试组织、断言模式、平台特定测试、覆盖率要求 |
| [测试方案设计](development/testing-strategy.md) | 战略层测试设计：五层测试金字塔、`Wails.Net.Testing` Mock 运行时、源生成器测试、禁反射守卫、CI 流水线重构 |
| [插件前后端一体发布](development/plugin-packaging.md) | 双包模型（NuGet + npm 同版本发布）、vite 项目调用、类型提示、发布流水线 |
| [插件前后端按平台拆分](development/plugin-platform-split.md) | 桌面端 / 安卓端 / 平台特有三维度的前后端拆分模型与能力边界 |
| [Wails v3 上游差异与对齐记录](development/wails-v3-beta-comparison.md) | 以 `v3.1.0` 为当前基线的上游差异清单、可参考项、实现与回归记录（§1–§10 保留 beta 时代结论） |
| [贡献指南](development/contributing.md) | 编码规范、命名约定、Git 提交规范、PR 流程、代码审查要点 |
| [发布指南](development/release-guide.md) | 版本号集中管理、发布流程、GitHub Actions CI/CD 流水线、NuGet 发布 |

### 架构决策记录（`ADR/`）

记录关键架构决策的背景、选项与权衡，便于未来回溯。

| ADR | 决策 |
|-----|------|
| [ADR-0001 技术选型](ADR/0001-technology-selection.md) | .NET 10、Generic Host、WebView2、GirCore、TUnit 等技术栈选择 |
| [ADR-0002 插件化架构](ADR/0002-plugin-architecture.md) | 采用"核心即插件"的自研插件化哲学 |
| [ADR-0003 源代码生成器替代反射](ADR/0003-source-generator-for-bindings.md) | `IIncrementalGenerator` 编译期注册绑定，支持 AOT 裁剪 |
| [ADR-0004 Android 平台实现](ADR/0004-android-platform.md) | `android` 工作负载、`net10.0-android24.0` TFM、Single-Activity + Fragment 窗口模型、`WebViewClient.ShouldInterceptRequest` 资源拦截、`WebMessageListener` IPC |

## 顶层文档

| 文档 | 内容 |
|------|------|
| [快速入门](getting-started.md) | 环境要求、创建项目、核心概念、项目结构 |
| [架构概览](architecture.md) | 分层架构图、11 大管理器、设计模式、命名空间 |
| [插件开发指南](plugins.md) | 插件接口、命令注册、47 个内置插件（39 桌面 + 7 移动端 + 1 Android 专属）、P1/P2 新能力、最佳实践 |
| [能力矩阵](comparison-with-wails-v3.md) | Wails.Net vs Wails v3 能力矩阵、独有能力、差距与路线图 |
| [架构策略对比详情](fusion-strategy-comparison.md) | 按 Host/DI、Runtime/Window/IPC、Plugin/Security 三大主线的逐项对比 |

### 归档文档（`archive/`）

历史快照，**不再维护**，仅用于追溯当时的对比结论。当前参考基线仅 Wails v3，归档内容中的外部框架对比列不代表现行立场。

| 文档 | 内容 |
|------|------|
| [归档说明](archive/README.md) | 归档原因、被取代的主文档、使用建议与维护规则 |
| [旧版能力矩阵](archive/comparison-with-tauri2-wails3.md) | 曾同时对比三个框架的能力矩阵原件 |
| [旧版架构融合策略对比](archive/fusion-strategy-comparison.md) | 曾按三框架逐项论证融合策略的对比原件 |

## 阅读建议

### 初次接触 Wails.Net

1. 阅读 [快速入门](getting-started.md) 跑通第一个示例
2. 浏览 [架构概览](architecture.md) 建立整体认识
3. 查阅 [插件开发指南](plugins.md) 了解可用能力

### 深入理解框架

1. 依次阅读 `architecture/` 下的架构文档
2. 对照 `implementation/` 下的实现文档阅读源码
3. 查阅 `ADR/` 了解关键设计取舍

### 开发贡献

1. 阅读 [贡献指南](development/contributing.md) 了解编码规范
2. 阅读 [测试指南](development/testing-guide.md) 编写测试
3. 遵循 `TreatWarningsAsErrors=true` 与 TUnit 断言规则

## 技术栈速览

| 领域 | 选型 |
|------|------|
| 运行时 | .NET 10 (net10.0) |
| 宿主 | Microsoft.Extensions.Hosting 10.0.15 |
| DI | Microsoft.Extensions.DependencyInjection 10.0.15 |
| 配置 | Microsoft.Extensions.Configuration 10.0.15 |
| 日志 | Microsoft.Extensions.Logging 10.0.15 |
| Windows WebView | Microsoft.Web.WebView2 1.0.4314.35 |
| Win32 互操作 | Microsoft.Windows.CsWin32 0.3.371 |
| Linux GTK | GirCore 0.8.1 |
| Android 工作负载 | `android` 36.1.43（`net10.0-android36.0`，最低 API Level 24） |
| CLI 解析 | System.CommandLine 2.0.15 |
| 测试框架 | TUnit 1.83.8 |
| 脚本语言 | F# (.fsx) |

## 参考资源

- [Wails v3 源码](https://github.com/wailsapp/wails/tree/v3.1.0) — Go 版本参考实现（`v3.1.0` weekly，2026-11-01；2026-11-12 复核仍为最新正式版）
- [GirCore](https://github.com/gircore/gir.core) — GTK4 .NET 绑定
- [TUnit](https://github.com/thomhurst/TUnit) — 现代 .NET 测试框架
- [CsWin32](https://github.com/microsoft/CsWin32) — Win32 源生成器
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI 解析库

---

**最后更新**：2026-11-12（文档体系收敛：参考源统一为 Wails v3 `v3.1.0`（2026-11-01 weekly），移除外部框架对比基线；能力矩阵文档更名为 `comparison-with-wails-v3.md` 并归档原件至 `archive/`；索引补齐 4 篇未收录文档与 `archive/` 说明；全库内部链接体检与修复（代码文件相对路径、模块拆分后的新位置））
