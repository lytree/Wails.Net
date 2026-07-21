# AGENTS.md — 智能体协作指南

> 本文档为参与 Wails.Net 项目开发的 AI 智能体（Agent）和人类贡献者提供协作规范。
> 所有智能体在开始工作前**必须**阅读并遵循本指南。

---

## 1. 项目背景

- **项目**：Wails.Net — Wails v3 (Go) 的 .NET 10 移植实现
- **参考版本**：`wails v3.0.0-*`
- **目标平台**：Windows、Linux、Android（macOS/iOS 暂不实现）
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
| Android 工作负载 | `android` 36.1.43 | .NET Android SDK，**禁止引入 MAUI Controls** |
| Android Webview | Android.Webkit.WebView | 通过 .NET Android 互操作直接调用 |
| Android TFM | `net10.0-android36.0`（`SupportedOSPlatformVersion=24`） | 最低 API Level 24（Android 7.0），使用已安装的 .NET Android SDK 平台版本 |
| CLI 解析 | System.CommandLine 2.0.9 | **禁止使用 McMaster.Extensions.CommandLineUtils** |
| 测试框架 | TUnit 1.58.0 | **禁止使用 MSTest/xUnit/NUnit** |
| 脚本语言 | F# (.fsx) | **严禁使用 Python (.py)** |

### 1.1.1 架构融合策略(必须遵守)

- **Host/DI/Config/Logging** → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
- **Runtime/Window/IPC** → 学 Wails v3（对象模型、IPC、多窗口、事件总线）
- **Plugin/Security/Capability** → 学 Tauri v2（插件能力、权限模型、安全设计）

### 1.2 互操作策略

采用 **Managed Wrappers（托管封装）** 策略：
- Windows：通过 CsWin32 源生成器调用 Win32 API
- Linux：通过 GirCore 调用 GTK4/WebKitGTK/GIO 原生库
- Android：通过 .NET Android 工作负载（Java 互操作）调用 `Android.Webkit.WebView` 等原生 API
- 不使用 C++/CLI 混合模式
- 不引入完整 GUI 框架（MAUI Controls / Avalonia）

---

## 2. 开发工作流

### 2.1 阶段递进原则

项目按 8 个阶段递进实现，**必须严格按顺序进行**：

1. **阶段 1** ✅ 基础架构与项目骨架
2. **阶段 2** ✅ 绑定系统与事件系统
3. **阶段 3** ✅ 传输层与消息处理器
4. **阶段 4** ✅ 窗口管理器与对话框
5. **阶段 5** ✅ Windows 平台实现
6. **阶段 6** ✅ Linux 平台实现（GirCore 0.8.0）
7. **阶段 7** ✅ CLI 工具与代码生成器
8. **阶段 8** ✅ Android 平台实现与三平台自包含构建（.NET Android + WebView）

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
- **源生成器路径下直接捕获具体异常类型**，不再需要解包 `TargetInvocationException`（§3.4 已禁止反射调用方法）

```csharp
catch (InvalidOperationException ex)
{
    return ErrorResult(ex.Message, Errors.CallErrorKind.RuntimeError);
}
catch (ArgumentException ex)
{
    return ErrorResult(ex.Message, Errors.CallErrorKind.TypeError);
}
catch (JsonException ex)
{
    return ErrorResult($"参数反序列化失败: {ex.Message}", Errors.CallErrorKind.TypeError);
}
catch (OperationCanceledException)
{
    // 取消异常直接重抛，由 MessageProcessor 统一处理为 "调用已被取消"
    throw;
}
```

### 3.4  前后端交互（禁用反射协议）

#### 3.4.1 总则

**生产代码（`src/`）严禁使用运行时反射发现或调用方法**。后端方法必须通过源代码生成器（`Wails.Net.SourceGenerators`）在编译期生成强类型调用器，以 Command 的形式实现前后端绑定。

#### 3.4.2 严禁的反射用法（生产代码）

