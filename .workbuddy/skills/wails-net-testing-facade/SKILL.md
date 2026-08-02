---
name: wails-net-testing-facade
description: 在 Wails.Net 中搭建 Mock 测试门面 / 接入 GuiContract 第 4 平台 / 写源生成器与禁反射测试时复用。涵盖 C# 重载决议陷阱、WailsTestHost 模式、TUnit 链接源码约束、测试运行方式。当用户要在本仓库新增无 GUI 测试、扩展 Wails.Net.Testing、或落地阶段 B（源生成器快照 / 禁反射守卫）时使用。
---

# Wails.Net 测试门面搭建（Mock / GuiContract / 源生成器）

跨会话复用的工作流。前置必读：[`.workbuddy/memory/MEMORY.md`](../../../.workbuddy/memory/MEMORY.md)（项目定位、架构红线、如何跑测试、挂起问题清单）。

## 0. 架构红线（不可破）
- 生产代码 `src/` 严禁运行时反射（AGENTS.md §3.4）。绑定走 `Wails.Net.SourceGenerators` 编译期强类型 invoker，`[ModuleInitializer]` 注册到 `GeneratedBindingRegistry.TryGetInvoker`。
- 全名格式 `Namespace.ClassName.MethodName`。`CancellationToken` 严禁作前端可调用的业务参数，须从 `ICommandContext.CancellationToken` 取。
- 测试框架 **TUnit 1.58.0**，禁用 MSTest/xUnit/NUnit。脚本用 F#，禁 Python。

## 1. ★ 致命陷阱：C# 重载决议（先读这个）
门面 API 若有「`params` 前带可选数值参数」的重载，第一个**数值**实参会**被吞成该可选参数**，导致参数错位且**静默**。

```csharp
// ❌ 灾难：windowId 在 params 之前
Task<InvokeResult> InvokeAsync<T>(string methodName, uint? windowId = null, params object?[] args);
// Add(2, 3)  →  windowId=2, args=[3]  → 结果错误且无异常
// Greet("World")  → "World" 不可转 uint?  → 碰巧走默认 windowId=1 而通过（掩盖 bug）

// ✅ 修复：把可选数值参数放到 params 之后作尾参
Task<InvokeResult> InvokeAsync<T>(string methodName, object?[] args, uint? windowId = null);
```
**任何测试门面 / DSL 都不要写成 `params` 在前、可选数值参数在中间。** 这是阶段 A 那 4 个 A2 失败的根因。

## 2. Mock 测试门面模式（WailsTestHost）
`Wails.Net.Testing`（在 `src/`，可发布 NuGet）= 无头 Mock 运行时，对标 Tauri `get_ipc_response`，无 GUI 跑通 传输层 → MessageProcessor → BindingManager 全链路。

- 门面：`WailsTestHost` / `WailsTestHostBuilder`。`CreateBuilder().AddService(...).AddPlugin(...).Build()` → `host.InvokeAsync("Ns.Class.Method", args)`。
- Mock 平台实现：`MockPlatformApp` / `MockWebviewWindow`（内存状态机，Set/Get 对称可回读 + `CallRecorder` 全量调用记录）。
- **注册（零反射，守 §3.4）**：`[ModuleInitializer]` 调 `PlatformFactory.RegisterPlatformApp("mock", ...)` + `RegisterClipboard(...)`。
- **隔离**：`PlatformFactory._platformAppFactories` 是静态字典 → `WailsTestHost` 必须 `[NotInParallel]` 或在 `DisposeAsync` 调 `ClearRegistrations()`，避免测试串味。
- 激活推荐：`WailsTestHostBuilder` 内部直接调委托，不走 `WAILS_PLATFORM=mock` 检测链（无全局副作用）。

## 3. 接入 GuiContract 第 4 平台（锁住 Mock 与真实实现一致）
Mock 必须继承 `WebviewWindowContractTests` 抽象基类，作为第 4 个平台受 109 条契约约束（相对 Tauri MockRuntime 的改进点，防漂移）。

