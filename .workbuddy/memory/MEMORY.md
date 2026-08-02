# Wails.Net 项目记忆（长期）

> 跨会话的"接手须知"。每日细节见 `2026-MM-DD.md`，本文件只放稳定事实与当前进度快照。

## 项目定位
Wails v3 (Go) 的 .NET 10 移植，吸收 Tauri v2 的插件/安全/能力模型。
技术栈（不可更改）：Microsoft.Extensions.*（Hosting/DI/Config/Logging）、Windows WebView2、Linux GirCore 0.8.0、Android .NET Android 工作负载、**TUnit 1.58.0 测试框架（禁 MSTest/xUnit/NUnit）**、脚本用 F#（禁 Python）、`TreatWarningsAsErrors=true`。

## 架构红线（AGENTS.md §3.4）
- **生产代码 `src/` 严禁运行时反射**发现/调用方法。绑定走 `Wails.Net.SourceGenerators` 编译期生成强类型 invoker，经 `[ModuleInitializer]` 注册到 `GeneratedBindingRegistry.TryGetInvoker(fullName, out var invoker)`。
- 全名格式 `Namespace.ClassName.MethodName`（短别名 `ClassName.MethodName`）。
- 白名单（仅 3 处已评审例外）：`PlatformFactory.cs` 的 `Assembly.Load`+`RuntimeHelpers.RunModuleConstructor`；`BindingManager.cs` 的 `GetType().Name`/`Namespace` 取字符串；源生成器本身的 Roslyn 分析。
- `CancellationToken` 严禁作为前端可调用的业务参数（JSON 无法序列化），须从 `ICommandContext.CancellationToken` 取（§3.4.6）。建议未来升级为编译期诊断 `WAILS0001`。

## 测试基础设施（已落地）
- 7 个测试工程、约 2283 个 `[Test]`，全 TUnit，无框架混用。
- **GuiContract 契约测试**（领先设计）：`WebviewWindowContractTests`(抽象基类) + `IWebviewWindowFixture`，L1/L2/L3 分级约 109 项；三真实平台各自实现。TUnit 源生成器**不能跨程序集发现基类 `[Test]`** → 平台项目用 `<Compile Include Link>` 链接基类源码，不能用 `ProjectReference`。
- **`Wails.Net.Testing`**（放 `src/`，可发布 NuGet）：无头 Mock 运行时。`WailsTestHost`/`WailsTestHostBuilder` 门面 = 对标 Tauri `get_ipc_response`，无 GUI 跑通 传输层→MessageProcessor→BindingManager 全链路。
- **Mock 是第 4 个 GuiContract 平台**（`HasRealGuiEnvironment => true`，锁定 L1+L2）。

## 当前进度（截至 2026-08-02）
- ✅ **阶段 A 完成**：`Wails.Net.Testing` + MockPlatform + 接入 GuiContract。新建 `tests/Wails.Net.Testing.Tests`（8 个 A2 + 108 个 A3 继承契约），**共 116 测试全绿**。
- ✅ **阶段 B 完成**：源生成器测试 + 禁反射守卫。
  - **B1** 源生成器测试（`tests/Wails.Net.SourceGenerators.Tests`，`CSharpGeneratorDriver` 驱动 `BindingSourceGenerator`，5 测试全绿；离线以结构化断言+确定性+语法有效+生成代码可编译性替代 `Verify.TUnit`，因缓存无此包）。
  - **B2** 禁反射守卫（`tests/Wails.Net.Application.Tests/Architecture/ReflectionGuardTests.cs` 扫描 `src/`，1 测试全绿；注释/字符串已剥离，白名单 3 处例外）。
- ⏭️ **下一步 = 阶段 C**：`Events`/`Errors`/`MacOS` 补测 + 覆盖率门禁开启（依赖 A' 覆盖率基线）。
- 设计文档 `docs/development/testing-strategy.md`（战略层，路线图状态列已更新）+ `testing-guide.md`（战术层，§5.1 仍待修订，见附录 A 第 3 项）。

## 如何跑测试（本机环境）
- 离线构建需 `-p:NuGetAudit=false`（`NU1900` 审计失败）。
- 单跑某工程：`dotnet build tests/<Proj>/<Proj>.csproj -p:NuGetAudit=false` → `dotnet tests/<Proj>/bin/Debug/net10.0/<Proj>.dll`（TUnit 走 MTP，非 `dotnet test`）。
- TUnit 过滤不直接支持 `--filter`；`--treenode-filter "*XxxTests*"` 有时匹配 0，必要时跑全量再 grep。

## 已知可复用陷阱
- **C# 重载决议陷阱（严重）**：门面 API 若有 `(string, uint? windowId, params object?[] args)` 这种 `params` 前带数值可选参数的重载，首个数值实参会**被吞成 windowId**，导致参数错位且静默。`WailsTestHost.InvokeAsync` 曾因此使 `Add(2,3)` 得 3、`SlowDoubleAsync(21)` 得 0。修复：把 `windowId` 放到 `params` **之后**作尾参。任何测试门面/DSL 都不要写成 `params` 在前、可选数值参数在中间。

## 仍挂起的旧问题（testing-strategy.md 附录 A）
1. `Directory.Packages.props:74-78` 残留 `Microsoft.NET.Test.Sdk`/`MSTest.*`/`FluentAssertions`（违反禁 MSTest）→ 建议删。
2. `Wails.Net.AssetServer` 缺 `InternalsVisibleTo`。
3. `testing-guide.md` §5.1 过期（仍写解包 `TargetInvocationException`，与 §3.3 冲突）；§2 项目表缺 Android/GuiContract/AssetServer。
4. CI 的 `test-linux`/`test-android`/`test-android-e2e` 全 `continue-on-error: true`（无门禁）。
5. 契约测试接线应统一为链接源码 + 加计数元测试（断言三平台契约数一致）。
