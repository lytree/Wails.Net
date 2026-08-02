# @wails-net/runtime

Wails.Net（Wails v3 的 .NET 10 移植版）前端运行时 SDK，使用 **TypeScript** 编写、**pnpm** 管理。

提供：

- **自包含 IPC 传输层**：自动检测 WebView2 / WebKitGTK / Android 原生 `postMessage`、大消息 HTTP 分块上传、Server 模式 WebSocket，以及调用取消。
- **全部核心命名空间** 与 **全部 46 个插件命令** 的强类型封装（window / dialog / clipboard / fs / http / notification / store / camera / geolocation / permissions …）。
- 与 Wails.Net 后端 `MessageProcessor` / `ResponseMessage` 协议严格一致，包括 **双层响应解包** 与 **FNV-1a 绑定 ID**。

> 与 C# 端 `Wails.Net.Generator` 的兼容：`wails.bindings.call(id, [args])` 由本包的 `wails.bindings.call` 与 `wails.bindings.id(fullName)` 提供支持。将 `TypeScriptGenerator.cs` 中的 `import { wails } from '@wails/runtime'` 改为 `@wails-net/runtime` 即可直接复用其生成产物。

## 安装

```bash
pnpm add @wails-net/runtime
# 开发
pnpm install
pnpm build      # tsc -> dist/ (ESM + .d.ts)
pnpm test       # vitest
pnpm typecheck  # tsc --noEmit
```

> 本包仅依赖浏览器/WebView 内置 API（`fetch` / `WebSocket` / `TextEncoder` / `crypto`），**无第三方运行时依赖**。

## 快速开始

```ts
import { wails } from "@wails-net/runtime";

// 调用后端绑定方法或插件命令（位置参数数组）
const greet = await wails.call<string>("GreetingService.Greet", ["World"]);

// 按 FNV-1a ID 调用（与 C# 源生成器一致）
const id = wails.bindings.id("GreetingService.Greet");
const r2 = await wails.bindings.call<string>(id, ["World"]);

// 插件命令（强类型命名空间）
await wails.clipboard.setText("hello");
const text = await wails.clipboard.getText();
await wails.dialog.message("标题", "内容");

// 可取消调用
const p = wails.http.get("https://example.com");
p.cancel(); // 向后端发送 cancel

// 事件（订阅为纯前端逻辑；62 个内建 wails:* 事件 + 自定义事件）
const off = wails.events.on("wails:window:created", (e) => console.log(e));
wails.events.emit("my:event", { foo: 1 });
off();
```

## 架构

```
前端调用 ──▶ wails.call(name, args) / wails.<ns>.<method>(...)
                │
                ▼
        transport.invoke/send  （internal/transport.ts）
                │  通道选择：
                ├─ Server 模式  → WebSocket (ws://localhost:34116/wails/ws)
                ├─ 原生 postMessage（≤512KB，低延迟）
                │     Windows: window.chrome.webview.postMessage
                │     Linux:   window.webkit.messageHandlers.external.postMessage
                │     Android: window.wails.invoke
                └─ 其余          → HTTP POST /wails/message（>512KB 自动分块）
                │
                ▼
   后端 MessageProcessor → BindingManager / CommandDispatcher(插件)
                │
                ▼
   响应：{ id, type:"response", result:{ result, error } }  ← 双层解包
   事件：{ type:"event", name, data }                       ← 推送至前端订阅者
```

### 关键协议约定（来自 AGENTS.md / MessageProcessor）

1. **所有绑定与插件命令统一走 `type:"call"` + `{ name, args:[...] }`**，多参数必须为位置数组。
2. **响应双层解包**：`result.result` 为业务值，`result.error` 为错误（含 `kind`：`ReferenceError` / `TypeError` / `RuntimeError`，权限拒绝表现为 `RuntimeError`）。
3. **`event.emit` / `drag` / `contextmenu` 不返回响应** —— SDK 内部以 fire-and-forget 发送，不会创建悬挂 Promise。
4. **调用 ID 为字符串且全局唯一**，取消依赖它作为 `callId`；`cancel` 始终走可靠通道（Server 模式走 WS，否则走 HTTP）。
5. **`byte[]` ↔ Base64 字符串**，`Guid` ↔ 字符串，`DateTime` ↔ ISO-8601。
6. **FNV-1a 绑定 ID** 必须与后端 `Bindings.FNV1aHash` 一致（offsetBasis=2166136261，prime=16777619，UTF-8 字节）。

## 后端集成提示

本包为**自包含**，不再依赖 C# 端注入的 `window.wails` / `window._wailsInvoke`。但为获知运行配置（Server 模式、各 URL、platform），它仍会读取 C# `RuntimeGenerator` 注入的轻量 `window._wails` 标志对象：

```js
window._wails = { platform: "windows", isDebug: true, isServerMode: false,
                  assetServerUrl: "...", webSocketUrl: "..." };
```

保持该标志注入即可；前端改为 `import { wails } from "@wails-net/runtime"`，无需再加载运行时 JS。

## 目录结构

```
src/
  index.ts                 # 汇总 wails 对象与全部公共导出
  core/runtime.ts          # call / bindings / events / query / invoke / cancel
  internal/
    transport.ts           # 自包含传输层（通道检测 / 分块 / WS / 取消 / 解包）
    types.ts               # 协议类型
    fnv1a.ts               # FNV-1a 哈希
    call-error.ts          # CallError
  api/
    window.ts clipboard.ts dialog.ts application.ts fs.ts
    net.ts data.ts notification.ts log.ts mobile.ts common.ts
```

## License

MIT
