# Wails.Net.Application.GuiContract.Tests

> **跨平台 Webview 窗口契约测试（GUI Contract Test Suite）。**
> 一份契约规范，三平台分别验证：保证 `IWebviewWindowImpl` 在 Windows / Linux / Android 上的行为一致。

---

## 1. 背景与动机

Wails.Net 在三平台（Windows / Linux / Android）各有一个 `IWebviewWindowImpl` 实现：

| 平台 | 实现 | 后端 |
|------|------|------|
| Windows | `Win32WebviewWindow` | Microsoft.Web.WebView2 + CsWin32 |
| Linux   | `LinuxWebviewWindow`   | GirCore (GTK4 + WebKitGTK) |
| Android | `AndroidWebviewWindow` | .NET Android → `Android.Webkit.WebView` |
| macOS   | `MacOSWebviewWindow`   | `#if MACOS` AppKit + WKWebView / `#else` ServerMode |

如果为每个平台单独写一份黑盒测试，会出现两个问题：

1. **重复**：80+ 个窗口方法的契约完全相同，每个都要写 3 遍。
2. **漂移**：不同平台测试写法的细微差别会掩盖真实差异，让"哪个平台有 bug"难以定位。

GuiContract 通过 **抽象基类 + 平台 Fixture** 解决：

```
            ┌──────────────────────────────┐
            │ WebviewWindowContractTests  │  ← 抽象基类，定义 80+ 个 [Test] 方法
            │   + IWebviewWindowFixture   │
            └──────────────┬───────────────┘
                           │ 继承
       ┌───────────────────┼───────────────────┐
       ▼                   ▼                   ▼
WindowsWebviewWindow   LinuxWebviewWindow   AndroidWebviewWindow
   ContractTests          ContractTests         ContractTests
       │                   │                   │
       ▼                   ▼                   ▼
WindowsWebviewWindow   LinuxWebviewWindow   AndroidWebviewWindow
      Fixture              Fixture              Fixture
```

---

## 2. 项目结构

```
tests/Wails.Net.Application.GuiContract.Tests/
├── README.md                              ← 本文件
├── IWebviewWindowFixture.cs               ← 平台夹具接口 + 契约分级常量
├── WebviewWindowContractTests.cs          ← 抽象基类，定义所有 [Test]
└── Wails.Net.Application.GuiContract.Tests.csproj
```

**关键点**：

- `Wails.Net.Application.GuiContract.Tests.csproj` **不是独立测试项目**（`IsTestProject=false`、`OutputType=Library`），不能直接 `dotnet run`。
- 基类源代码通过 `<Compile Include="..\Wails.Net.Application.GuiContract.Tests\WebviewWindowContractTests.cs" Link="..." />` 以**链接方式**嵌入各平台测试项目。
- 这样做的原因是 **TUnit 的源生成器只能发现当前项目源代码中的 `[Test]` 方法**，跨程序集继承 `[InheritsTests]` 不会触发源生成器输出测试入口。

各平台测试项目的实际承载方式见 `Wails.Net.Application.Windows.Tests.csproj`（用 `<Compile Include>` 链接）以及 `Linux / Android` 测试 csproj（直接 `ProjectReference` — 同样有效，因为源生成器遍历的是已编译进当前程序集的源代码）。

---

## 3. 契约分级（L1 / L2 / L3）

通过 `[Category(WindowContractLevel.L1Universal)]` 等标记每个测试所属级别，便于：

- **CI 分级过滤**：例如 `dotnet run -- --treenode-filter "*/L1*"` 仅跑通用契约。
- **环境降级**：无 GUI 环境（CI 容器）跳过 L2/L3，仅验证 L1。
- **故障定位**：契约违例时一眼区分"通用问题"还是"平台差异"。

| 级别 | 常量 | 含义 | 适用平台 |
|------|------|------|---------|
| **L1** | `WindowContractLevel.L1Universal` | 所有平台必须满足：方法不抛异常、返回类型正确、空实现 no-op 行为明确。 | 全部 |
| **L2** | `WindowContractLevel.L2Functional` | 调用后状态变化可观察：`Set*` 后 `Get*` 返回新值。 | Windows / Linux（Android 窗口由系统管理，部分跳过） |
| **L3** | `WindowContractLevel.L3PlatformSpecific` | 平台特有功能：Windows 的 Mica/Acrylic、Linux 的 GTK CSS 类、Android 的 Activity 生命周期等。 | 仅对应平台 |

测试基类在方法体内部还会用 `if (!fixture.HasRealGuiEnvironment) return;` 做**早退保护**：

