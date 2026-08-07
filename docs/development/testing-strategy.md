# Wails.Net 测试方案设计

> 本文档基于对 **Wails v3 (Go)** 与 **Tauri v2 (Rust)** 测试体系的对标分析，设计 Wails.Net 的分层测试架构。
>
> - 本文回答**「测什么、怎么分层、缺什么」**（战略层）
> - 具体的**「怎么写一个测试」**（战术层）见 [testing-guide.md](./testing-guide.md)
>
> 关联：[AGENTS.md](../../AGENTS.md) · [ADR-0003 源生成器绑定](../ADR/0003-source-generator-for-bindings.md)

> **🔖 接手入口（2026-08-02）**：新窗口 / 新会话请先读 [`.workbuddy/memory/MEMORY.md`](../../.workbuddy/memory/MEMORY.md) —— 它汇总了**项目定位、架构红线（§3.4 禁反射 / 全名格式 / `CancellationToken` 白名单）、测试基础设施现状（`Wails.Net.Testing` Mock 运行时、GuiContract 第 4 平台、TUnit 链接源码约束）、当前进度快照、如何跑测试、已知可复用陷阱（C# 重载决议陷阱）与挂起问题清单（附录 A 五项）**，是跨会话的「接手须知」。本文件是其战略层路线图补充。

---

## 1. 背景

Wails.Net 是 Wails v3 的 .NET 10 移植，同时在插件/安全/能力模型上吸收 Tauri v2 的设计。测试体系也应当同样"融合"：

| 领域 | 对标对象 | 理由 |
|------|---------|------|
| 后端逻辑单测组织 | Wails v3 | Service-centric，业务逻辑天然可测 |
| **无头运行时 / IPC 全链路测试** | **Tauri v2** | MockRuntime 是 Tauri 最强的一环，Wails 完全缺失 |
| 前端 Runtime mock | Tauri v2 | `@tauri-apps/api/mocks` 是官方一等公民 |
| 桌面 E2E | Tauri v2 | 驱动真实打包二进制，比 Wails 的 dev-server Playwright 更可信 |
| 平台原生层契约 | **两者皆无，Wails.Net 自创** | 本项目已有的 GuiContract 是领先设计，应强化而非替换 |

---

## 2. 上游方案分析

### 2.1 Wails v3 (Go)

**工具链**：语言原生，零额外框架。

```bash
go test ./...                              # 全量
go test ./pkg/application -v               # 单包
go test ./... -race                        # 竞态检测
go test ./... -coverprofile=coverage.out   # 覆盖率
go tool cover -html=coverage.out
```

**组织方式**

- 测试与源码同包同目录（`xxx_test.go`），可直接访问包内私有符号
- 内部测试包：`v3/pkg/application/internal/tests`
- 官方力荐**表驱动测试**（table-driven）
- Mock 靠**手写 interface + 假实现 struct**，无 mock 框架

**核心思路：把逻辑挤出 GUI 层**

Wails 的 Service 是普通 Go struct，注册进 `application.Options.Services` 即可被绑定。因此测试直接 `new` 一个 Service 测方法，完全不碰 application：

```go
func TestUserService_Create(t *testing.T) {
    service := &UserService{users: make(map[string]*User)}
    user, err := service.Create("john@example.com", "password123")
    // ...
}
```

**前端**：Vitest + `vi.mock()` 直接 mock 生成的 binding 模块。

**E2E**：Playwright 连 dev server（`http://localhost:9245`），断言 DOM 与窗口标题/尺寸。

**结论 — 优点与硬伤**

| ✅ 优点 | ❌ 硬伤 |
|--------|--------|
| 零框架负担，工具链原生 | **平台层几乎不可测**：cgo / Win32 / GTK 直接调用，无抽象层 |
| Service 设计让业务逻辑天然可测 | **没有 mock runtime**，无法在无 GUI 环境跑通 IPC 全链路 |
| `-race` 是并发正确性的强力保障 | E2E 打的是 dev server 而非打包产物，与生产形态有差 |
| | 窗口 / 菜单 / 托盘 / 对话框全靠人工验证 |

### 2.2 Tauri v2 (Rust)

Tauri 明确划分**三层**，职责边界清晰。

#### 第 1 层：Rust 单测 + `MockRuntime`（杀手锏）

启用 `tauri = { features = ["test"] }` 后，`tauri::test` 模块提供一整套**假运行时**：

