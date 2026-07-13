# 贡献指南

> 本文档为参与 Wails.Net 项目的人类贡献者和 AI 智能体提供协作规范。
> 开始贡献前，请先阅读 [AGENTS.md](../../AGENTS.md) 了解完整项目背景。

---

## 1. 开发环境要求

| 组件 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 10.0 (net10.0) | **必需**，不支持早期版本 |
| WebView2 Runtime | 1.0.3240.44 | Windows Webview |
| GTK4 / WebKitGTK 6 | 通过 GirCore 0.8.0 | Linux 平台 |
| Git | 任意现代版本 | 版本控制 |

### 平台特定依赖

- **Windows 开发**：需安装 WebView2 Runtime、Windows SDK
- **Linux 开发**：需安装 `gtk4`、`webkitgtk-6.0`、`libadwaita-1` 等系统库
- **跨平台 CLI/核心**：仅需 .NET 10 SDK

---

## 2. 项目结构

```
Wails.Net/
├── src/
│   ├── Wails.Net.Application/          # 核心应用（平台无关）
│   ├── Wails.Net.Application.Windows/  # Windows 实现
│   ├── Wails.Net.Application.Linux/    # Linux 实现（GirCore）
│   └── Wails.Net.Cli/                  # CLI 工具与代码生成器
├── tests/
│   ├── Wails.Net.Application.Tests/    # 核心功能测试
│   ├── Wails.Net.Application.Windows.Tests/
│   ├── Wails.Net.Application.Linux.Tests/
│   └── Wails.Net.Cli.Tests/            # CLI 测试
├── Directory.Build.props              # 全局 MSBuild 属性
├── Directory.Packages.props           # CPM 集中包版本管理
└── AGENTS.md                          # 智能体协作指南
```

### 命名空间结构

```
Wails.Net.Application           # 核心应用
Wails.Net.Application.Bindings  # 绑定系统
Wails.Net.Application.Events    # 事件系统
Wails.Net.Application.Transport # 传输层
Wails.Net.Application.Platform  # 平台抽象
Wails.Net.Application.Windows   # Windows 实现
Wails.Net.Application.Linux    # Linux 实现
Wails.Net.Errors                # 错误类型
Wails.Net.Events                # 事件类型定义
```

---

## 3. 开发工作流

### 3.1 七阶段递进原则

项目按 7 个阶段**严格按顺序**实现：

1. ✅ 基础架构与项目骨架
2. ✅ 绑定系统与事件系统
3. ✅ 传输层与消息处理器
4. ✅ 窗口管理器与对话框
5. ✅ Windows 平台实现
6. ✅ Linux 平台实现（GirCore 0.8.0）
7. ✅ CLI 工具与代码生成器

### 3.2 每阶段交付标准

进入下一阶段前必须满足：

1. 所有计划任务已实现
2. 核心功能 100% 测试覆盖，全部通过
3. 代码审查通过，无严重问题
4. Git 提交完成，信息符合规范
5. TODO 更新反映当前进度

### 3.3 工作循环

```
实现 → 单元测试 → 构建 → 运行测试 → 代码审查 → 修复 → Git 提交
```

**关键规则**：
- 每完成一个子任务，立即更新 TODO
- 测试未通过前不得进入下一子任务
- 代码审查发现问题立即修复

---

## 4. 编码规范

### 4.1 命名规范

| 类型 | 规范 | 示例 |
|------|------|------|
| 类名、方法名 | PascalCase | `BindingManager`, `FNV1aHash` |
| 私有字段 | _camelCase | `_boundMethods` |
| 局部变量 | camelCase | `fullName`, `parameterTypes` |
| 接口 | I 前缀 | `IPlatformApp`, `ITransport` |
| 常量 | PascalCase | `OffsetBasis` |

### 4.2 文档注释

- **所有公共 API 必须有 XML 文档注释**（使用 `///`）
- 注释语言：**中文**
- 描述对应 Wails v3 Go 版本的源文件

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

### 4.3 命名空间冲突避免

**重要**：类名不得与所在命名空间同名，否则导致 CS0118 错误。

```csharp
// ❌ 错误：类名与命名空间冲突
namespace Wails.Net.Application.Bindings;
public class Bindings { ... }  // CS0118

// ✅ 正确：使用不同的类名
namespace Wails.Net.Application.Bindings;
public class BindingManager { ... }
```

### 4.4 线程安全

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

### 4.5 错误处理

使用 `Wails.Net.Errors.CallError` 报告绑定调用错误，区分 `CallErrorKind`：`ReferenceError`、`TypeError`、`RuntimeError`。反射调用需解包 `TargetInvocationException`：

```csharp
catch (TargetInvocationException ex) when (ex.InnerException is not null)
{
    var inner = ex.InnerException;
    return ErrorResult(inner.Message,
        inner is ArgumentException ? CallErrorKind.TypeError : CallErrorKind.RuntimeError);
}
```

### 4.6 前后端交互

