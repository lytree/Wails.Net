# ADR-0004: Android 平台实现决策

- 状态：已接受
- 日期：2026-07-13
- 决策者：Wails.Net 团队
- 相关 ADR：[ADR-0001](0001-technology-selection.md)（已修订以纳入 Android）

## 背景

ADR-0001 初版将 Android 列为"暂不实现"。随着项目演进至阶段 7 完成（CLI 工具与代码生成器），用户提出扩展至 Android 平台的诉求，并将 Android 实现作为阶段 8 正式纳入路线图。

Wails v3 原版（Go 实现）已支持 Android，使用 GoMobile + Android WebView。Wails.Net 需要在 .NET 平台上找到等价方案。

### 关键约束

1. **ADR-0001 替代方案第 8 条**：放弃 Avalonia / MAUI 作为完整跨平台 GUI 框架。Wails.Net 的 GUI 由 WebView 承载，仅需原生窗口壳。
2. **互操作策略**：ADR-0001 §1.2 明确采用 Managed Wrappers，禁用 C++/CLI。
3. **AOT 兼容性**：未来需要支持 Native AOT 与裁剪。
4. **AGENTS.md §7.4 禁止行为**：未禁用 MAUI，但禁止 `Xamarin.Forms`。
5. **测试体系**：必须使用 TUnit。

## 决策

### 1. Android 工作负载：`android`（不使用 `maui-android`）

选择 `android` 工作负载（仅 .NET Android SDK），不引入 `maui-android`。

**理由**：
- 与 ADR-0001 替代方案第 8 条"放弃 MAUI 作为完整 GUI 框架"一致
- 仅需调用 `Android.Webkit.WebView`，无需 MAUI Controls 的 `Microsoft.Maui.Controls.WebView` 封装
- 包体积小、构建快、依赖少
- 与 Win/Linux 互操作策略（CsWin32 / GirCore）保持一致：直接调用原生 API

### 2. TFM：`net10.0-android24.0`

- 最低 API Level 24（Android 7.0），覆盖 95%+ 设备
- 支持 `WebView.WebMessageListener`（API 23+），与 Windows WebView2 的 WebMessage 模式一致
- 不选择 API 21 是因为 `addJavascriptInterface` 存在已知安全风险（API 17 以下漏洞，API 23+ 推荐使用 WebMessageListener）

### 3. 窗口模型：单 Activity + Fragment

- 采用现代 Android 推荐的 Single-Activity 架构
- 多窗口通过 Fragment 切换实现，每个 `WebviewWindow` 对应一个持有 WebView 的 Fragment
- 与 Wails 多窗口对象模型的对应：`WindowManager` 内部维护 `Fragment` 引用而非 `Activity` 引用
- 不选择多 Activity 模式：Android 多 Activity 切换开销大，且不符合现代推荐

### 4. WebView 资源拦截方案：`WebViewClient.ShouldInterceptRequest`

- 重写 `WebViewClient.ShouldInterceptRequest` 拦截所有请求
- 与 Windows 的 `AddWebResourceRequestedFilter` 模式一致
- 支持 SPA 路由回退到 `index.html`
- 不使用 `file://` 协议（项目历史已踩坑，CORS 限制与权限问题）
- 不启动本地 HTTP 服务器（端口占用与攻击面问题）

### 5. IPC 桥：`WebView.WebMessageListener`

- 使用 `Android.Webkit.WebView.WebMessageListener`（API 23+）
- 与 Windows WebView2 的 `WebMessageReceived` 模式完全一致
- 双向通过 `WebMessageListener.OnPostMessage` 接收 + `WebView.PostWebMessage` 发送
- 不使用 `WebView.AddJavascriptInterface`（安全风险高，且与 Wails IPC 模型不一致）

### 6. 互操作策略：Java 互操作（.NET Android 绑定）