| API | 作用 |
|-----|------|
| `mock_builder()` | 用 `MockRuntime` 替换真实 wry/tao runtime |
| `mock_context(noop_assets())` | 假 Context + 空 Assets |
| `MockWebviewDispatcher` / `MockWindowDispatcher` / `MockWindowBuilder` | 完整实现 `Runtime` trait 的假实现 |
| `get_ipc_response()` / `assert_ipc_response()` | 构造 `InvokeRequest` 跑完整 IPC 管线并断言 |
| `INVOKE_KEY` | 绕过 invoke key 校验的测试常量 |

```rust
let app = create_app(mock_builder());
let webview = tauri::WebviewWindowBuilder::new(&app, "main", Default::default())
    .build().unwrap();

let res = tauri::test::get_ipc_response(&webview, tauri::webview::InvokeRequest {
    cmd: "ping".into(),
    callback: tauri::ipc::CallbackFn(0),
    error: tauri::ipc::CallbackFn(1),
    url: "http://tauri.localhost".parse().unwrap(),
    body: tauri::ipc::InvokeBody::default(),
    headers: Default::default(),
    invoke_key: tauri::test::INVOKE_KEY.to_string(),
}).map(|b| b.deserialize::<String>().unwrap());
```

**关键价值**：能建窗口、能注入 `State`、能挂插件、能跑完整 IPC —— **全程没有真实 webview**。CI 上无需显示器即可覆盖后端全栈。

社区共识的配套实践：**业务逻辑抽成纯函数，`#[tauri::command]` 只做薄包装**。

#### 第 2 层：前端 mock（`@tauri-apps/api/mocks`）

| API | 作用 |
|-----|------|
| `mockIPC(cb, opts)` | 拦截 `window.__TAURI_INTERNALS__.invoke` |
| `mockWindows('main','second')` | 伪造窗口标签（只造存在性，不造属性） |
| `mockConvertFileSrc` | 伪造资源协议转换 |
| `clearMocks()` | **每个测试后必调**，否则状态串味 |
| `{ shouldMockEvents: true }` | 2.7.0+ 支持 `listen`/`emit`（`emitTo`/`emit_filter` 尚不支持） |

配 Vitest + jsdom（需手动补 WebCrypto），可用 `vi.spyOn` 断言调用次数。

#### 第 3 层：WebDriver E2E

- **推荐**：`@wdio/tauri-service`，Windows/Linux/macOS 全覆盖
- 默认 **embedded WebDriver server**（应用内嵌 `tauri-plugin-wdio-webdriver`），无需外部 driver —— **macOS 因此得以支持**
- 或走 `tauri-driver` 驱动原生 driver（msedgedriver / WebKitWebDriver），**仅 Windows + Linux**
- `tauri-plugin-wdio` 提供 `browser.tauri.execute()`、IPC mock、前后端日志抓取
- 另有 **browser mode**：Chrome + Vite dev server 跑纯渲染层快测，不需要二进制
- 驱动对象是 **真实打包二进制**（`appBinaryPath`）

**结论**

| ✅ 优点 | ❌ 硬伤 |
|--------|--------|
| MockRuntime 让后端全栈可测且不需 GUI | `tauri::test` 官方标注 **unstable** |
| 三层职责边界极清晰 | MockRuntime **无契约保证**与真实 runtime 行为一致，存在漂移风险 |
| E2E 打真实二进制，可信度高 | 原生行为（窗口特效、菜单）仍只能靠 E2E / 人工 |
| 前端 mock 是官方一等公民 | |

### 2.3 横向对比与 Wails.Net 现状定位

| 维度 | Wails v3 | Tauri v2 | **Wails.Net 现状** |
|------|----------|----------|-------------------|
| 后端单测 | `go test` 原生 | `cargo test` | ✅ TUnit，**2283 个 `[Test]`**，7 个工程 |
| 无头运行时（测试用） | ❌ 无 | ✅ MockRuntime | ⚠️ 有 `ServerMode`（625 行）但**定位是生产降级，非测试设施** |
| IPC 全链路断言 | ❌ | ✅ `get_ipc_response` | ⚠️ 仅 `CommandTestHelper`，覆盖 Command 层未及传输层 |
| **平台原生层测试** | ❌ | ❌ | ✅✅ **GuiContract 三平台分级契约（L1/L2/L3，109 项）— 领先两个上游** |
| 前端 Runtime mock | 手工 `vi.mock` | ✅ 官方 mocks | ❌ 无（`Runtime.Js` 1141 行**零测试**） |
| 桌面 E2E | Playwright + dev server | WebDriver + 真实二进制 | ⚠️ 仅 Android adb 脚本（`run-android-e2e.fsx`） |
| 覆盖率 | `go cover` | `cargo-llvm-cov` | ❌ **完全没有** |
| 竞态 / 并发 | ✅ `-race` | — | ⚠️ 零星 `Parallel.For` 压测 |
| 源生成器测试 | N/A | trybuild | ❌ **1402 行零测试**（禁反射架构的基石） |

