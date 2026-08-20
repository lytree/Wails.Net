
# 0.1.x → 1.0.0-alpha.x 迁移指南：权限默认 Strict 翻转

> 本文档对应 R3-E（库默认从 Compatibility 翻转为 Strict）与 SemVer 0.1.0-alpha.x → 1.0.0-alpha.x 版本号 bump。
>
> **状态**：✅ 已落地（2026-08-20，R3-E 提交待合并）
> **影响范围**：所有 0.1.x 应用升级到 1.0.0-alpha.x 时必须审视权限配置
> **预期破坏性**：默认 Mode 由 Compatibility 翻转为 Strict

## 0. TL;DR

1.0.0-alpha.x 起，Wails.Net 库默认采用 **Strict 最小权限姿态**，对齐 Tauri v2 默认行为。
未授权命令一律拒绝，需要通过 `capabilities/` 目录下的 JSON 文件显式声明。

如果你的应用在 0.1.x 下默认能跑通且未声明 capability 文件，升级到 1.0.0-alpha.x 后会立即触发"未授权命令拒绝"错误——这是预期行为，请按本文档配套 capability 文件或临时回退到 Compatibility 模式。

## 1. 行为变化

| 配置项 | 0.1.x 默认 | 1.0.0-alpha.x 默认 | 说明 |
|---|---|---|---|
| `Wails:Permissions:Mode` | `Compatibility` | `Strict` | **破坏性变化**（R3-E） |
| `Wails:Permissions:Enabled` | `false` | `true`（在 Strict 下隐式） | Strict 模式下始终启用检查 |
| `Wails:Permissions:DenyByDefault` | `true` | `true` | 不变 |
| 未声明能力的命令 | 默认放行 | **拒绝**（"权限拒绝：未声明能力"） | **破坏性变化** |
| 启动日志 warning 触发条件 | `Mode=Compatibility && !Enabled` | `Mode=Compatibility`（用户显式回退） | warning 引导用户回正 Strict |

## 2. API 变化

### 2.1 重命名：`EnforceStrictByDefault` → `CompatibilityByDefault`

```csharp
// 0.1.x（已废弃，1.0.0-alpha.x 已移除）
PermissionOptions.EnforceStrictByDefault = true;

// 1.0.0-alpha.x（仅作临时回退用）
PermissionOptions.CompatibilityByDefault = true;
```

新名字直接表达语义：默认是 Strict（对齐 Tauri v2），此标志仅在需要回退时使用。

### 2.2 重命名：`EnableStrictByDefault()` → `EnableCompatibilityByDefault()`

```csharp
// 0.1.x（已废弃，1.0.0-alpha.x 已移除）
builder.Services.EnableStrictByDefault();

// 1.0.0-alpha.x（仅作临时回退用）
builder.Services.EnableCompatibilityByDefault();
```

### 2.3 `PermissionOptions.Mode` 默认值变更

```csharp
// 0.1.x
public PermissionMode Mode { get; set; } = PermissionMode.Compatibility;

// 1.0.0-alpha.x
public PermissionMode Mode { get; set; } = PermissionMode.Strict;
```

## 3. 升级步骤

### 3.1 最小影响（capability 配套就绪的项目）

如果项目已经按 R3-B 落地了 capability 文件（每个插件默认 `<plugin>:default` 权限集），
升级到 1.0.0-alpha.x 后**无需任何修改**——Strict 默认行为就是你的预期。

### 3.2 中等影响（仅有部分 capability 配套）

升级后会看到部分命令被拒绝。运行 `wails-net check` 识别缺失的 capability，
再用 `wails-net capability init <plugin>` 生成默认 capability JSON。

### 3.3 较大影响（尚未迁移到 capability 模型）

短期过渡方案：在 `Program.cs` 启动期调用 `EnableCompatibilityByDefault()` 临时回退（仅作 0.1.x → 1.0.0-alpha.x 升级过渡）：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 临时回退 0.1.x 行为，仅作 0.1.x → 1.0.0-alpha.x 升级过渡
// **生产环境应配套 capability JSON 后移除此调用**
builder.Services.EnableCompatibilityByDefault();

