# AGENTS.md — 智能体协作指南

> 本文档为参与 Wails.Net 项目开发的 AI 智能体（Agent）和人类贡献者提供协作规范。
> 所有智能体在开始工作前**必须**阅读并遵循本指南。

---

## 1. 项目背景

- **项目**：Wails.Net — Wails v3 (Go) 的 .NET 10 移植实现
- **参考版本**：`wails v3.0.0-alpha.102`
- **目标平台**：Windows、Linux（macOS/iOS/Android 暂不实现）
- **实施计划**：详见 `.trae/documents/wails-net-dotnet10-implementation-plan.md`

### 1.1 技术选型（已确定，不可更改）

| 领域 | 选型 | 说明 |
|------|------|------|
| 运行时 | .NET 10 (net10.0) | 不支持早期版本 |
| **宿主** | Microsoft.Extensions.Hosting 10.0.0 | Generic Host 管理生命周期 |
| **DI** | Microsoft.Extensions.DependencyInjection 10.0.0 | 依赖注入容器 |
| **配置** | Microsoft.Extensions.Configuration 10.0.0 | appsettings.json 配置 |
| **选项** | Microsoft.Extensions.Options 10.0.0 | IOptions\<T\> 强类型配置 |
| **日志** | Microsoft.Extensions.Logging 10.0.0 | ILogger\<T\> 日志抽象 |
| Windows Webview | Microsoft.Web.WebView2 1.0.3240.44 | WebView2 Runtime |
| Win32 互操作 | Microsoft.Windows.CsWin32 0.3.298 | 源生成器，**禁止使用 PInvoke.\*** |
| Linux GTK | GirCore 0.8.0 | **禁止使用 Xamarin.Forms、GtkSharp** |
| CLI 解析 | System.CommandLine 2.0.9 | **禁止使用 McMaster.Extensions.CommandLineUtils** |
| 测试框架 | TUnit 1.58.0 | **禁止使用 MSTest/xUnit/NUnit** |
| 脚本语言 | F# (.fsx) | **严禁使用 Python (.py)** |

### 1.1.1 架构融合策略

- **Host/DI/Config/Logging** → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
- **Runtime/Window/IPC** → 学 Wails v3（对象模型、IPC、多窗口、事件总线）
- **Plugin/Security/Capability** → 学 Tauri v2（插件能力、权限模型、安全设计）

### 1.2 互操作策略

采用 **Managed Wrappers（托管封装）** 策略：
- Windows：通过 CsWin32 源生成器调用 Win32 API
- Linux：通过 GirCore 调用 GTK4/WebKitGTK/GIO 原生库
- 不使用 C++/CLI 混合模式

---

## 2. 开发工作流

### 2.1 阶段递进原则

项目按 7 个阶段递进实现，**必须严格按顺序进行**：

1. **阶段 1** ✅ 基础架构与项目骨架
2. **阶段 2** ✅ 绑定系统与事件系统
3. **阶段 3** ✅ 传输层与消息处理器
4. **阶段 4** ✅ 窗口管理器与对话框
5. **阶段 5** ✅ Windows 平台实现
6. **阶段 6** ✅ Linux 平台实现（GirCore 0.8.0）
7. **阶段 7** ✅ CLI 工具与代码生成器

### 2.2 每阶段交付标准（Definition of Done）

每个阶段必须满足以下条件才能进入下一阶段：

1. **代码实现完成**：所有计划任务已实现
2. **单元测试完整**：核心功能 100% 覆盖，测试全部通过
3. **代码审查通过**：无严重问题，遵循编码规范
4. **Git 提交完成**：提交信息符合规范
5. **TODO 更新**：反映当前进度

### 2.3 工作循环

每个阶段遵循以下循环：

```
实现 → 单元测试 → 构建 → 运行测试 → 代码审查 → 修复 → Git 提交
```

**关键规则**：
- 每完成一个子任务，立即更新 TODO
- 测试未通过前不得进入下一子任务
- 代码审查发现问题立即修复

---

## 3. 编码规范

### 3.1 项目配置

所有项目继承 `Directory.Build.props`：

```xml
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

- `TreatWarningsAsErrors=true` — **警告视为错误**，不允许任何警告
- NuGet 版本通过 `Directory.Packages.props` (CPM) 集中管理

### 3.2 C# 编码风格

#### 命名规范

| 类型 | 规范 | 示例 |
|------|------|------|
| 类名、方法名 | PascalCase | `BindingManager`, `FNV1aHash` |
| 私有字段 | _camelCase | `_boundMethods` |
| 局部变量 | camelCase | `fullName`, `parameterTypes` |
| 接口 | I 前缀 | `IPlatformApp`, `ITransport` |
| 常量 | PascalCase | `OffsetBasis` |

#### 文档注释

- **所有公共 API 必须有 XML 文档注释**（使用 `///`）
- 注释语言：**中文**
- 描述对应 Wails v3 Go 版本的源文件（如 `对应 bindings.go`）