**一句话定位**：Wails.Net 的**广度**（2283 个测试）与**平台契约深度**已超越两个上游，但缺三样 Tauri 有的关键基础设施——**无头 Mock 运行时、前端 mock、覆盖率门禁**，外加一个自身架构特有的空白——**源生成器与禁反射约束无自动化验证**。

---

## 3. 目标架构：五层测试金字塔

```
                    ┌─────────────────────────────┐
        L4          │  桌面 E2E（真实二进制）      │  nightly / tag
                    │  Win CDP · Linux WebKitWD    │  ~10 用例
                    │  Android adb（已有）         │
                    ├─────────────────────────────┤
        L3          │  GUI 契约测试（真实原生）    │  平台 CI
                    │  Windows / Linux / Android   │  109 × 3
                    │  ★ + MockPlatform 第 4 实现  │
                    ├─────────────────────────────┤
        L2          │  MockPlatform 集成测试 ★新   │  每 PR，全平台可跑
                    │  Host+DI+IPC+窗口+事件全链路 │  目标 ~300
                    ├─────────────────────────────┤
        L1          │  组件单测（TUnit）           │  每 PR
                    │  已有 2283 个                │
                    ├─────────────────────────────┤
        L0          │  编译期守卫                  │  每次构建
                    │  源生成器快照 · 禁反射扫描   │  ★新
                    │  AOT 零警告 · TreatWarnAsErr │
                    └─────────────────────────────┘

  旁路：前端 Vitest（Runtime.Js + mocks 包）★新
        覆盖率门禁 · 并发压测 · MSBuild 打包集成 ★新
```

**设计原则**

1. **越往下越多、越快、越强制**。L0/L1/L2 必须在每个 PR 全绿，L3/L4 允许分级降级。
2. **L2 是新增的重心** —— 对标 Tauri MockRuntime，把"需要 GUI 才能测"的东西压到不需要 GUI。
3. **L2 的 Mock 必须受 L3 契约约束**，避免 Tauri MockRuntime 那种"mock 与真实行为漂移"的问题。这是本方案相对 Tauri 的改进点。

---

## 4. 核心新增件详细设计

### 4.1 ★ `Wails.Net.Testing` —— 无头 Mock 运行时（最高优先级）

#### 4.1.1 定位决策

**放 `src/` 而非 `tests/`，作为可发布 NuGet 包。**

理由：Tauri 把 `tauri::test` 放在主 crate 里用 feature 开关，就是因为**下游用户写自己的 App 也需要它**。Wails.Net 的用户需要能测自己的 Service/Plugin 而不启动 WebView2/GTK。这是产品能力，不是内部测试代码。

```
src/Wails.Net.Testing/
├── Platform/
│   ├── MockPlatformApp.cs        # IPlatformApp（约 30 成员）
│   ├── MockWebviewWindow.cs      # IWebviewWindowImpl（102 成员）
│   ├── MockClipboard.cs          # IClipboardImpl
│   └── MockPlatformRegistrar.cs  # [ModuleInitializer] 注册 "mock" 平台
├── Recording/
│   ├── CallRecord.cs             # 单次调用记录（成员名 + 参数 + 时间戳 + 线程）
│   └── CallRecorder.cs           # 线程安全记录器
├── WailsTestHost.cs              # 门面：起一个无 GUI 的完整 Application
└── WailsTestHostBuilder.cs
```

#### 4.1.2 与现有 `ServerMode` 的关系

`src/Wails.Net.Application/Platform/ServerMode/` 已有 625 行 no-op 桩实现，是现成地基：

| | ServerMode（现有） | Mock（新增） |
|---|---|---|
| 定位 | 生产降级路径（无 GUI 环境仍能跑） | 测试设施 |
| 行为 | 纯 no-op，`SetTitle` 后 `GetTitle` 拿不回来 | **内存状态机**，Set/Get 对称可回读 |
| 可观测性 | 无 | 全量调用记录 + 可编程返回值 |

**实现策略**：`MockWebviewWindow` 不继承 `ServerWebviewWindow`（no-op 语义会拖累），而是**独立实现 + 内存状态机**，但复用其成员清单与 XML 注释结构。

#### 4.1.3 关键 API 草案