var app = builder.Build();
app.Run();
```

或在 `appsettings.json` 显式置回：

```json
{
  "Wails": {
    "Permissions": {
      "Mode": "Compatibility",
      "Enabled": false,
      "DenyByDefault": false
    }
  }
}
```

> ⚠️ **警告**：以上两种回退方式都会让应用运行在宽松模式（未授权命令默认放行）。
> **仅建议短期过渡**（数天 / 数周），生产环境必须配套 capability JSON 后移除。

### 3.4 推荐升级顺序

1. **升级 Wails.Net NuGet 包到 1.0.0-alpha.x（最新 alpha 版本）**
2. **运行 `wails-net check`** 定位缺失的 capability（预期会有大量"未知权限"警告）
3. **对每个插件运行 `wails-net capability init <plugin> --out capabilities/<plugin>.json`**
4. **在主 capability 文件中引用各插件的 `<plugin>:default` 权限集**
5. **再次运行 `wails-net check`** 确认无警告
6. **移除临时回退调用**（`EnableCompatibilityByDefault()` 或 `Mode: Compatibility`）
7. **完整回归测试 + 灰度上线**

## 4. 版本号

| 系列 | 范围 | 状态 |
|---|---|---|
| 0.1.x | 早期开发（Strict 默认前的 Compatibility 默认） | 历史 |
| **1.0.0-alpha.x** | **R3-E 起，Strict 默认 + Capability 模型（持续迭代）** | **当前** |
| 1.0.0-rc.x | RC 阶段（API 冻结、迁移工具完善） | 待启动 |
| 1.0.0 | GA 正式版（视 RC 测试周期） | 待启动 |
| 1.x.x 之后 | 1.x 系列持续迭代（插件市场、IDE 插件） | 规划 |

## 5. 与 Tauri v2 默认行为对齐

| 维度 | Wails.Net 1.0.0-alpha.x | Tauri v2 GA |
|---|---|---|
| 默认权限姿态 | Strict | Strict（最小权限） |
| capability JSON 目录 | `capabilities/` | `capabilities/`（同） |
| capability 校验时机 | 启动期 + 编译期（`wails-net check`） | 编译期（`tauri-build`） |
| 默认 deny 未声明权限 | ✓ | ✓ |
| 窗口级 capability 隔离 | ✓ | ✓ |
| 远程 URL 模式限制 | ✓ | ✓ |

## 6. 后续阶段

- R3-E 完成后进入 1.0 RC 阶段（API 冻结、文档完善、迁移工具）
- 1.0 GA 后进入 H1：插件市场 + IDE 插件（VS Code / Rider）

---

*历史内容（0.1.x → 1.0.0-alpha.x 大版本段之前的 Compatibility 详细迁移指南）保留在下方章节，
仅作历史参考；1.0.0-alpha.x+ 用户请直接阅读 §1-5。*
# 权限模式迁移指南：Compatibility → Strict

> 本文档对应优化路线图 [OPT-SEC-003](./optimization-roadmap.md#46-安全模板落地清单opt-sec-003-遗留项-2026-08-15-核实)
> （安全模板与迁移指南）与 [OPT-SEC-001](./optimization-roadmap.md#45-权限-strict-模式迁移)（权限 Strict 模式）。
>
> 更新日期：2026-08-15

## 1. 背景

Wails.Net 权限系统支持两种模式（`Wails:Permissions:Mode`）：

| 模式 | 语义 | 适用场景 |
|---|---|---|
| `Compatibility`（默认，兼容） | 未授权命令默认放行 | 旧应用迁移期 / 快速原型 |
| `Strict`（推荐生产） | 未授权命令一律拒绝（最小权限） | 生产应用，对应 Tauri v2 默认姿态 |

模板与示例项目自 2026-08-15 起默认生成 `Strict` 配置。已有项目如需升级到
Strict 模式，请按本文档操作。

## 2. 配置差异

### 2.1 宽松（旧默认）配置

```json
{
  "Wails": {
    "ApplicationName": "My App",
    "Permissions": {
      "Mode": "Compatibility",
      "Enabled": false,
      "DenyByDefault": true
    }
  }
}
```

### 2.2 Strict（推荐）配置

```json
{
  "Wails": {
    "ApplicationName": "My App",
    "Permissions": {
      "Mode": "Strict",
      "Enabled": true,
      "DenyByDefault": true,
      "CapabilitiesDirectory": "capabilities"
    }
  }
}
```

行为差异：

- `Compatibility` + `Enabled=false`：权限检查关闭，所有命令默认放行（启动时输出一次
  warning 提示迁移）。
- `Strict`：即使 `Enabled` 未显式置 true 也强制启用检查；未授权命令拒绝；
  未配置 `capabilities` 目录时，**所有声明了能力要求的命令都会被拒绝**。

## 3. 迁移步骤

### 3.1 修改配置

将 `Wails:Permissions` 改为 Strict 配置（见 §2.2）。

### 3.2 创建 capabilities 目录

在应用根目录（相对 `AppContext.BaseDirectory`，即输出目录）创建 `capabilities/` 目录，
并放置能力声明文件：

```json
{
  "identifier": "default",
  "description": "主窗口能力",
  "windows": ["main"],
  "platforms": ["windows", "linux", "macos", "server"],
  "permissions": [
    "window:default"
  ]
}
```

字段说明：

| 字段 | 说明 |
|---|---|
| `identifier` | 能力标识，应用内唯一 |
| `windows` | 应用此能力的窗口名；空数组 = 所有窗口 |
| `platforms` | 平台过滤（`windows`/`linux`/`android`/`macos`/`server`）；空数组 = 所有平台 |
| `permissions` | 权限标识列表；支持命名权限集（如 `window:default`）与细粒度权限（如 `window:allow-readonly`）；`!` 前缀表示显式拒绝 |

> 注意：csproj 中需将 `capabilities/**` 复制到输出目录：
>
> ```xml
> <None Update="capabilities\**\*">
>   <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
> </None>
> ```

### 3.3 定位并授权缺失权限

Strict 模式下，未授权命令在前端调用时会收到权限拒绝错误（`PermissionDenied` 类错误），
后端日志输出类似：

```
权限拒绝: 命令需要能力 window:openDevTools（窗口=main）
```

处理方式：

1. 找到被拒绝的能力标识。
2. 确认该能力确属应用需要（最小权限原则：能不放就不放）。
3. 在 capabilities 文件中追加授权：

```json
{
  "permissions": [
    "window:default",
    "window:allow-dangerous"
  ]
}
```

4. 重新构建并运行。

### 3.4 验证

- 未授权命令调用返回拒绝错误（而不是静默成功）。
- 已授权命令行为与 Compatibility 模式一致。
- 使用 `wails-net check`（若已实现）可构建期校验 capability 文件语法与权限标识。

## 4. CSP 与模板默认值

模板与示例默认携带内容安全策略（`Wails:App:Security:Csp`）：

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' ws://localhost:* http://localhost:*; frame-src 'none'; object-src 'none'
```

说明：

- `connect-src` 中的 `ws://localhost:* http://localhost:*` 用于 Debug 模式 vite HMR；
  生产环境可收紧为 `connect-src 'self'`。
- `style-src 'unsafe-inline'` 为前端框架内联样式保留；如无内联样式可移除。
- `img-src blob:` 为图片/视频 Blob 流保留。
- 若前端需要加载远程资源（CDN、第三方脚本），需在 CSP 中显式放行对应源。

## 5. 常见问题

| 问题 | 原因 | 处理 |
|---|---|---|
| 前端所有调用都失败 | Strict 模式未配置 capabilities | 创建 capabilities 目录并按需授权 |
| 单个命令失败 | 该命令需要的能力未授权 | 在 capabilities 中追加对应权限标识 |
| 平台声明不生效 | `platforms` 拼写错误 | 平台名固定为 `windows`/`linux`/`android`/`macos`/`server`；未知平台加载时输出 warning |
| Debug 模式 HMR 断开 | CSP 拦截了 websocket | 确认 `connect-src` 含 `ws://localhost:*` |

## 6. R3-A：命令安全声明强制（2026-08-19 落地）

R3-A 是 R3（Tauri 风格 ACL）的第一阶段基础设施落地，在不破坏现有应用的前提下引入
**编译期**与**启动期**两道关卡，为后续默认 Strict 翻转做准备。

### 6.1 新增的诊断 WNET003

源生成器 `MapCommandSecurityDiagnosticAnalyzer` 新增 WNET003 诊断：

- **触发条件**：调用 `MapCommand` / `MapCommandAsync` 的**新重载**（已声明 `CommandSecurity?` 形参）时未传入 `CommandSecurity` 实参。
- **不触发场景**：旧重载（无 `CommandSecurity` 形参）调用，以避免对存量未迁移注册路径产生噪声。
- **严重度**：Warning。在 `TreatWarningsAsErrors=true` 下即构建失败。
- **修复方式**：调用末尾追加 `CommandSecurity.Require("xxx:cap").WithScope(...)` 或 `CommandSecurity.Public()`，或迁移到带 security 形参的重载。

WNET001 / WNET002 行为不变：

- WNET001：方法标记 `[RequireCapability]` 但 MapCommand 调用未传 `CommandSecurity`。
- WNET002：方法参数标记 `[ScopeParameter]` 但 MapCommand 调用未传 `CommandSecurity`。
- WNET003 是更宽泛的"未传任何 CommandSecurity"警告，覆盖即使无特性标注的命令也应显式声明安全姿态。

### 6.2 Capability JSON Schema 与启动期校验

新增 `schemas/capability.schema.json`（嵌入到 `Wails.Net.Application` 程序集）：

- 描述 capability 文件的权威字段形状，对应 `Wails.Net.Application.Security.Capability`。
- 平台字段约束为枚举：`windows` / `linux` / `android` / `macos` / `server`。
- 权限字段允许 `!` 前缀表示显式 deny。

新增 `CapabilityManifestValidator`（R3-A.3）：

- `ValidateShape(capabilities)`：字段级结构校验，返回 `CapabilityValidationIssue` 列表。错误码 `CM001`–`CM005`。
- `FindUndeclaredPermissions(manager, capabilities)`：引用校验，capability 引用的 permission 若未通过 `IPermissionRegistrar.DeclarePermission` 声明则报错。
- `LoadSchemaJson()`：导出嵌入的 schema 文本，供 CLI / IDE 插件消费。

### 6.3 默认 Strict 翻转开关

新增 `PermissionOptions.EnforceStrictByDefault`（静态属性，默认 `false`）与 `EnableStrictByDefault()` 扩展方法：

```csharp
// Program.cs（启动期）
PermissionOptions.EnforceStrictByDefault = true;

// 或通过 DI
builder.Services.EnableStrictByDefault();
```

设置后，所有未显式覆盖 `Mode` 的 `PermissionOptions` 实例都将以 `Strict` 为初始值。
当前库默认仍为 `Compatibility`（向后兼容），此开关是 R3 完成后翻转默认前的 opt-in 入口。

### 6.4 迁移顺序建议

1. **先消除 WNET003**：把所有 `MapCommand` 调用补充 `CommandSecurity`，或迁移到带 security 形参的重载。
2. **再消除 WNET001 / WNET002**：补充 `Require(...)` / `WithScope(...)`。
3. **最后开启 Strict 默认**：在生产应用启动期调用 `EnableStrictByDefault()`，
   配合已迁移的 capability JSON 完成默认翻转。

### 6.5 后续 R3 阶段

- **R3-B ✅**：47 插件 304 命令全量迁移到 `Require` / `Public`，每个插件配套生成 capability 默认 JSON。
- **R3-C ✅**：源生成器产出插件权限清单 + 运行时注册表 (PluginPermissionManifestRegistry)，库默认仍为 Compatibility（Strict 翻转延后到 R3-E 协调版本号 bump）。 `Compatibility` 翻转为 `Strict`（破坏性变更，需配合本指南与版本号 bump）。

## 7. R3-D.A：wails-net check 自动加载插件清单（2026-08-19 落地）

R3-C 提供了 PluginPermissionManifestRegistry.CollectAllPermissions() 运行时聚合 API，
R3-D.A 把它接入 CapabilityChecker，形成"编译期生成 → 运行时聚合 → CLI 校验"的完整闭环。

### 7.1 行为变化

wails-net check 现在**自动**加载源生成器嵌入的插件清单，无需显式指定目录：

`ash
# 之前（仍可用，作为外部覆盖 / 离线补位）
wails-net check --project MyApp.csproj --permissions-manifest ./manifests

# 现在（默认自动加载源生成器嵌入的清单，可选叠加外部目录）
wails-net check --project MyApp.csproj
`

执行结果会在日志中报告来源计数：

`
已加载权限清单：源生成 137 个 + 外部目录 0 个 = 去重后 137 个已知权限标识
`

### 7.2 合并语义

- **源生成器嵌入**（基线）：编译期由 PluginPermissionManifestGenerator 通过 [ModuleInitializer] 写入 PluginPermissionManifestRegistry，随 Wails.Net.Application.dll 自动加载。
- **外部清单目录**（补位）：--permissions-manifest <dir> 指向的目录下的 *.json 文件，支持数组形式（["x:default", ...]）或对象形式（{"plugin": "x", "permissions": [...]}）。
- **去重合并**：两个源的去重并集；源生成器作为基线，外部清单补全。

### 7.3 显式目录不存在视为错误

如果用户显式指定 --permissions-manifest <dir> 但目录不存在，命令以退出码 1 失败（用户显式声明的资源缺失）。
若不指定该参数，则目录源视为空（仅使用源生成器聚合）。

### 7.4 后续 R3 阶段

- **R3-E（计划）**：库默认从 Compatibility 翻转为 Strict（破坏性变更，需配合 SemVer 2.0.0 → 3.0.0 版本号 bump 与本指南大版本段更新）。

- **R3-D.B/C (2026-08-19)**：wails-net capability 子命令与默认 capability 生成器。
## 8. R3-D.B/C：`wails-net capability` 子命令（2026-08-19 落地）

R3-D.B/C 在 R3-D.A 的基础上提供 capability 文件的 CLI 生命周期管理，对标 Tauri v2
`cargo tauri capability` 体验。

### 8.1 子命令概览

```
wails-net capability init <plugin> [--out <path>]
wails-net capability list
wails-net capability validate <file>
```

数据源均为 `PluginPermissionManifestRegistry`（R3-C 源生成器嵌入的插件清单），
CLI 与运行期共享同一份注册表，确保 `init` 产出的 capability 与运行时声明的权限一致。

### 8.2 `capability init <plugin>`

为指定插件生成默认 capability JSON（含命名权限集 `<plugin>:default` + 全部已声明细粒度权限）。

```bash
# 输出到 stdout（管道友好）
wails-net capability init window

# 输出到文件
wails-net capability init window --out capabilities/window.json
```

输出示例：

```json
{
  "identifier": "window:default",
  "description": "默认能力（自动生成于 R3-D.B）：包含 window 插件全部已声明权限",
  "permissions": [
    "window:default",
    "window:allow-close",
    "window:allow-minimize"
  ]
}
```

库支持（`DefaultCapabilityGenerator`）：

- `GenerateJson(pluginName, manifestJson)` — 单个插件；
- `GenerateAllDefaults()` — 全部已注册插件 → `Dictionary<string, string>`；
- `ExtractPermissions(manifestJson)` — 复用为独立解析器。

### 8.3 `capability list`

列出所有已注册插件 + 其默认 capability 标识符 + 权限计数，便于诊断源生成器是否触发：

```
已注册插件清单：3 个
  - dialog  (5 个权限)  default capability: dialog:default
  - fs  (12 个权限)  default capability: fs:default
  - window  (8 个权限)  default capability: window:default
```

### 8.4 `capability validate <file>`

对单个 capability JSON 做结构校验（CM001-CM005），退出码 0 = 通过（可能含 Warning）/ 1 = 含 Error。

```bash
wails-net capability validate capabilities/main.json
# [成功] capability 校验通过：main-capability
```

底层使用 `CapabilityManifestValidator.ValidateShape`（R3-A.3）。