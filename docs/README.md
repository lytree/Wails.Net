# Wails.Net 文档

Wails.Net 是 Wails v3 (Go) 的 .NET 10 移植实现，融合 ASP.NET Core 的 Generic Host、Wails v3 的窗口/IPC 对象模型和 Tauri v2 的插件/权限设计。

本文档库采用三层结构：**架构 → 实现 → API/指南**，并提供 ADR 记录关键架构决策。

## 快速导航

- [快速入门](getting-started.md) - 从零开始构建第一个 Wails.Net 应用
- [架构概览](architecture.md) - 框架整体分层与核心模式速览
- [插件开发指南](plugins.md) - 内置插件清单与自定义插件开发

## 文档结构

### 架构文档（`architecture/`）

描述框架的分层设计、核心抽象与跨模块协作模式。

| 文档 | 内容 |
|------|------|
| [宿主层与应用生命周期](architecture/hosting-and-lifecycle.md) | `DesktopApplicationBuilder`、Generic Host 集成、`DesktopHostedService`、启动/关闭顺序 |
| [绑定系统与命令调度](architecture/binding-and-command-system.md) | `BindingManager`、`CommandDispatcher`、表达式树编译、FNV-1a 哈希、双轨调用路径 |
| [插件框架](architecture/plugin-system.md) | `IPlugin` 契约、`IPluginContext`、命令注册、生命周期、30 个内置插件分类 |
| [平台抽象层](architecture/platform-abstraction.md) | `IPlatformApp`、`IWebviewWindowImpl`、Server 模式降级、Windows/Linux 实现 |
| [传输层与 IPC 通信](architecture/transport-and-ipc.md) | `ITransport`、AssetServerTransport、WebSocket 广播、消息格式 |
| [安全与权限模型](architecture/security-and-permissions.md) | CSP、URL 白名单、IPC 来源验证、`Capability`、`PermissionManager` |

### 实现文档（`implementation/`）

描述各子系统的实现细节、关键代码路径与设计取舍。

| 文档 | 内容 |
|------|------|
| [窗口管理实现](implementation/window-management.md) | `WindowManager`、`Win32WebviewWindow`、多窗口、消息循环、`WindowPlugin` |
| [源代码生成器与代码生成](implementation/source-generators.md) | `BindingSourceGenerator`、`GeneratedBindingRegistry`、表达式树编译、TypeScript 生成 |
| [资源服务器实现](implementation/asset-server.md) | `FileAssetServer`、SPA 路由回退、运行时 JS 注入、MIME 类型 |
| [CLI 工具实现](implementation/cli-tool.md) | `Wails.Net.Cli`、`System.CommandLine`、脚手架、代码生成器 |
| [事件系统实现](implementation/event-system.md) | `EventProcessor`、事件订阅/发布、跨窗口广播、JS 回调 |

### API 参考（`api/`）

| 文档 | 内容 |
|------|------|
| [前端 JavaScript API 参考](api/frontend-api.md) | `window.wails` 全局对象、25 个命名空间、`call`/`events`/`window`/`app` 等模块 |

### 开发指南（`development/`）

| 文档 | 内容 |
|------|------|
| [测试指南](development/testing-guide.md) | TUnit 使用、测试组织、断言模式、平台特定测试、覆盖率要求 |
| [贡献指南](development/contributing.md) | 编码规范、命名约定、Git 提交规范、PR 流程、代码审查要点 |
| [发布指南](development/release-guide.md) | 版本号集中管理、发布流程、GitLab CI/CD 流水线、NuGet 发布 |

### 架构决策记录（`ADR/`）

记录关键架构决策的背景、选项与权衡，便于未来回溯。

| ADR | 决策 |
|-----|------|
| [ADR-0001 技术选型](ADR/0001-technology-selection.md) | .NET 10、Generic Host、WebView2、GirCore、TUnit 等技术栈选择 |
| [ADR-0002 插件化架构](ADR/0002-plugin-architecture.md) | 采用 Tauri v2 风格的"核心即插件"哲学 |
| [ADR-0003 源代码生成器替代反射](ADR/0003-source-generator-for-bindings.md) | `IIncrementalGenerator` 编译期注册绑定，支持 AOT 裁剪 |

## 顶层文档

| 文档 | 内容 |
|------|------|
| [快速入门](getting-started.md) | 环境要求、创建项目、核心概念、项目结构 |
| [架构概览](architecture.md) | 分层架构图、9 大核心模块、设计模式、命名空间 |
| [插件开发指南](plugins.md) | 插件接口、命令注册、30 个内置插件、最佳实践 |

## 阅读建议

### 初次接触 Wails.Net

1. 阅读 [快速入门](getting-started.md) 跑通第一个示例
2. 浏览 [架构概览](architecture.md) 建立整体认识
3. 查阅 [插件开发指南](plugins.md) 了解可用能力

### 深入理解框架

1. 依次阅读 `architecture/` 下的 6 篇架构文档
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
| 宿主 | Microsoft.Extensions.Hosting 10.0.0 |
| DI | Microsoft.Extensions.DependencyInjection 10.0.0 |
| 配置 | Microsoft.Extensions.Configuration 10.0.0 |
| 日志 | Microsoft.Extensions.Logging 10.0.0 |
| Windows WebView | Microsoft.Web.WebView2 1.0.3240.44 |
| Win32 互操作 | Microsoft.Windows.CsWin32 0.3.298 |
| Linux GTK | GirCore 0.8.0 |
| CLI 解析 | System.CommandLine 2.0.9 |
| 测试框架 | TUnit 1.58.0 |
| 脚本语言 | F# (.fsx) |

## 参考资源

- [Wails v3 源码](https://github.com/wailsapp/wails/tree/v3.0.0-alpha.102) — Go 版本参考实现
- [GirCore](https://github.com/gircore/gir.core) — GTK4 .NET 绑定
- [TUnit](https://github.com/thomhurst/TUnit) — 现代 .NET 测试框架
- [CsWin32](https://github.com/microsoft/CsWin32) — Win32 源生成器
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI 解析库

---

**最后更新**：2026-07-13