```csharp
// —— 内存状态机 + 调用记录 ——
public sealed class MockWebviewWindow : IWebviewWindowImpl
{
    private readonly CallRecorder _recorder;
    private string _title = string.Empty;
    private (int W, int H) _size = (800, 600);

    public IReadOnlyList<CallRecord> Calls => _recorder.Snapshot();

    public void SetTitle(string title)
    {
        _recorder.Record(nameof(SetTitle), title);
        _title = title;                       // ★ 真实存储，可回读
    }

    public string GetTitle()
    {
        _recorder.Record(nameof(GetTitle));
        return _title;
    }
    // ... 其余 100 个成员
}

// —— 测试门面 ——
public sealed class WailsTestHost : IAsyncDisposable
{
    public static WailsTestHostBuilder CreateBuilder();

    public MockPlatformApp Platform { get; }
    public IServiceProvider Services { get; }
    public EventBus Events { get; }

    /// 对标 Tauri get_ipc_response：走完整 传输层 → MessageProcessor → BindingManager
    public Task<InvokeResult> InvokeAsync(string command, object? args = null, uint windowId = 1);

    /// 对标 Tauri assert_ipc_response
    public Task AssertInvokeAsync<T>(string command, object? args, T expected);

    public MockWebviewWindow CreateWindow(WebviewWindowOptions? options = null);
}
```

使用效果（对比 Tauri 的 `get_ipc_response`）：

```csharp
[Test]
public async Task GreetCommand_ReturnsGreeting()
{
    // Arrange —— 无 WebView2、无 GTK、无显示器
    await using var host = WailsTestHost.CreateBuilder()
        .AddService(new GreetService())
        .AddPlugin(new ClipboardPlugin())
        .Build();

    // Act
    var result = await host.InvokeAsync("GreetService.Greet", new { name = "World" });

    // Assert
    await Assert.That(result.IsSuccess).IsTrue();
    await Assert.That(result.Value<string>()).IsEqualTo("Hello, World!");
}

[Test]
public async Task SetTitle_RecordedAndReadable()
{
    await using var host = WailsTestHost.CreateBuilder().Build();
    var window = host.CreateWindow();

    window.SetTitle("测试标题");

    await Assert.That(window.GetTitle()).IsEqualTo("测试标题");          // 状态机
    await Assert.That(window.Calls).Contains(c => c.Member == "SetTitle"); // 调用记录
}
```

#### 4.1.4 注册方式（严守 AGENTS.md §3.4 禁反射）

复用现有零反射机制，不引入任何新的反射路径：

```csharp
internal static class MockPlatformRegistrar
{
    [ModuleInitializer]
    internal static void Register()
    {
        PlatformFactory.RegisterPlatformApp("mock", opts => new MockPlatformApp(opts));
        PlatformFactory.RegisterClipboard("mock", () => new MockClipboard());
    }
}
```

激活途径二选一：
- 环境变量 `WAILS_PLATFORM=mock`（`PlatformFactory.DetectPlatformOrNull` Level 2 已支持，**需追加 `mock` 到白名单**）
- `WailsTestHostBuilder` 内部直接调用委托，不走检测链（推荐，无全局副作用）

> ⚠️ 实施注意：`PlatformFactory` 的 `_platformAppFactories` 是**静态字典**。`WailsTestHost` 必须 `[NotInParallel]` 或在 `DisposeAsync` 中调用 `ClearRegistrations()` 复原，避免测试间串味。

#### 4.1.5 ★ 用契约测试锁住 Mock 与真实实现的一致性

这是本方案**优于 Tauri** 的关键一招。Tauri 的 MockRuntime 没有任何机制保证它和真实 runtime 行为一致；本项目已有 `WebviewWindowContractTests`（2237 行抽象基类），直接把 Mock 接成第 4 个平台：

```csharp
// tests/Wails.Net.Testing.Tests/MockWebviewWindowContractTests.cs
[InheritsTests]
public sealed class MockWebviewWindowContractTests : WebviewWindowContractTests
{
    protected override IWebviewWindowFixture GetFixture() => new MockWebviewWindowFixture();
}

internal sealed class MockWebviewWindowFixture : IWebviewWindowFixture
{
    public string PlatformName => "mock";
    public bool HasRealGuiEnvironment => true;   // ★ Mock 必须通过 L1+L2 全部契约
    public void RunOnUiThread(Action action) => action();
    // ...
}
```

**效果**：Mock 的状态机语义被 109 条契约强制约束，与三个真实平台同源。任何一方偏离立刻红灯。

---

### 4.2 ★ 源生成器测试（当前最大缺口）

`Wails.Net.SourceGenerators` 有 **1402 行、0 测试**。它是「禁止反射」整个架构的基石——一旦生成错误代码，全项目 IPC 静默失效。

#### 框架选型注意

`Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` 的 Verifier 包**绑死 xUnit/NUnit/MSTest**，与 AGENTS.md 冲突。

**方案**：手动驱动 `CSharpGeneratorDriver`（框架无关）+ `Verify.TUnit` 做快照。

