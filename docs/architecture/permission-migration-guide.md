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