- 直接通过 .NET Android 工作负载生成的 Java 绑定调用 `Android.Webkit.*`、`Android.App.*`、`Android.OS.*` 等
- 不引入第三方 C# Android 绑定库
- 与 Win/Linux 互操作策略保持一致：Managed Wrappers 模式

### 7. 生命周期与 Generic Host 集成

- Host 跟随 Activity 生命周期：`Activity.OnCreate` 启动 Host，`Activity.OnDestroy` 停止 Host
- `Application.Run()` 不阻塞，而是注册 Activity 生命周期回调
- 与 Win/Linux 的 Host 主动 Run 模式略有差异，但通过 `IPlatformApp.Run()` 的返回值（int 退出码）保持接口一致

### 8. 项目命名与命名空间

- 程序集名：`Wails.Net.Application.Android`
- 顶层类型：`Wails.Net.Application.Platform.AndroidPlatformApp`
- 与 Win/Linux 完全一致，PlatformFactory 反射加载模式无需变更

### 9. 阶段编号

- 作为阶段 8 追加，不打乱已有 1~7 阶段编号
- README "第四阶段" 描述修正为"阶段 8"

## 后果

**正面影响**：

- 项目正式支持三大平台（Windows / Linux / Android），覆盖桌面与移动端
- 与 ADR-0001 "放弃完整 GUI 框架"决策一致，仅引入 .NET Android SDK 而非完整 MAUI
- IPC 与资源拦截方案与 Windows 实现对齐，前端运行时（Runtime.Js）可三平台共享
- 单 Activity + Fragment 架构符合现代 Android 推荐，性能优、状态同步简单

**负面影响**：

- ADR-0001 需修订"Android 暂不实现"措辞，与原始决策冲突（本 ADR 已记录该变更）
- 需要 CI/CD 增加 Android 工作负载安装步骤与 `android-arm64` / `android-x64` RID
- Android 模拟器/真机插桩测试基础设施需单独搭建，CI 复杂度上升
- 部分 `IWebviewWindowImpl` 桌面专属方法（SetSize、Maximise、SetTaskbarProgress 等）将 no-op 或抛 `NotSupportedException`
- Java 互操作代码量较大，需熟悉 Android API

## 考虑过的替代方案

1. **`maui-android` 工作负载**：放弃。引入大量未使用的 MAUI 依赖，与 ADR-0001 替代方案第 8 条冲突；构建慢；包体积大。
2. **`Microsoft.AspNetCore.Components.WebView`**：放弃。引入 Blazor 模型，与 Wails IPC 双重抽象层冲突；Android 支持成熟度需验证。
3. **多 Activity 窗口模型**：放弃。Android 多 Activity 切换开销大，状态同步复杂，不符合现代推荐。
4. **单 Activity + 单 WebView（限制多窗口）**：放弃。功能缺失，与 Wails v3 多窗口核心特性不兼容。
5. **`WebView.AddJavascriptInterface`**：放弃。安全风险高，API 17 以下有漏洞，与 Wails/WebMessage 模型不一致。
6. **本地 HTTP 服务器**：放弃。端口占用风险，性能开销，增加攻击面。
7. **`file://` 协议 + `loadDataWithBaseURL`**：放弃。已知权限问题（项目历史已踩坑），CORS 限制，不支持自定义路由。
8. **`net10.0-android21.0`**：放弃。WebMessageListener 不可用，需回退到 addJavascriptInterface（安全风险）。
9. **`net10.0-android34.0`**：放弃。覆盖率较低（约 70%+），过度限制目标设备。

## 相关文件

- [AGENTS.md — 技术选型](../../AGENTS.md)
- [ADR-0001: 技术选型决策](0001-technology-selection.md)
- [PlatformFactory.cs](../../src/Wails.Net.Application/Platform/PlatformFactory.cs)
- [IPlatformApp.cs](../../src/Wails.Net.Application/Platform/IPlatformApp.cs)
- [IWebviewWindowImpl.cs](../../src/Wails.Net.Application/Windows/IWebviewWindowImpl.cs)