```csharp
// tests/Wails.Net.SourceGenerators.Tests/
private static GeneratorDriverRunResult RunGenerator(string source)
{
    var compilation = CSharpCompilation.Create("TestAsm",
        [CSharpSyntaxTree.ParseText(source)],
        Basic.Reference.Assemblies.Net100.References.All,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    return CSharpGeneratorDriver
        .Create(new BindingGenerator())
        .RunGeneratorsAndUpdateCompilation(compilation, out _, out _)
        .GetRunResult();
}

[Test]
public async Task Binding_SimpleMethod_GeneratesInvoker()
    => await Verify(RunGenerator(Sources.SimpleService));   // Verify.TUnit 快照
```

#### 必测清单

| 类别 | 用例 |
|------|------|
| 正向 | `[Binding]` 识别、invoker 签名、异步方法、`ICommandContext` 注入、泛型/可空/记录类型参数 |
| **诊断** | 重复命令名、不支持的参数类型、非 public 方法、**§3.4.6 `CancellationToken` 作为业务参数** |
| 增量性 | 无关编辑不触发重新生成（`IncrementalStepRunReason.Cached`） |
| 输出编译性 | 生成代码本身能通过 `TreatWarningsAsErrors` 编译 |

> **强烈建议**：把 AGENTS.md §3.4.6 的 `CancellationToken` 约定从"人肉遵守"升级为**编译期诊断**（`WAILS0001`）。当前 `UpdaterPlugin` / `CameraPlugin` 的正确写法完全靠 code review 保障，新插件极易踩坑并在运行时才暴露 JSON 序列化失败。

---

### 4.3 ★ 禁反射守卫 / 架构测试

AGENTS.md §3.4 是硬约束，但 §3.4.5 目前只写了「代码审查时关注」——纯人工。做成自动化测试：

```csharp
// tests/Wails.Net.Application.Tests/Architecture/ReflectionGuardTests.cs
[Test]
public async Task Src_MustNotUseRuntimeReflection()
{
    // §3.4.3 白名单：三处已评审的例外
    string[] whitelist =
    [
        "Platform/PlatformFactory.cs",      // Assembly.Load + RunModuleConstructor
        "Bindings/BindingManager.cs",       // GetType().Name 取字符串
    ];

    string[] banned =
    [
        @"\bMethodInfo\s*\.\s*Invoke", @"\bActivator\s*\.\s*CreateInstance",
        @"\bType\s*\.\s*GetMethod", @"\.GetTypes\(\)", @"\bMakeGenericMethod\b",
        @"\bDelegate\s*\.\s*CreateDelegate",
    ];

    var violations = ScanSrcDirectory(banned, whitelist);
    await Assert.That(violations)
        .IsEmpty()
        .Because($"AGENTS.md §3.4 禁止生产代码使用运行时反射：\n{string.Join('\n', violations)}");
}
```

配套：
- **AOT 冒烟**：CI 增加 `dotnet publish -p:PublishAot=true` 步骤，断言 `IL2xxx`/`IL3xxx` 警告数为 0
- **命名空间冲突守卫**：AGENTS.md 提到的 CS0118 陷阱，可加一条扫描测试

---

### 4.4 前端测试（`@wails-net/runtime` 1141 行零测试）

对标 `@tauri-apps/api/mocks`，为 `@wails-net/runtime` 增加 `/mocks` 子路径导出：

```js
// @wails-net/runtime/mocks
export function mockIPC(handler, { shouldMockEvents = false } = {});
export function mockWindows(current, ...others);
export function clearMocks();       // 每个测试后必调
```

```js
import { mockIPC, clearMocks } from '@wails-net/runtime/mocks'
import { Call } from '@wails-net/runtime'
import { afterEach, expect, test, vi } from 'vitest'

afterEach(clearMocks)

test('Call.ByName 走 IPC 并回传结果', async () => {
  mockIPC((cmd, args) => cmd === 'GreetService.Greet' ? `Hello, ${args.name}!` : undefined)
  const spy = vi.spyOn(window.__wails_internals__, 'invoke')

  await expect(Call.ByName('GreetService.Greet', { name: 'World' }))
    .resolves.toBe('Hello, World!')
  expect(spy).toHaveBeenCalledOnce()
})
```

**runtime.js 自身必测**：`Call.ByID` FNV-1a 一致性（必须与 C# 端 `BindingManager.FNV1aHash` 结果逐位相同）、事件总线订阅/退订、`CallError` 三种 Kind 的 JS 侧映射、并发调用的 callback id 不冲突。

