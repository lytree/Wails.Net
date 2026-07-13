# 测试指南

> 本文档描述 Wails.Net 项目的单元测试规范、运行方式与最佳实践。
> 所有贡献者在提交代码前**必须**保证测试通过，且核心功能 100% 覆盖。

---

## 1. 测试框架

Wails.Net 使用 **TUnit 1.58.0** 作为唯一测试框架，**禁止使用 MSTest、xUnit、NUnit**。

- 测试运行器：`TUnit.Engine`
- 断言库：`TUnit.Assertions`
- Mock 库：`NSubstitute` 5.3.0

版本通过 [Directory.Packages.props](../../Directory.Packages.props) 集中管理（CPM），不得在单个项目文件中指定版本。

### 关键规则：断言必须 await

TUnit 的断言返回 `ValueTask`，**必须使用 `await`**，否则断言不会被执行，测试将假性通过。

```csharp
[Test]
public async Task MyTest()
{
    // ✅ 正确：await 断言
    await Assert.That(result).IsEqualTo(expected);

    // ✅ 正确：await 异常断言
    await Assert.That(() => ThrowMethod())
        .ThrowsExactly<InvalidOperationException>();

    // ❌ 错误：忘记 await，断言不会执行
    // Assert.That(result).IsEqualTo(expected);
}
```

---

## 2. 项目结构

测试项目位于 `tests/` 目录下，按测试目标划分：

| 项目 | 路径 | 说明 |
|------|------|------|
| 核心应用测试 | [tests/Wails.Net.Application.Tests/](../../tests/Wails.Net.Application.Tests/) | 绑定、事件、传输、窗口、插件等核心功能 |
| CLI 测试 | [tests/Wails.Net.Cli.Tests/](../../tests/Wails.Net.Cli.Tests/) | 代码生成器、脚手架、构建器、打包器 |
| Windows 平台测试 | tests/Wails.Net.Application.Windows.Tests/ | 依赖 Win32 API、注册表、剪贴板 |
| Linux 平台测试 | tests/Wails.Net.Application.Linux.Tests/ | 依赖 GirCore/GTK4 原生库 |

代表性测试文件参考：[BindingsTests.cs](../../tests/Wails.Net.Application.Tests/BindingsTests.cs)、[BuiltInPluginsExtendedTests.cs](../../tests/Wails.Net.Application.Tests/BuiltInPluginsExtendedTests.cs)、[TypeScriptGeneratorTests.cs](../../tests/Wails.Net.Cli.Tests/TypeScriptGeneratorTests.cs)。

---

## 3. 编写测试

### 3.1 测试类组织

- 文件位置：`tests/Wails.Net.Application.Tests/` 或 `tests/Wails.Net.Cli.Tests/`
- 命名规范：`{被测类名}Tests.cs`（如 `BindingsTests.cs`）
- 类必须标记 `[NotInParallel]` 以避免共享状态冲突
- 类声明为 `sealed`，使用 XML 文档注释说明测试目的

```csharp
using TUnit.Core;
using Wails.Net.Application.Bindings;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 绑定系统的单元测试（TUnit）。
/// 测试绑定注册、调用、FNV-1a 哈希、异常处理等。
/// </summary>
[NotInParallel]
public sealed class BindingsTests
{
    // 测试方法...
}
```

### 3.2 测试命名

格式：`Method_Scenario_ExpectedBehavior`

| ✅ 正确示例 | 说明 |
|-----------|------|
| `FNV1aHash_ReturnsConsistentValue` | 相同输入返回一致哈希 |
| `Add_ExcludesServiceInternalMethods` | Add 排除 Service 内部方法 |
| `Call_UnknownID_ReturnsReferenceError` | 未知 ID 返回 ReferenceError |
| `ProcessPlugin_Configure_RegistersCommands` | Configure 注册命令 |

### 3.3 Arrange-Act-Assert 模式

所有测试遵循 **三段式** 结构，注释分隔：

```csharp
[Test]
public async Task Add_RegistersPublicMethods()
{
    // Arrange
    var bindings = new BindingManager();
    var service = new TestService();

    // Act
    bindings.Add(service);

    // Assert
    await Assert.That(bindings.BoundMethods.Count).IsGreaterThan(0);
    await Assert.That(
        bindings.BoundMethods.ContainsKey("Wails.Net.Application.Tests.TestService.GetName")
    ).IsTrue();
}
```

中文注释亦可使用 `// 安排` / `// 操作` / `// 断言`，与现有测试文件保持一致。

### 3.4 断言示例

```csharp
// 相等性
await Assert.That(result).IsEqualTo(expected);
await Assert.That(result).IsNotEqualTo(other);

// 布尔
await Assert.That(condition).IsTrue();
await Assert.That(condition).IsFalse();

// 集合
await Assert.That(list.Count).IsGreaterThan(0);
await Assert.That(list).Contains(item);

// 异常
await Assert.That(() => ThrowMethod()).ThrowsExactly<InvalidOperationException>();
await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
```

### 3.5 异步测试

异步方法的测试方法签名必须为 `async Task`：

```csharp
[Test]
public async Task GetAsync_ReturnsExpectedResult()
{
    // Arrange
    var service = new TestService();

    // Act
    var result = await service.GetAsync();

    // Assert
    await Assert.That(result).IsEqualTo("async-result");
}
```