```csharp
/// <summary>
/// 绑定管理器，负责注册和调用绑定方法。
/// 对应 Wails v3 Go 版本 bindings.go 中的 Bindings 结构。
/// </summary>
public class BindingManager
{
    /// <summary>
    /// 注册指定实例的所有公共方法。
    /// </summary>
    /// <param name="instance">要注册的实例。</param>
    public void Add(object instance) { ... }
}
```

#### 命名空间冲突避免

**重要**：类名不得与所在命名空间同名，否则导致 CS0118 错误。

```csharp
// ❌ 错误：类名与命名空间冲突
namespace Wails.Net.Application.Bindings;
public class Bindings { ... }  // CS0118

// ✅ 正确：使用不同的类名
namespace Wails.Net.Application.Bindings;
public class BindingManager { ... }
```

#### 线程安全

- 静态共享状态必须使用 `Interlocked` 或 `lock`
- 并发集合优先使用 `ConcurrentDictionary`
- ID 生成器必须使用 `Interlocked.Increment`

```csharp
// ❌ 错误：非线程安全
private static int _nextId = 1;
public int Next() => _nextId++;

// ✅ 正确：线程安全
private static int _nextId = 1;
public int Next() => Interlocked.Increment(ref _nextId);
```

### 3.3 错误处理

- 使用 `Wails.Net.Errors.CallError` 报告绑定调用错误
- 区分 `CallErrorKind`：`ReferenceError`、`TypeError`、`RuntimeError`
- 反射调用需解包 `TargetInvocationException`

```csharp
catch (TargetInvocationException ex) when (ex.InnerException is not null)
{
    var inner = ex.InnerException;
    return ErrorResult(inner.Message,
        inner is ArgumentException ? CallErrorKind.TypeError : CallErrorKind.RuntimeError);
}
```

---

## 4. 测试规范

### 4.1 测试框架

- **唯一允许**：TUnit 1.58.0
- 断言库：TUnit.Assertions
- **断言必须 await**

```csharp
[Test]
public async Task MyTest()
{
    await Assert.That(result).IsEqualTo(expected);
    await Assert.That(() => ThrowMethod()).ThrowsExactly<InvalidOperationException>();
}
```

### 4.2 测试类组织

- 测试类放在 `tests/Wails.Net.Application.Tests/` 下
- 命名规范：`{被测类名}Tests.cs`（如 `BindingsTests.cs`）
- 使用 `[NotInParallel]` 属性避免共享状态冲突

```csharp
using TUnit.Core;

namespace Wails.Net.Application.Tests;

[NotInParallel]
public sealed class BindingsTests
{
    [Test]
    public async Task Method_Scenario_ExpectedBehavior()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

### 4.3 测试命名

格式：`Method_Scenario_ExpectedBehavior`

- `FNV1aHash_ReturnsConsistentValue`
- `Add_ExcludesServiceInternalMethods`
- `Call_UnknownID_ReturnsReferenceError`

### 4.4 运行测试

**重要**：.NET 10 SDK 不再支持 `dotnet test`（VSTest 模式）。

```bash
# 构建测试项目
dotnet build tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj

# 运行测试（使用 dotnet run，不是 dotnet test）
dotnet run --project tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj --no-build
```

#### 平台特定测试

- **Windows 测试**：必须在 Windows 上运行（依赖 Win32 API、注册表、WinForms 剪贴板）
  ```bash
  dotnet run --project tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj
  ```
- **Linux 测试**：必须在 Linux 或 WSL 中运行（依赖 GirCore/GTK4 原生库）
  ```bash
  # 在 WSL 中运行
  wsl -d kali-linux -- bash -c "cd /mnt/f/Code/Dotnet/Wails.Net && dotnet run --project tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj"
  ```
- **CLI 测试**：跨平台，验证生成器、脚手架、构建器逻辑
  ```bash
  dotnet run --project tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj
  ```

### 4.5 测试覆盖要求

| 组件类型 | 覆盖要求 |
|---------|---------|
| 公共 API | 100% 方法覆盖 |
| 错误路径 | 所有 `catch` 分支必须测试 |
| 边界条件 | 空值、空集合、最大值 |
| 并发场景 | 共享状态的线程安全验证 |

---

## 5. Git 提交规范

### 5.1 提交信息格式

使用 Conventional Commits：

```
<type>: <描述>