> 跨语言哈希一致性建议做成**双向黄金用例**：同一份 `hash-cases.json` 同时被 C# 测试与 Vitest 消费。

---

### 4.5 桌面 E2E（分两级，避免一步到位）

现有 `run-android-e2e.fsx` 是很好的模板（adb → `am start` → logcat → `uiautomator dump` → `input tap`）。桌面侧参考 Tauri 的 WebDriver 路线：

| 级别 | 触发 | 手段 | 断言对象 |
|------|------|------|---------|
| **E2E-Lite** | 每 PR | 启动真实构建产物 → 通过 AssetServer HTTP 端口 + **测试后门 IPC**（named pipe）直接调命令 | 进程存活、窗口创建、命令返回值、日志无 ERROR |
| **E2E-Full** | nightly / tag | Windows：Playwright .NET over **WebView2 CDP**（`CallDevToolsProtocolMethodAsync`）<br>Linux：`WebKitWebDriver` + Selenium，`xvfb-run` 提供虚拟显示<br>Android：现有 fsx 脚本 | 真实 DOM 交互、截图对比 |

**测试后门**：对标 `tauri-plugin-wdio`，实现为一个 `Wails.Net.Testing.E2E` 插件，仅在 `WAILS_E2E=1` 时注册，暴露 named pipe 供测试进程调用。**绝不能进生产构建**——用 MSBuild 条件引用隔离。

统一 `WailsE2EFixture` 抽象，三平台共用生命周期（启动 → 就绪探测 → 操作 → 日志收集 → 清理）。

---

## 5. 横切能力

### 5.1 覆盖率门禁（当前完全没有）

项目已用 MTP（`dotnet run` 而非 `dotnet test`），因此**用 `Microsoft.Testing.Extensions.CodeCoverage` 而非 coverlet**：

```bash
dotnet run --project tests/Wails.Net.Application.Tests/... --no-build -c Release -- \
  --coverage --coverage-output-format cobertura --coverage-output app.cobertura.xml
```

`.runsettings` 排除：`obj/`、`bin/`、源生成产物（`*.g.cs`）、`Platform/ServerMode/`、`src/Wails.Net.Testing/`。

**分层阈值**（先立基线，逐季度上调）：

| 模块 | 行覆盖阈值 | 说明 |
|------|-----------|------|
| `Wails.Net.Application` 核心 | ≥ 80% | 排除 Platform 目录 |
| `Wails.Net.AssetServer` | ≥ 85% | 纯逻辑，无平台依赖 |
| `Wails.Net.Cli` | ≥ 70% | 含大量 IO |
| `Wails.Net.SourceGenerators` | ≥ 75% | 新增后适用 |
| `Wails.Net.Events` / `Errors` | ≥ 90% | 小而关键 |
| 三平台 Application.* | 不设门禁 | 单独看契约通过率 |

ReportGenerator 合并多工程报告 + PR 覆盖率增量评论。

### 5.2 并发正确性（补 Go `-race` 的缺）

.NET 没有 `-race` 等价物，分三档补：

1. **压测**（立即可做）：`BindingManager`、`EventBus`、ID 生成器、`ConcurrentDictionary` 路径做 `Parallel.For(0, 10_000)` + TUnit `[Repeat(20)]`
2. **主线程模型验证**：`DispatchOnMainThread` 的 FIFO 顺序性、重入安全、异常不吞（MockPlatformApp 可精确控制主线程队列，L2 层就能测）
3. **可选（Phase D）**：引入 **Microsoft.Coyote** 做系统化并发探索——这是 .NET 生态对 `-race` 最接近的答案，能确定性重放死锁/竞态

### 5.3 MSBuild 打包集成测试

`Wails.Net.Sdk` + 三个 `Bundle.*` 共 0 行 C#、纯 MSBuild props/targets、**零测试**。建议：

- 用临时项目 + `dotnet msbuild -t:WailsBundle -getProperty:...` 驱动，断言产物路径/清单/图标存在
- 现有 `scripts/pack-and-test.sh` 已做 CLI dotnet tool 安装冒烟，扩展为模板实例化 → 构建 → 打包全链路

---

## 6. CI 流水线重构

### 现状问题

`.github/workflows/ci.yml`（764 行）中 **`test-linux` / `test-android` / `test-android-e2e` 全部 `continue-on-error: true`**。等于这三条线**没有门禁**，可能长期红着无人察觉。

### 建议改造