---

## 4. 运行测试

### 4.1 重要：.NET 10 不支持 dotnet test

.NET 10 SDK 移除了 VSTest 模式，`dotnet test` **不再可用**。
必须使用 `dotnet run --project ...` 运行 TUnit 测试项目。

### 4.2 核心应用测试

```bash
# 先构建（可选，--no-build 跳过构建）
dotnet build tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj

# 运行测试
dotnet run --project tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj --no-build
```

### 4.3 CLI 测试

CLI 测试跨平台，验证生成器、脚手架、构建器逻辑：

```bash
dotnet run --project tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj
```

### 4.4 平台特定测试

**Windows 测试**：必须在 Windows 上运行（依赖 Win32 API、注册表、WinForms 剪贴板）：

```bash
dotnet run --project tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj
```

**Linux 测试**：必须在 Linux 或 WSL 中运行（依赖 GirCore/GTK4 原生库）：

```bash
# 在 WSL 中运行
wsl -d kali-linux -- bash -c "cd /mnt/f/Code/Dotnet/Wails.Net && \
  dotnet run --project tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj"
```

---

## 5. 测试覆盖要求

| 组件类型 | 覆盖要求 |
|---------|---------|
| 公共 API | **100% 方法覆盖** |
| 错误路径 | 所有 `catch` 分支必须测试 |
| 边界条件 | 空值、空集合、最大值 |
| 并发场景 | 共享状态的线程安全验证 |

### 5.1 错误路径

绑定调用异常需解包 `TargetInvocationException`，并区分 `CallErrorKind`：

```csharp
[Test]
public async Task Call_ThrowException_ReturnsRuntimeError()
{
    var bindings = new BindingManager();
    bindings.Add(new TestService());
    var result = bindings.Call("...ThrowException");
    await Assert.That(result.IsSuccess).IsFalse();
    await Assert.That(result.Error!.Kind).IsEqualTo(CallErrorKind.RuntimeError);
}
```

### 5.2 边界条件与并发

```csharp
[Test]
public async Task FNV1aHash_EmptyString_ReturnsOffsetBasis()
{
    await Assert.That(BindingManager.FNV1aHash("")).IsEqualTo(2166136261u);
}

[Test]
public async Task IdGenerator_ConcurrentCalls_ReturnsUniqueIds()
{
    var generator = new IdGenerator();
    var results = new ConcurrentBag<int>();
    Parallel.For(0, 1000, _ => results.Add(generator.Next()));
    await Assert.That(results.Distinct().Count()).IsEqualTo(1000);
}
```

---

## 6. Mock — NSubstitute 使用

使用 `NSubstitute` 创建接口的 mock 实现，避免依赖真实平台资源。

### 6.1 创建 Mock

```csharp
using NSubstitute;

var context = Substitute.For<IPluginContext>();
context.Services.Returns(new ServiceCollection());
context.Commands.Returns(new CommandRegistry());
context.Configuration.Returns(new ConfigurationBuilder().Build());
context.LoggerFactory.Returns(LoggerFactory.Create(_ => { }));
```

### 6.2 验证调用

```csharp
// 验证方法被调用
context.Received(1).SomeMethod(Arg.Any<string>());

// 验证方法未被调用
context.DidNotReceive().SomeMethod();
```

### 6.3 完整示例

参考 [BuiltInPluginsExtendedTests.cs](../../tests/Wails.Net.Application.Tests/BuiltInPluginsExtendedTests.cs)：

```csharp
private static IPluginContext CreatePluginContext()
{
    var services = new ServiceCollection();
    var commands = new CommandRegistry();
    var config = new ConfigurationBuilder().Build();
    var loggerFactory = LoggerFactory.Create(_ => { });

    var context = Substitute.For<IPluginContext>();
    context.Services.Returns(services);
    context.Commands.Returns(commands);
    context.Configuration.Returns(config);
    context.LoggerFactory.Returns(loggerFactory);
    return context;
}

[Test]
public async Task ProcessPlugin_Configure_RegistersCommands()
{
    // 安排
    var plugin = new ProcessPlugin();
    var context = CreatePluginContext();

    // 操作
    await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

    // 断言
    await Assert.That(context.Commands.Count).IsEqualTo(4);
    var names = context.Commands.GetCommandNames().ToList();
    await Assert.That(names.Contains("process.exit")).IsTrue();
}
```

---

## 7. 测试最佳实践

1. **测试独立性**：每个测试可独立运行，不依赖执行顺序。
2. **使用 `[NotInParallel]`**：涉及共享状态的测试类必须标记，避免并发冲突。
3. **私有嵌套类**：测试专用辅助类作为私有嵌套类定义在测试类内部（参考 `BindingsTests.TestService`）。
4. **断言要具体**：避免仅断言 `IsNotNull`，应断言具体值或属性。
5. **中文 XML 文档**：测试类需有中文 XML 文档注释说明测试目的。
6. **不跳过测试**：测试未通过前不得进入下一子任务。
7. **平台隔离**：平台特定测试放在对应平台测试项目，避免跨平台失败。
8. **构建优先**：运行测试前先 `dotnet build`，定位编译错误更快。

---

**参考文档**：[AGENTS.md §4 测试规范](../../AGENTS.md) | [贡献指南](./contributing.md)