```csharp
[InheritsTests]
public sealed class MockWebviewWindowContractTests : WebviewWindowContractTests
{
    protected override IWebviewWindowFixture GetFixture() => new MockWebviewWindowFixture();
}
internal sealed class MockWebviewWindowFixture : IWebviewWindowFixture
{
    public string PlatformName => "mock";
    public bool HasRealGuiEnvironment => true;   // Mock 必须通过 L1+L2 全部契约
    public void RunOnUiThread(Action action) => action();
}
```
**★ TUnit 硬约束**：源生成器**不能跨程序集发现基类 `[Test]`**。平台项目必须用 `<Compile Include="..\GuiContract.Tests\*.cs" Link="..." />` **链接基类源码**，不能用 `ProjectReference`（后者会静默少跑测试）。

## 4. 如何跑测试（本机）
- 离线构建必须加 `-p:NuGetAudit=false`（`NU1900` 审计失败）。
- TUnit 走 **MTP**，不是 `dotnet test`：
  `dotnet build tests/<Proj>/<Proj>.csproj -p:NuGetAudit=false` → `dotnet tests/<Proj>/bin/Debug/net10.0/<Proj>.dll`
- 过滤不直接支持 `--filter`；`--treenode-filter "*XxxTests*"` 有时匹配 0，必要时跑全量再 grep。

## 5. 阶段 B：源生成器测试（B1）与禁反射守卫（B2）
- **B1 源生成器测试（已实现 ✅）**：`tests/Wails.Net.SourceGenerators.Tests` 已落地，5 测试全绿。要点：
  - 手动驱动 `CSharpGeneratorDriver`（`new BindingSourceGenerator()`）；**禁用** `Microsoft.CodeAnalysis.*.Testing` Verifier 包（绑死 xUnit/MSTest，违反 AGENTS.md）。
  - 引用集用 `TRUSTED_PLATFORM_ASSEMBLIES` + `Wails.Net.Application` 本地依赖 DLL（在 `typeof(BindingAttribute).Assembly.Location` 目录枚举 `*.dll`），**替代离线不可得的 `Basic.Reference.Assemblies.*`**。
  - 快照用「结构化断言（生成文本含方法名/`GeneratedBindingRegistry`/`ModuleInitializer`）+ 确定性（两次运行文本一致）+ 语法有效性（再 ParseText 无错误）+ 生成代码可编译无错误（对接真实 Wails 类型）」替代 `Verify.TUnit` 全文快照——**因为本机 NuGet 缓存无 `Verify.TUnit` 与 `Basic.Reference.Assemblies.*`，联网后可平滑替换为 Verify.TUnit**。
  - **关键坑**：`BindingSourceGenerator` **不发射任何 Diagnostic**，`[Test]` 别断言生成器诊断（§3.4.6 `CancellationToken` 业务参数无编译期诊断，仍靠 review / 未来 WAILS0001）。
- **B2 禁反射守卫**：`tests/Wails.Net.Application.Tests/Architecture/ReflectionGuardTests.cs` 扫描 `src/`，禁止 `MethodInfo.Invoke` / `Activator.CreateInstance` / `Type.GetMethod` / `.GetTypes()` / `MakeGenericMethod` / `Delegate.CreateDelegate`；白名单仅 3 处：`PlatformFactory.cs`（`Assembly.Load`+`RuntimeHelpers.RunModuleConstructor`）、`BindingManager.cs`（`GetType().Name`/`Namespace` 取字符串）、源生成器本身 Roslyn 分析。

## 6. 仍挂起的旧问题（动手前先核对）
1. `Directory.Packages.props:74-78` 残留 `Microsoft.NET.Test.Sdk`/`MSTest.*`/`FluentAssertions`（违反禁 MSTest）→ 建议删。
2. `Wails.Net.AssetServer` 缺 `InternalsVisibleTo`。
3. `testing-guide.md` §5.1 过期（仍写解包 `TargetInvocationException`，与 §3.3 冲突）；§2 项目表缺 Android/GuiContract/AssetServer。
4. CI 的 `test-linux`/`test-android`/`test-android-e2e` 全 `continue-on-error: true`（无门禁）。
5. 契约测试接线应统一为链接源码 + 加契约计数元测试（断言三平台契约数一致）。