| Job | 现状 | 建议 |
|-----|------|------|
| `test-generators` ★新 | — | **必须通过**，秒级，最先跑 |
| `test-architecture` ★新 | — | **必须通过**，禁反射扫描 + AOT 警告 |
| `test-mock-platform` ★新 | — | **必须通过**，ubuntu-latest 即可（无需 GUI） |
| `test-application` | 强制 | 保持 + 产出覆盖率 |
| `test-windows` | 强制 | 保持 |
| `test-linux` | 全 `continue-on-error` | **拆分**：L1 契约（容器可跑）强制 ✅ ／ L2+L3（需 `xvfb-run`）允许失败 |
| `test-android` | 全 `continue-on-error` | 同上拆分 |
| `test-frontend` ★新 | — | Vitest，**必须通过** |
| `coverage` ★新 | — | 合并报告 + 阈值门禁 + PR 评论 |
| `e2e-full` | Android only，main/tag | 扩展三平台，保持 nightly |

关键改进：**Linux 用 `xvfb-run` 提供虚拟显示**（Tauri CI 的标准做法），让 L2 功能契约在 CI 真跑起来，而不是因 `HasRealGuiEnvironment == false` 静默跳过。

### 契约测试接线统一

现状不一致，有风险：
- Windows 用 `<Compile Include="..\GuiContract.Tests\*.cs" Link="..." />` 链接源码
- Linux / Android 用 `ProjectReference`

因为 **TUnit 源生成器不能跨程序集发现基类 `[Test]`**，ProjectReference 路径可能静默少跑测试。**统一改为链接源码**，并加一条元测试断言三平台契约用例数一致：

```csharp
[Test]
public async Task ContractTestCount_MustMatchAcrossPlatforms()
    => await Assert.That(DiscoveredContractTests.Count).IsEqualTo(109);
```

---

## 7. 落地路线图

| 阶段 | 内容 | 产出 | 依赖 | 状态 |
|------|------|------|------|------|
| **A** | `Wails.Net.Testing` + MockPlatform + 接入 GuiContract 契约 | 无 GUI 跑通 IPC 全链路；Mock 受 109 契约约束 | 无 | ✅ **已完成**（2026-08-02，116 测试全绿：8 个 A2 + 108 个 A3 继承契约） |
| **A'** | 覆盖率基线（不设门禁，先出数） | 知道真实覆盖率是多少 | 无 | ⬜ 待开始 |
| **B** | 源生成器测试（`CSharpGeneratorDriver` 驱动 + 结构化快照）+ 禁反射守卫 | 1402 行基石有测试；§3.4 自动化 | 无 | ✅ **已完成**（2026-08-02，B1 5 测试 + B2 1 测试；详见 `Wails.Net.SourceGenerators.Tests` 与 `ReflectionGuardTests`） |
| **C** | `Events`/`Errors`/`MacOS` 补测 + 覆盖率门禁开启 | 8 个零测试模块降到 4 个 | A' | 🟡 **进行中**（2026-08-02：`Errors`/`Events` 补测完成，13+10 测试全绿；`MacOS` 需 macOS runner、`A'` 覆盖率基线待建、门禁待开） |
| **D** | 前端 `mocks` 包 + Vitest + 跨语言哈希黄金用例 | Runtime.Js 脱离零测试 | 无 | ⬜ 待开始 |
| **E** | CI 重构（xvfb、拆分 continue-on-error、契约计数元测试） | Linux/Android 有真门禁 | A、B | ⬜ 待开始 |
| **F** | 桌面 E2E-Lite（测试后门插件） | 三平台 E2E 对齐 | A | ⬜ 待开始 |
| **G** | E2E-Full（CDP / WebKitWebDriver）+ Coyote 并发探索 | 可选增强 | F | ⬜ 待开始 |

> **进度说明（2026-08-02）**：阶段 A 已全部落地并通过。`tests/Wails.Net.Testing.Tests` 是 A 的载体（A2 门面验证 + A3 Mock 第 4 平台契约）。**阶段 B 已完成**：B1 在 `tests/Wails.Net.SourceGenerators.Tests` 用 `CSharpGeneratorDriver` 驱动 `BindingSourceGenerator`（离线环境以「结构化断言 + 确定性 + 语法有效性 + 生成代码可编译性」替代 `Verify.TUnit` 全文快照，因仓库缓存无 `Verify.TUnit` 与 `Basic.Reference.Assemblies.*`），共 5 测试；B2 在 `tests/Wails.Net.Application.Tests/Architecture/ReflectionGuardTests.cs` 扫描 `src/` 禁用运行时反射（注释/字符串已剥离，白名单 3 处例外），共 1 测试。两者均已通过。阶段 A 修复的关键坑（C# `params` 前带可选数值参数的重载决议陷阱）已记入 `.workbuddy/memory/MEMORY.md`，接手前必读。仓库根目录已确认无 `invoke_json.txt` / `parse_json.txt` / `binding_args.txt` / `DiagnosticTests.cs` 等临时文件残留，交接干净。

**建议起手**：A → B 两步价值密度最高。A 让绝大多数"必须有 GUI"的测试变成 CI 可跑；B 堵住整个禁反射架构的验证空白。

**进度更新（2026-08-02 续）**：阶段 C 的 `Errors`/`Events` 补测已落地并全绿——新增 `tests/Wails.Net.Errors.Tests`（13 测试：`CallError`/`WailsError`/`ErrorCodes` 的 IPC 契约与枚举数值稳定性）与 `tests/Wails.Net.Events.Tests`（10 测试：`CommonEvents` 保留名识别 + `KnownEvents` 事件名映射与 `uint` 阈值路由）。两项均 0 警告 0 错误编译通过。`CommonEvents.IsKnownEvent` 顺带把参数由 `string` 改为 `string?`（方法内部 `Contains` 本就容忍 null，属合理生产改进）。`MacOS` 补测因代码受 macOS TFM 条件编译约束、本机（Windows）无法构建运行，留待 macOS CI runner；覆盖率门禁（§5.1）依赖 `A'` 基线，亦未开启。C 阶段目前只看 `Errors`/`Events` 已实质性推进。

