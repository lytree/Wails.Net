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