<可选正文>
```

**type** 取值：
- `feat` — 新功能
- `test` — 测试相关
- `fix` — 修复 bug
- `refactor` — 重构（无功能变化）
- `docs` — 文档
- `chore` — 构建、配置等杂项

### 5.2 提交示例

```
feat: 实现绑定系统与事件系统（阶段 2）

绑定系统：BindingManager 反射绑定注册与调用...
事件系统：EventProcessor 事件订阅/发布/取消...
测试：39 个 TUnit 单元测试覆盖绑定和事件系统，全部通过。
```

### 5.3 提交原则

- **每个阶段一个提交**（或多个逻辑提交）
- 提交前必须通过所有测试
- 不提交 `bin/`、`obj/` 目录（已由 .gitignore 忽略）
- 不提交敏感信息（密钥、密码等）

### 5.4 分支策略

- `main` — 主开发分支
- 功能分支命名：`feat/{phase}-{feature}`（如 `feat/3-transport`）

---

## 6. 架构原则

### 6.1 核心模式

1. **接口驱动的平台抽象**
   - `IPlatformApp`、`IWebviewWindowImpl`、`IClipboardImpl` 等接口
   - 平台实现位于独立项目（`Wails.Net.Application.Windows`、`.Linux`）

2. **管理器模式**
   - `Application` 委托给 `WindowManager`、`EventManager` 等聚焦管理器

3. **服务生命周期**
   - `IServiceStartup` → 启动顺序
   - `IServiceShutdown` → 逆序关闭

4. **Server 模式降级**
   - `ServerPlatformApp`、`ServerWebviewWindow` 提供无 GUI 的 no-op 实现
   - 用于容器化部署和测试

### 6.2 命名空间结构

```
Wails.Net.Application          # 核心应用
Wails.Net.Application.Bindings # 绑定系统
Wails.Net.Application.Events   # 事件系统
Wails.Net.Application.Transport# 传输层
Wails.Net.Application.Platform # 平台抽象
Wails.Net.Application.Windows  # Windows 实现
Wails.Net.Application.Linux    # Linux 实现
Wails.Net.Errors               # 错误类型
Wails.Net.Events               # 事件类型定义
```

### 6.3 FNV-1a 哈希一致性

绑定方法 ID 使用 FNV-1a 32 位哈希，**必须与 Go 版本 `fnv.New32a()` 一致**：

```csharp
const uint offsetBasis = 2166136261u;
const uint prime = 16777619u;
```

---

## 7. 智能体协作协议

### 7.1 任务接收

当接收到任务时，智能体应：
1. 阅读本指南和实施计划
2. 确认当前阶段（通过 TODO 或 git log）
3. 阅读相关已有代码
4. 开始实现

### 7.2 工作报告

每完成一个子任务，智能体应：
1. 更新 TODO（标记完成）
2. 简要报告进度
3. 如遇阻塞，明确说明问题

### 7.3 代码审查要点

智能体进行代码审查时关注：
- [ ] 线程安全（共享状态是否保护）
- [ ] 命名冲突（类名 vs 命名空间）
- [ ] 死代码（不可达分支、未使用字段）
- [ ] 错误处理完整性
- [ ] 测试覆盖完整性
- [ ] 文档注释完整性
- [ ] 编码规范遵循

### 7.4 禁止行为

- ❌ 使用 Python 进行任何脚本任务
- ❌ 使用 PInvoke.* 包（应使用 CsWin32）
- ❌ 使用 McMaster.Extensions.CommandLineUtils
- ❌ 使用 Xamarin.Forms 或 GtkSharp
- ❌ 使用 MSTest/xUnit/NUnit
- ❌ 跳过测试直接进入下一阶段
- ❌ 创建未请求的文档文件

### 7.5 脚本任务规则

所有脚本任务必须使用 F# (.fsx)：

```bash
# 运行 F# 脚本
dotnet fsi script.fsx
```

可使用库：`FSharp.Data`、`System.Net.Http`、`System.Text.Json` 等。

---

## 8. 资源链接

| 资源 | 链接 |
|------|------|
| Wails v3 源码 | https://github.com/wailsapp/wails/tree/v3.0.0-alpha.102 |
| GirCore | https://github.com/gircore/gir.core |
| TUnit | https://github.com/thomhurst/TUnit |
| CsWin32 | https://github.com/microsoft/CsWin32 |
| System.CommandLine | https://github.com/dotnet/command-line-api |
| 实施计划 | `.trae/documents/wails-net-dotnet10-implementation-plan.md` |

---

## 9. 联系

- 问题反馈：GitHub Issues
- 代码贡献：Pull Request（请遵循本指南）

---

**最后更新**：2026-07-10（阶段 7 完成）