下列 API 在 `src/` 目录下禁止使用（除非明确属于 §3.4.4 允许例外）：

| 禁止 API | 禁止用途 | 替代方案 |
|----------|---------|----------|
| `Type.GetMethod` / `GetMethods` / `GetProperty` / `GetField` / `GetConstructor` | 运行时发现成员 | 源生成器编译期分析 `[Binding]` 特性 |
| `MethodInfo.Invoke` / `ConstructorInfo.Invoke` | 运行时调用方法/构造 | `GeneratedBindingRegistry.TryGetInvoker` 委托 |
| `Activator.CreateInstance` / `CreateInstanceFrom` | 运行时实例化类型 | DI 容器 / `[ModuleInitializer]` 静态注册 |
| `Delegate.CreateDelegate` | 运行时创建委托 | 源生成器生成强类型委托 |
| `MakeGenericMethod` / `MakeGenericType` | 运行时泛型具现 | 源生成器为每个具现类型生成专用代码 |
| `Assembly.GetTypes` / `GetExportedTypes` | 运行时类型扫描 | `[ModuleInitializer]` 显式注册 |
| `AppDomain.AssemblyResolve` 事件 | 全局程序集解析 | 显式 `Assembly.Load` 或项目引用 |

#### 3.4.3 允许的例外（白名单）

以下反射用法经过评审后允许保留，**新增代码须再次评审**：

1. **程序集加载触发模块初始化器**（`PlatformFactory.TryLoadPlatformAssembly`）：
   - `Assembly.Load("Wails.Net.Application.{Platform}")` 加载目标平台程序集
   - `RuntimeHelpers.RunModuleConstructor` 显式触发 `[ModuleInitializer]` 完成委托注册
   - **必须显式调用 `RuntimeHelpers.RunModuleConstructor`**：.NET 运行时对 `[ModuleInitializer]` 采用 lazy 策略，仅 `Assembly.Load` 不保证立即触发模块初始化器执行
   - 不通过反射发现或调用方法，实际调用仍走源生成器生成的强类型委托
   - 解决 `UseAutoPlatform()` 路径下平台程序集按需加载导致 `[ModuleInitializer]` 未触发的根因问题

2. **类型名取字符串**（`BindingManager`）：
   - `instance.GetType().Namespace` / `.Name` 仅取类型名字符串作为字典 key，不构成反射调用方法
   - 注释已标注 "NativeAOT 友好，非反射枚举"

3. **源生成器编译期 Roslyn 分析**（`Wails.Net.SourceGenerators/`）：
   - 编译期通过 Roslyn 符号 API 分析 `[Binding]` 特性并生成代码，不是运行时反射

#### 3.4.4 测试代码例外（`tests/`）

测试代码允许使用反射进行白盒测试（访问 internal/private 成员构造测试场景），但应优先通过以下手段避免反射：

- **`InternalsVisibleTo`** —— 暴露 internal 成员给测试程序集直接调用
- **`internal` 测试辅助属性/方法** —— 在被测类型上添加 internal setter 供测试使用
- **接口注入** —— 通过 DI 注入 mock 实现，避免访问私有状态

无法避免时，反射代码须有清晰注释说明用途（如 `// 白盒测试：通过反射设置 _isRunning 模拟已运行状态`），且不得用于验证生产代码中的绑定调用路径。

#### 3.4.5 违规自动检测

CI 构建通过 `TreatWarningsAsErrors=true` 阻止 AOT 不兼容的反射警告。代码审查时关注：

- [ ] `src/` 目录是否新增 `using System.Reflection;`（除非属于 §3.4.3 白名单）
- [ ] 是否使用 `MethodInfo.Invoke` / `Activator.CreateInstance` 进行方法调用
- [ ] 是否使用 `Type.GetMethod` / `Assembly.GetTypes` 进行动态发现
- [ ] 测试反射代码是否可通过 `InternalsVisibleTo` 改进

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
- **Android 测试**：必须在已安装 `android` 工作负载的 Windows 上运行单元测试；插桩测试需 Android 模拟器或真机
  ```bash
  # 单元测试（Mock Android API，无需设备）
  dotnet run --project tests/Wails.Net.Application.Android.Tests/Wails.Net.Application.Android.Tests.csproj
  ```