禁止使用反射获取对应方法。C# 使用**源代码生成器**，以 Command 的形式调用实现。

---

## 5. 项目配置

### 5.1 Directory.Build.props

所有项目继承全局属性：

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
- NuGet 版本通过 [Directory.Packages.props](../../Directory.Packages.props) (CPM) 集中管理

### 5.2 Directory.Packages.props

启用 NuGet Central Package Management（CPM）：

```xml
<PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentrallyManagedPackageVersionEnabled>true</CentrallyManagedPackageVersionEnabled>
</PropertyGroup>
```

在项目文件中使用 `<PackageReference>` 时**不指定版本**：

```xml
<!-- ✅ 正确 -->
<PackageReference Include="TUnit" />

<!-- ❌ 错误：不得在项目文件中指定版本 -->
<PackageReference Include="TUnit" Version="1.58.0" />
```

---

## 6. Git 提交规范

### 6.1 Conventional Commits 格式

```
<type>: <描述>

<可选正文>
```

**type** 取值：

| type | 说明 |
|------|------|
| `feat` | 新功能 |
| `test` | 测试相关 |
| `fix` | 修复 bug |
| `refactor` | 重构（无功能变化） |
| `docs` | 文档 |
| `chore` | 构建、配置等杂项 |

### 6.2 提交示例

```
feat: 实现绑定系统与事件系统（阶段 2）

绑定系统：BindingManager 反射绑定注册与调用...
事件系统：EventProcessor 事件订阅/发布/取消...
测试：39 个 TUnit 单元测试覆盖绑定和事件系统，全部通过。
```

### 6.3 提交原则

- **每个阶段一个提交**（或多个逻辑提交）
- 提交前必须通过所有测试
- 不提交 `bin/`、`obj/` 目录（已由 .gitignore 忽略）
- 不提交敏感信息（密钥、密码等）

### 6.4 分支策略

- `main` — 主开发分支
- 功能分支命名：`feat/{phase}-{feature}`（如 `feat/3-transport`）

---

## 7. 禁止行为清单

以下行为**严格禁止**：

- ❌ 使用 **Python (.py)** 进行任何脚本任务（必须用 F# `.fsx`）
- ❌ 使用 **PInvoke.\*** 包（应使用 CsWin32 源生成器）
- ❌ 使用 **McMaster.Extensions.CommandLineUtils**（应使用 System.CommandLine）
- ❌ 使用 **Xamarin.Forms** 或 **GtkSharp**（应使用 GirCore）
- ❌ 使用 **MSTest / xUnit / NUnit**（应使用 TUnit）
- ❌ 跳过测试直接进入下一阶段
- ❌ 创建未请求的文档文件
- ❌ 使用 C++/CLI 混合模式

### 脚本任务规则

所有脚本任务必须使用 **F# (.fsx)**，通过 `dotnet fsi` 运行：

```bash
# 运行 F# 脚本
dotnet fsi script.fsx
```

可用库：`FSharp.Data`、`System.Net.Http`、`System.Text.Json` 等。

---

## 8. 智能体协作协议

### 8.1 任务接收

接收到任务时，智能体应：

1. 阅读 [AGENTS.md](../../AGENTS.md) 和实施计划
2. 确认当前阶段（通过 TODO 或 `git log`）
3. 阅读相关已有代码
4. 开始实现

### 8.2 工作报告

每完成一个子任务，智能体应：

1. 更新 TODO（标记完成）
2. 简要报告进度
3. 如遇阻塞，明确说明问题

### 8.3 代码审查要点

代码审查时关注：

- [ ] 线程安全（共享状态是否保护）
- [ ] 命名冲突（类名 vs 命名空间）
- [ ] 死代码（不可达分支、未使用字段）
- [ ] 错误处理完整性
- [ ] 测试覆盖完整性
- [ ] 文档注释完整性
- [ ] 编码规范遵循

---

## 9. 资源链接

| 资源 | 链接 |
|------|------|
| Wails v3 源码 | https://github.com/wailsapp/wails/tree/v3.0.0-alpha.102 |
| GirCore | https://github.com/gircore/gir.core |
| TUnit | https://github.com/thomhurst/TUnit |
| CsWin32 | https://github.com/microsoft/CsWin32 |
| System.CommandLine | https://github.com/dotnet/command-line-api |
| 实施计划 | `.trae/documents/wails-net-dotnet10-implementation-plan.md` |
| 问题反馈 | GitHub Issues |

**架构融合策略**：Host/DI/Config/Logging 学 ASP.NET Core；Runtime/Window/IPC 学 Wails v3；Plugin/Security/Capability 学 Tauri v2。绑定方法 ID 使用 FNV-1a 32 位哈希（`offsetBasis=2166136261u`、`prime=16777619u`），必须与 Go 版本 `fnv.New32a()` 一致。

---

**参考文档**：[AGENTS.md](../../AGENTS.md) | [测试指南](./testing-guide.md)

**最后更新**：2026-07-13