```csharp
[Test]
[Category(WindowContractLevel.L2Functional)]
public async Task GetMinSize_Reflects_SetMinSize()
{
    var fixture = GetFixture();
    if (!fixture.HasRealGuiEnvironment) return;   // ← 早退，跳过断言
    ...
}
```

更严格的场景会通过基类的 `CreateWindow(...)` 抛出 `SkipTestException`，确保 L1 之外的契约在没有 GUI 时被**显式跳过**而非假性通过。

---

## 4. 平台 Fixture 概览

`IWebviewWindowFixture` 接口规定每个平台测试项目必须提供：

| 方法 / 属性 | 职责 |
|-------------|------|
| `PlatformName` | 报告平台名（"windows" / "linux" / "android"）。 |
| `HasRealGuiEnvironment` | 是否具备真实 GUI 运行时（无则只跑 L1）。 |
| `CreateWindow(id, options)` | 构造平台 `IWebviewWindowImpl` 实例。 |
| `DestroyWindow(window)` | 幂等销毁并释放原生资源。 |
| `RunOnUiThread(Action)` | 在平台 UI 线程同步执行操作。 |
| `RunOnUiThread<T>(Func<T>)` | 在平台 UI 线程同步执行函数并返回结果。 |

具体实现见各平台项目：

| 平台 | Fixture 类 | UI 线程模型 |
|------|-----------|-------------|
| Windows | `WindowsWebviewWindowFixture` | 通过 `Thread.SetApartmentState(STA)` 每次起新 STA 线程（Win32 窗口要求 STA）。 |
| Linux   | `LinuxWebviewWindowFixture`   | 当前线程同步执行（测试套件不启动 GTK MainLoop；无 DISPLAY 时 `HasRealGuiEnvironment=false`）。 |
| Android | `AndroidWebviewWindowFixture` | 委托给 `AndroidPlatformApp.DispatchOnMainThread`，无 MainLooper 时回退同步；`HasRealGuiEnvironment` 始终 false（单元测试环境无 Activity / Surface）。 |

---

## 5. 怎么跑

```bash
# Windows（需桌面会话）
dotnet run --project tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj

# Linux（需 DISPLAY 或 WAYLAND_DISPLAY；无则自动降级到仅 L1）
wsl -d kali-linux -- bash -c "cd /mnt/f/Code/Dotnet/Wails.Net && dotnet run --project tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj"

# Android（仅 L1，无 Activity 时跳过 L2/L3）
dotnet run --project tests/Wails.Net.Application.Android.Tests/Wails.Net.Application.Android.Tests.csproj
```

按级别过滤（TUnit MTP 过滤语法）：

```bash
# 仅跑 L1 通用契约
dotnet run --project tests/Wails.Net.Application.Windows.Tests/... -- --treenode-filter "/*/*/*/L1*"
```

---

## 6. 添加新契约测试

```csharp
// 1. 在 WebviewWindowContractTests.cs 的适当"模块"注释下添加 [Test] 方法
// 2. 选择合适的契约级别：
//    - 永不抛异常、跨平台一致 → L1
//    - Set 后 Get 应反映状态变化 → L2
//    - 仅某个平台有意义的特性 → L3
// 3. 在 fixture 不支持的平台用 `if (!HasRealGuiEnvironment) return;` 早退
// 4. 复杂场景用基类 CreateWindow(...) 抛 SkipTestException
// 5. 不修改平台 fixture 文件即可让三平台自动共享新测试
```

---

## 7. 不在契约测试范围内的事项

- **窗口创建前的环境准备**（如 GTK 初始化、Looper 绑定）由各平台 `*PlatformApp` 自行保证，不属于 `IWebviewWindowImpl` 契约。
- **前端 ↔ 后端 IPC 端到端验证** 走 `Wails.Net.Application.Tests` 中的传输层测试 + Android E2E 脚本（`tests/Wails.Net.Android.E2E/run-android-e2e.fsx`）。
- **macOS 真实 GUI 行为**：见 `tests/Wails.Net.Application.MacOS.Tests/MacOSPlatformSkeletonTests.cs`（仅验证 `net10.0` `#else` 分支的骨架降级）；完整 AppKit 分支需在 macOS 主机上以 E2E 形式补充。

---

## 8. 参考

- TUnit `[InheritsTests]` 与源生成器行为：参考 TUnit 官方文档
- 契约测试思想：Junit `ContractTest`、Go `testing/quick`、xUnit 理论（Theory）
- Wails.Net 整体测试策略：[docs/development/testing-strategy.md](../../docs/development/testing-strategy.md)
- 测试编写规范：[docs/development/testing-guide.md](../../docs/development/testing-guide.md)
- 平台实现：[docs/architecture/platform-abstraction.md](../../docs/architecture/platform-abstraction.md)