- **Android E2E 测试（设备端）**：通过 F# 脚本驱动 adb + Demo APK 完成端到端验证，需 Android 设备/模拟器
  ```bash
  # 启动 Android 模拟器后，运行 E2E 测试（自动构建 + 安装 + 启动 + 验证）
  dotnet fsi tests/Wails.Net.Android.E2E/run-android-e2e.fsx -- --verbose

  # 跳过构建/安装（已手动部署 APK）
  dotnet fsi tests/Wails.Net.Android.E2E/run-android-e2e.fsx -- --no-build --no-install

  # 指定 APK 路径
  dotnet fsi tests/Wails.Net.Android.E2E/run-android-e2e.fsx -- --apk-path path/to/app.apk
  ```
  E2E 测试脚本（`tests/Wails.Net.Android.E2E/run-android-e2e.fsx`）验证：
  1. Demo APK 构建/安装成功
  2. MainActivity 启动并触发 `Android.ActivityCreated` 等平台事件
  3. WebView 加载本地资源（uiautomator dump 验证页面元素）
  4. IPC 绑定调用（点击按钮验证 `GreetingService.Greet` 返回结果）
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
Wails.Net.Application.Android  # Android 实现
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
- [ ] **反射合规**（详见 §3.4）：
  - `src/` 是否新增 `using System.Reflection;`（除非属 §3.4.3 白名单）
  - 是否使用 `MethodInfo.Invoke` / `Activator.CreateInstance` 调用方法
  - 是否使用 `Type.GetMethod` / `Assembly.GetTypes` 动态发现
  - 是否使用 `TargetInvocationException` 解包（已禁用，应捕获具体异常）
  - 测试反射代码是否可通过 `InternalsVisibleTo` 改进

### 7.4 禁止行为

- ❌ 使用 Python 进行任何脚本任务
- ❌ 使用 PInvoke.* 包（应使用 CsWin32）
- ❌ 使用 McMaster.Extensions.CommandLineUtils
- ❌ 使用 Xamarin.Forms 或 GtkSharp
- ❌ 使用 MSTest/xUnit/NUnit
- ❌ 跳过测试直接进入下一阶段
- ❌ 创建未请求的文档文件
- ❌ **生产代码（`src/`）使用反射发现或调用方法**（详见 §3.4）：禁止 `MethodInfo.Invoke`、`Activator.CreateInstance`、`Type.GetMethod`、`Assembly.GetTypes` 等运行时反射 API（`Assembly.Load` 仅触发 `[ModuleInitializer]` 属白名单例外）

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
| .NET Android | https://learn.microsoft.com/dotnet/android/ |
| TUnit | https://github.com/thomhurst/TUnit |
| CsWin32 | https://github.com/microsoft/CsWin32 |
| System.CommandLine | https://github.com/dotnet/command-line-api |
| 实施计划 | `.trae/documents/wails-net-dotnet10-implementation-plan.md` |

---

## 9. 联系

- 问题反馈：GitHub Issues
- 代码贡献：Pull Request（请遵循本指南）

---

**最后更新**：2026-07-21（§3.4 禁用反射协议规范扩展 + §3.3 移除反射时代异常解包示例 + §4.4 添加 Android E2E 测试说明 + PlatformFactory 通过 `Assembly.Load` + `RuntimeHelpers.RunModuleConstructor` 显式触发 `[ModuleInitializer]` 根因修复 + 新增 `tests/Wails.Net.Android.E2E/run-android-e2e.fsx` 设备端 E2E 测试 + CI 添加 `test-android-e2e` job）