---

## 附录 A：现状缺口清单

### 完全无测试的 src 模块（8 / 17）

| 模块 | 规模 | 风险 |
|------|------|------|
| `Wails.Net.SourceGenerators` | 3 文件 / **1402 行** | 🔴 极高——禁反射架构基石 |
| `@wails-net/runtime` | 4 文件 / 1141 行 | 🔴 高——前端唯一入口 |
| `Wails.Net.Events` | 5 / 588 | 🟡 中 |
| `Wails.Net.Application.MacOS` | 4 / 341 | 🟢 低——骨架实现 |
| `Wails.Net.Errors` | 3 / 256 | 🟡 中 |
| `Wails.Net.Templates` | 2 / 119 | 🟢 低 |
| `Wails.Net.Sdk` + `Bundle.{Windows,Linux,Android}` | 0 C# / 纯 MSBuild | 🟡 中——无打包集成测试 |
| `Wails.Net.Generator` | 9 / 1114 | 🟡 中——仅被 Cli.Tests 间接覆盖 |

### 工程配置问题

1. **`Directory.Packages.props:74-78` 残留违规声明**：`Microsoft.NET.Test.Sdk 17.12.0`、`MSTest.TestAdapter/TestFramework 3.7.0`、`FluentAssertions 7.0.0` 均未被引用，但与 AGENTS.md「禁止 MSTest」冲突 → **建议直接删除**
2. **`Wails.Net.AssetServer` 缺 `InternalsVisibleTo`**（其余 4 个 src 工程都有）
3. **Linux 测试工程 TFM 是 `net10.0`** 而非 linux TFM，靠 csproj 注入 `SupportedOSPlatformAttribute` 消 CA1416，属于绕行
4. **`testing-guide.md` §5.1 已过期**：仍写「绑定调用异常需解包 `TargetInvocationException`」，但 AGENTS.md §3.3 明确源生成器路径下直接捕获具体异常类型，无需解包 → **需修订**
5. **`testing-guide.md` §2 项目表不全**：缺 Android、GuiContract、AssetServer 三个测试工程

---

## 附录 B：三方测试能力速查

| 能力 | Wails v3 | Tauri v2 | Wails.Net（目标态） |
|------|----------|----------|---------------------|
| 无头跑 IPC | ❌ | `get_ipc_response` | `WailsTestHost.InvokeAsync` |
| 无头建窗口 | ❌ | `MockRuntime` | `MockWebviewWindow` |
| Mock 行为一致性保证 | — | ❌ 无 | ✅ **GuiContract 约束** |
| 前端 IPC 拦截 | `vi.mock` | `mockIPC` | `@wails-net/runtime/mocks` |
| 真实二进制 E2E | ❌ dev server | ✅ WebDriver | CDP / WebKitWD / adb |
| 平台原生分级契约 | ❌ | ❌ | ✅ L1/L2/L3 × 4 实现 |
| 竞态检测 | ✅ `-race` | — | 压测 + Coyote（可选） |
| 编译期架构守卫 | — | trybuild | 禁反射扫描 + 生成器快照 |

---

**参考**：[Wails v3 Testing](https://v3.wails.io/guides/testing/) · [Tauri v2 Mocking](https://v2.tauri.app/develop/tests/mocking/) · [Tauri v2 WebDriver](https://v2.tauri.app/develop/tests/webdriver/) · [`tauri::test` API](https://docs.rs/tauri/latest/tauri/test/)
