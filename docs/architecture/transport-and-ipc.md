# 传输层与 IPC 通信

## 1. 概述

Wails.Net 的传输层是连接前端 WebView 与后端 .NET 运行时的**唯一通信桥梁**，承担三类职责：

1. **接收前端调用**——把前端 `window.wails.*` 触发的 JSON 请求送入 [`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs)；
2. **回送响应与事件**——把绑定调用的返回值、`EventProcessor` 广播的事件推送给前端；
3. **承载静态资源**——为 WebView 提供 HTML/CSS/JS 等静态资源服务，避免使用 `file://` 协议。

为了同时覆盖桌面 WebView 场景与 Server 模式（容器化部署、远程调试），项目实现了三种可插拔的传输方式：

| 传输方式 | 实现类 | 适用场景 | 双向通道 |
|---------|--------|---------|---------|
| **IPC 传输** | WebView2 `WebResourceRequested` + `WebMessageReceived` 事件 | 桌面模式（Windows/Linux） | HTTP 拦截 + postMessage |
| **HTTP 传输** | [`HttpTransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs) | Server 模式（无 GUI、容器化） | HTTP POST 轮询端点 |
| **WebSocket 传输** | [`WebSocketTransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketTransport.cs) + [`WebSocketBroadcaster`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketBroadcaster.cs) | Server 模式、远程调试 | 全双工 WebSocket |

三者都遵循同一份 [`ITransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs) 契约，由 [`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 统一处理消息，由 [`AssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 统一服务静态资源，从而把"传输介质差异"与"业务处理逻辑"完全解耦。

## 2. 传输接口 — ITransport 契约

[`ITransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs) 定义了所有传输层必须满足的最小契约，对应 Wails v3 Go 版本 `Transport` 接口：

```csharp
public interface ITransport
{
    string JSClient();                                  // 返回前端 JS 客户端代码
    Task StartAsync(CancellationToken cancellationToken); // 启动传输
    Task StopAsync(CancellationToken cancellationToken);  // 停止传输
}
```

围绕 `ITransport` 还有三个细分接口，分别声明传输层在能力上的可选项：

| 接口 | 用途 | 实现者 |
|------|------|--------|
| [`IWailsEventListener`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs) | 接收 `EventProcessor` 推送的事件并广播到前端 | `HttpTransport`、`WebSocketTransport`、Windows 平台 WebView |
| [`ITransportHttpHandler`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs) | 暴露当前 `HttpListenerContext`，供资源服务器中间件读取请求头/查询参数 | `HttpTransport` |
| [`IAssetServerTransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs) | 绑定 `AssetServer`，传输层在收到资源请求时转发 | `HttpTransport`、`WebSocketTransport` |

这种细粒度接口组合（而非单一大接口）借鉴自 ASP.NET Core 的 marker interface 风格——`MessageProcessor` 只依赖它真正需要的能力，避免强制所有传输层实现无关方法。

## 3. IPC 传输（AssetServerTransport 与 WebView 拦截）

桌面模式下，传输层并不通过独立 HTTP 监听器工作，而是直接挂在 WebView 自身的事件机制上。Windows 平台的 [`Win32WebviewWindow`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 同时注册了两条互补的拦截通道：

### 3.1 WebView2 资源拦截（HTTP 端）

通过 `AddWebResourceRequestedFilter` 把 `http://wails.localhost/*` 与 `https://wails.localhost/*` 的所有请求拦截到 `OnWebResourceRequested`：

```csharp
_webview.AddWebResourceRequestedFilter(
    "http://wails.localhost/*", CoreWebView2WebResourceContext.All);
_webview.AddWebResourceRequestedFilter(
    "https://wails.localhost/*", CoreWebView2WebResourceContext.All);
_webview.WebResourceRequested += OnWebResourceRequested;
```

`OnWebResourceRequested` 内部按路径前缀分流：

- **`POST /wails/*`**——IPC 消息端点。同步读取请求体，调用 `Application.HandleMessageFromFrontend(body, _id)` 进入 [`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs)，并把窗口 ID `_id` 透传给消息上下文，使 `window.*` 操作能定位目标窗口。响应通过 `Environment.CreateWebResourceResponse` 以 `Content-Type: application/json` 返回。
- **其他路径**——静态资源端点。委托给 [`AssetServer.ServeAsync`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs)，再由 `CreateWebResourceResponse` 包装为带 MIME 的响应。若资源不存在且路径无扩展名，则回退到 `index.html`（SPA 路由）。

> **重要约束**：`OnWebResourceRequested` 必须使用同步方法（`GetAwaiter().GetResult()`），而非 `async/await`。Win32 消息循环没有 `SynchronizationContext`，`await` 之后的 continuation 会跑在线程池线程，而 WebView2 的 `CoreWebView2` 成员只能在 UI 线程访问，否则抛出 `InvalidOperationException`。

### 3.2 WebView2 postMessage 通道（异步事件）

第二条通道 `WebMessageReceived` 用于接收前端 `window.chrome.webview.postMessage()` 推送的消息，主要用于拖拽等需要立即响应的事件。`OnWebMessageReceived` 内部先识别 `DragMessageType` 拖拽请求并直接调用 `StartDrag`，其余消息再走 `Application.HandleMessageFromFrontend`，与 HTTP 端汇合到同一处理流程。

后端反向推送（事件广播、运行时回调）则通过 `PostWebMessageAsString` 完成一对一投递：

```csharp
public void PostMessageToWebView(string message)
    => _webview?.PostWebMessageAsString(message);
```

### 3.3 AssetServerTransport 适配器

[`AssetServerTransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/AssetServerTransport.cs) 是一个轻量适配器，把任意实现了 `IAssetServerTransport` 的传输层与一个 [`AssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 绑定：

```csharp
public static void BindToTransport(IAssetServerTransport transport, AssetServer assetServer)
    => transport.ServeAssets(assetServer);
```

绑定后，传输层在收到非 IPC 端点的请求时调用 `ServeAsync(context, ct)`，由 AssetServer 走中间件链 + 核心读取器返回资源字节。若未绑定，则直接返回 404。

### 3.4 消息格式：InvokeRequest / InvokeResponse

前端 `window.wails.*` 触发的调用最终被序列化为如下 JSON 进入 `MessageProcessor`：

```json
// 调用请求（前端 → 后端）
{
  "id": "req-1",
  "type": "call",
  "payload": {
    "name": "GreetingService.Greet",
    "args": ["张三"]
  },
  "windowId": 1
}

// 调用响应（后端 → 前端）
{
  "id": "req-1",
  "type": "response",
  "result": {
    "result": "Hello, 张三",
    "error": null
  }
}
```

`type` 字段支持点分命名空间（`"window.setTitle"`、`"event.emit"`、`"tray.setIcon"`），由 `MessageProcessor` 提取基础命名空间后路由（见第 5 节）。`windowId` 仅在桌面模式下由 WebView 注入，用于让 `window.*` 命令定位目标 `WebviewWindow`。

## 4. WebSocket 传输

### 4.1 WebSocketTransport — 单窗口连接

[`WebSocketTransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketTransport.cs) 在 `HttpListener` 之上实现 WebSocket 升级，默认从端口 `34116` 开始自动查找可用端口（最多重试 1000 次）。处理流程：

1. `ListenLoopAsync` 接到请求后判断 `Upgrade: websocket` 头，是则升级为 WebSocket，否则转发给 `AssetServer`；
2. `HandleWebSocketAsync` 调用 `_broadcaster.AddClient(webSocket)` 注册新连接、生成 `clientId`，并在 `_clientTasks` 字典中跟踪接收循环任务；
3. `ReceiveLoopAsync` 持续读取消息片段，累积到 `EndOfMessage` 后交给 `ProcessWebSocketMessageAsync`；
4. `ProcessWebSocketMessageAsync` 调用 `_processor.ParseMessage` + `ProcessAsync`，并通过 `_broadcaster.SendToClientAsync(clientId, json)` 把响应**只回送给当前客户端**（不会广播到其他窗口）；
5. 连接断开时 `RemoveClient` + `Dispose`，确保资源释放。

### 4.2 WebSocketBroadcaster — 多窗口广播

[`WebSocketBroadcaster`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketBroadcaster.cs) 是事件广播的核心，对应 Wails v3 Go 版本的 WebSocket 广播实现：

- `_clients`：`ConcurrentDictionary<string, WebSocket>` 维护所有已连接客户端；
- `_nextClientId`：`Interlocked.Increment` 保证线程安全的 ID 生成（遵循 [`AGENTS.md`](file:///f:/Code/Dotnet/Wails.Net/AGENTS.md) 第 3.2 节线程安全规范）；
- `BroadcastEventAsync`：序列化为 `{type:"event", name, data}` 后并发 `Task.WhenAll` 发送给所有 `Open` 状态客户端；
- `SendToAllExceptAsync`：广播但排除指定客户端，避免事件回传给发送者窗口造成循环；
- `SendToClientAsync`：定向发送，用于返回调用响应。

事件消息格式如下：

```json
{
  "type": "event",
  "name": "wails:window:setTitle",
  "data": { "title": "新标题" }
}
```

`WebSocketTransport.NotifyEvent` 直接委托给 `_broadcaster.BroadcastEvent`，从而使后端 `EventProcessor.Emit(...)` 能通过实现 `IWailsEventListener` 接口的传输层广播到所有连接的浏览器/WebView 客户端。**适用场景**：Server 模式下的多浏览器会话、远程调试、容器化部署中通过端口映射访问应用。

## 5. HTTP 传输（HttpTransport）

[`HttpTransport`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs) 是 Server 模式的"轮询式"实现，从端口 `34115` 开始自动查找可用端口，并定义两个端点：

```csharp
public const string MessageEndpoint   = "/wails/message"; // POST IPC 消息
public const string WebSocketEndpoint = "/wails/ws";       // WebSocket 升级（占位，实际由 WebSocketTransport 处理）
```

`HandleRequestAsync` 的分流逻辑与 Windows WebView 拦截高度一致：

1. **OPTIONS 预检**——直接返回 204，并写入 CORS 头 `Access-Control-Allow-Origin: *`、`Access-Control-Allow-Methods: GET, POST, OPTIONS`、`Access-Control-Allow-Headers: Content-Type, Range, If-None-Match, x-wails-window-id, x-wails-window-name`；
2. **`POST /wails/message`**——同步读取请求体，调用 `_processor.ParseMessage(body)` + `ProcessAsync(message)`，把 `ResponseMessage` 序列化为 JSON 返回；若结果为 null 则返回 204；
3. **其他路径**——转发给 `_assetServer.ServeHttpAsync(context, ct)`；
4. **未绑定 AssetServer**——返回 404 文本。

`HttpTransport` 通过 `AsyncLocal<HttpListenerContext?>` 实现 [`ITransportHttpHandler.GetCurrentContext`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs)，让资源服务器中间件无需额外参数即可读取当前请求上下文，借鉴 ASP.NET Core 的 `HttpContextAccessor` 模式。`NotifyEvent` 委托给 `_broadcaster.BroadcastEvent`，与 WebSocket 传输共用同一广播器实例。

## 6. 消息处理器（MessageProcessor）

[`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 是传输层与业务层之间的唯一中介，对应 Wails v3 Go 版本 `internal/runtime/messageprocessor.go`。所有传输层（HTTP/WebSocket/WebView 拦截）都通过 `ParseMessage` + `ProcessAsync` 进入它。

### 6.1 消息类型与点分命名空间路由

`MessageTypes` 静态类定义了与 Wails v3 前端协议一致的消息类型常量：

| 常量 | 值 | 含义 |
|------|----|----|
| `Call` | `"call"` | 绑定/命令调用 |
| `Event` | `"event"` | 事件发布 |
| `Window` | `"window"` | 窗口操作 |
| `Query` | `"query"` | 查询请求（如列出绑定方法） |
| `Response` | `"response"` | 响应消息 |
| `Error` | `"error"` | 错误消息 |
| `Drag` | `"drag"` | 拖放操作 |
| `ContextMenu` | `"contextmenu"` | 右键菜单 |
| `System` | `"system"` | 系统命令 |

`ProcessAsync` 的核心路由逻辑首先**提取点分命名空间的基础类型**：

```csharp
var baseType = message.Type;
var dotIndex = message.Type.IndexOf('.');
if (dotIndex > 0) baseType = message.Type[..dotIndex];

return baseType switch
{
    MessageTypes.Call       => await ProcessCallAsync(message),
    MessageTypes.Event     => ProcessEvent(message),
    MessageTypes.Query     => ProcessQuery(message),
    MessageTypes.Drag      => ProcessDrag(message),
    MessageTypes.ContextMenu => ProcessContextMenu(message),
    MessageTypes.Window    => await ProcessWindowAsync(message),
    _ => await ProcessCommandFallbackAsync(message) // 未识别命名空间兜底
};
```

这意味着 `"window.setTitle"`、`"window.minimize"` 都路由到 `ProcessWindowAsync`，而 `"tray.setIcon"`、`"application.hide"`、`"notification.show"` 等未识别命名空间走兜底路径。

### 6.2 call 路径 — BindingManager → CommandDispatcher

`ProcessCallAsync` 优先按 `payload.id`（FNV-1a 哈希）调用 [`BindingManager`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs)，其次按 `payload.name`。当 `BindingManager` 返回 "未找到" 错误且 `CommandDispatcher` 已配置时，自动回退到 `TryDispatchCommandAsync`——这是与 [绑定系统与命令调度](file:///f:/Code/Dotnet/Wails.Net/docs/architecture/binding-and-command-system.md) 的关键集成点。

### 6.3 window 路径 — WindowPlugin 优先 + 硬编码回退

`ProcessWindowAsync` 体现"核心即插件"哲学（借鉴 Tauri v2）：

1. 从 `message.Type` 提取 action（如 `"window.setTitle"` → `setTitle`），若为空则从 `WindowPayload.Action` 读取（向后兼容）；
2. **优先**调用 `ProcessCommandFallbackAsync` 走 `WindowPlugin` 注册的 `window.*` 命令路径；
3. 命令未命中则回退到 `DispatchWindowAction` 硬编码 switch 分发（向后兼容未注册 WindowPlugin 的场景）；
4. 通过 `_windowLookup(windowId.Value)` 查找目标 `WebviewWindow` 实例，找不到则返回 `ReferenceError`；
5. 操作完成后 `_events.Emit($"wails:window:{action}", payload, message.WindowId)` 广播事件，让其他窗口能感知变化。

`DispatchWindowAction` 覆盖了 60+ 个窗口操作（`settitle`、`setsize`、`minimize`、`maximize`、`setfullscreen`、`opendevtools`、`printtopdf`、`execjs`、`injectcss`、`getsize`、`isfullscreen` 等）。

### 6.4 未识别命名空间兜底

`ProcessCommandFallbackAsync` 把 `message.Type` 直接作为命令名（如 `"notification.show"`），构造 `InvokeRequest(Guid, Method, Parameters)` 派发到 `CommandDispatcher`。`CommandContext` 通过 `IServiceProvider` + `WindowId` + `CancellationToken` 三元组创建，使命令方法既能从 DI 取依赖，又能通过 `ICommandContext.WindowId` 定位目标窗口。失败时返回 `null` 保持 `_ => null` 的向后兼容。

### 6.5 异步队列消费

`MessageProcessor` 还内建 `BlockingCollection<Message>` 队列：`Enqueue` 把消息入队，`ProcessQueueAsync` 后台消费循环逐条调用 `ProcessAsync`，异常不会中断循环。`Start()` / `StopAsync()` 提供生命周期管理，与 [`DesktopHost`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting) 的服务启动/关闭顺序对齐。

## 7. 资源服务器（AssetServer）

[`AssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 同时服务于桌面 WebView 拦截与 Server 模式 HTTP 监听器，对应 Wails v3 Go 版本 `internal/assetserver/assetserver.go`。

### 7.1 中间件管道模式

[`MiddlewareChain`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Middleware/MiddlewareChain.cs) 同时管理两类中间件：

- [`IMiddleware`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Middleware/IMiddleware.cs)——基于路径，签名 `Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)`；
- [`IHttpMiddleware`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Middleware/IMiddleware.cs)——基于 HTTP 上下文，签名 `Task<bool> ProcessAsync(HttpListenerContext context, Func<Task> next)`，返回 `true` 表示短路。

`ExecuteAsync` 与 `ExecuteHttpAsync` 都通过**倒序构建委托链**实现"先注册先执行"的语义（类似 ASP.NET Core 的 `IApplicationBuilder.Use`）。`AssetServer.Use` 是注册入口，最终处理器是 `ReadAssetCore(path)`。

### 7.2 两种资源来源

| 实现类 | 数据源 | 关键方法 |
|--------|--------|---------|
| [`FileAssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/FileAssetServer.cs) | 文件系统 | `ReadFile(path)` + `GetLastModified(path)` |
| [`BundledAssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/BundledAssetServer.cs) | 程序集嵌入资源 | `ReadResource(path)` + 通配符匹配 + 多程序集合并查找 |

[`FileAssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/FileAssetServer.cs) 在 `ResolveFullPath` 中通过 `StartsWith(fullRoot, OrdinalIgnoreCase)` 防止路径穿越攻击，并重写 `ServeHttpAsync` 在基类处理之前注入 `Last-Modified` 头（RFC 1123 格式）。

[`BundledAssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/BundledAssetServer.cs) 把 `/`、`\` 替换为 `.` 后查询 `GetManifestResourceStream`，并支持三步匹配：精确匹配 → 通配符匹配（`*` → `.*`、`?` → `.`）→ 程序集默认命名空间前缀补全。`AddAssembly` 支持多程序集合并查找。

### 7.3 Range / ETag / MIME

`AssetServer.ServeHttpAsync` 的核心流程：

1. 写入 CORS 头、可选 CSP 头；
2. OPTIONS 预检直接返回 204；
3. 走 HTTP 中间件链，未短路则进入 `ServeAssetCoreAsync`；
4. 通过中间件链读取资源字节，空则 `ServeNotFound`（404）；
5. 计算 MIME（按扩展名映射 30+ 类型，未知返回 `application/octet-stream`）；
6. 设置 `Accept-Ranges: bytes`，计算 SHA-256 前 8 字节的 ETag；
7. 处理 `If-None-Match` 命中则返回 304；
8. 处理 `Range: bytes=start-end`，有效则返回 206 Partial Content（含 `Content-Range`），无效返回 416；
9. 默认返回 200 + 完整内容。

### 7.4 配置 — DesktopHostOptions.Assets

资源服务器通过 [`DesktopHostOptions.Assets`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostOptions.cs) 配置：

```csharp
public class AssetsOptions
{
    public string RootPath { get; set; } = string.Empty;            // 静态资源根路径
    public string DefaultDocument { get; set; } = "index.html";   // 目录默认文档
    public bool EnableSpaFallback { get; set; } = true;            // SPA 路由回退
}
```

设置 `RootPath` 后，[`DesktopApplicationBuilder`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs) 会自动创建 `FileAssetServer` 并通过 `http://wails.localhost/` 提供服务，无需 `file://` 协议——避免后者带来的 WebView2 沙箱权限问题。

## 8. 运行时 JS 注入

前端能调用 `window.wails.*` API 的前提是 [`RuntimeGenerator`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 生成的运行时 JS 已注入到 WebView。

### 8.1 Generate(options) — 生成 window.wails API

`RuntimeGenerator.Generate(RuntimeOptions)` 由四部分拼接而成：

```csharp
var flags          = GenerateFlags(options);          // window._wails 标志对象
var api           = GenerateApi(options);             // window.wails API 对象
var transport     = LoadTemplate(TransportTemplateFileName, options); // 传输层模板
var platformRuntime = options.IsServerMode
    ? ServerRuntime.Generate(options)
    : DesktopRuntime.Generate(options);
return $"{flags}\n{api}\n{transport}\n{platformRuntime}";
```

- `GenerateFlags` 生成 `window._wails = { platform, isDebug, isServerMode }`；
- `GenerateApi` 生成 `window.wails` 命名空间对象，涵盖 `call`、`bindings`、`events`、`window`、`tray`、`windows`、`screen`、`clipboard`、`dialog`、`menu`、`application`、`stronghold`、`scope`、`localhost`、`fswatch`、`system`、`power`、`process`、`fs`、`shell`、`notification`、`store`、`log` 等 20+ 命名空间。所有方法最终汇聚到 `window._wailsInvoke(type, payload)`，由传输层模板根据 `IsServerMode` 选择 HTTP POST `/wails/message` 或 WebSocket `ws://localhost:port/wails/ws`。
- `LoadTemplate` 从程序集嵌入资源加载 `transport.template.js`、`runtime.template.js`，并用 `ReplacePlaceholders` 替换 `{PLATFORM}`、`{IS_DEBUG}`、`{IS_SERVER_MODE}`、`{ASSET_SERVER_URL}`、`{WEBSOCKET_URL}` 等占位符。

### 8.2 注入方式 — AddScriptToExecuteOnDocumentCreatedAsync

注入时机至关重要：必须在页面脚本执行**之前**完成，否则页面访问 `window.wails` 会得到 `undefined`。Windows 平台通过 WebView2 的 `AddScriptToExecuteOnDocumentCreatedAsync` 实现：

```csharp
// Win32WebviewWindow.InjectRuntimeJs()
var js = app.GenerateRuntimeJs(false);
_ = _webview.AddScriptToExecuteOnDocumentCreatedAsync(js);
```

该方法注册的脚本会在每个新文档创建时、页面脚本执行前自动运行，**仅注册一次**即对后续所有导航生效。`NavigationCompleted` 事件中**不再重复注入**——这一点在 [`Win32WebviewWindow`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 中有显式注释提醒：`ExecuteScriptAsync` 只在调用时执行一次，时机太晚。

### 8.3 与 HTTP 路由的关系

**关键区别**：运行时 JS **不经过 HTTP 路由**——它由 WebView2 内部直接注入到页面脚本上下文，绕过 `OnWebResourceRequested` 拦截。这意味着即使是空 `RootPath`（无 `AssetServer`）的场景，前端依然能调用 `window.wails.*` API。运行时 JS 内部的 `window._wailsInvoke(...)` 才是真正触发 IPC 的入口：桌面模式下走 `fetch("/wails/message", {method:"POST", body:...})`，由 WebView2 拦截；Server 模式下走真实 HTTP 或 WebSocket。

## 9. 消息格式汇总

下表汇总了所有跨边界消息的 JSON 结构：

### 9.1 InvokeRequest（前端 → 后端）

```json
{
  "id": "req-1",
  "type": "call",                      // 或 event / window / window.setTitle / drag / contextmenu / query
  "payload": {                          // 类型相关
    "name": "GreetingService.Greet",    // call: 绑定名
    "args": ["张三"]                    // call: 参数数组
  },
  "windowId": 1                          // 桌面模式由 WebView 注入；Server 模式可为 null
}
```

不同 `type` 对应的 `payload` 类型由 [`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 末尾的 payload 类定义：

| type | Payload 类 | 关键字段 |
|------|-----------|---------|
| `call` | `CallPayload` | `id?`（uint?）、`name?`（string）、`args?`（JsonElement[]） |
| `event` | `EventPayload` | `name`、`data`、`senderWindowId?` |
| `window` | `WindowPayload` | `action`、`windowId?`、`data?` |
| `query` | `QueryPayload` | `query`（如 `"bindings"`、`"events"`） |
| `drag` | `DragPayload` | `files?`、`data?`、`x`、`y`、`windowId?` |
| `contextmenu` | `ContextMenuPayload` | `x`、`y`、`contextId?`、`windowId?` |

### 9.2 InvokeResponse（后端 → 前端）

```json
{
  "id": "req-1",
  "type": "response",                   // 或 error
  "result": {
    "result": "Hello, 张三",            // 调用返回值；错误时为 null
    "error": null                        // CallError.ToJson() 或 null
  }
}
```

错误响应示例（[`CallError`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Errors) 的 `ToJson()` 输出）：

```json
{
  "id": "req-1",
  "type": "error",
  "result": {
    "result": null,
    "error": {
      "message": "找不到 ID 为 99 的窗口",
      "cause": null,
      "kind": "ReferenceError"          // ReferenceError / TypeError / RuntimeError
    }
  }
}
```

### 9.3 事件广播（后端 → 前端，由 `WebSocketBroadcaster.BroadcastEventAsync` 发送）

```json
{
  "type": "event",
  "name": "wails:window:setTitle",
  "data": { "title": "新标题" }
}
```

### 9.4 命令调度内部协议

在 `MessageProcessor` 与 `CommandDispatcher` 之间使用 [`InvokeRequest`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/InvokeRequest.cs) / [`InvokeResponse`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/InvokeResponse.cs) 这对 record 类型：

```csharp
public sealed record InvokeRequest (Guid Id, string Method, JsonElement Parameters);
public sealed record InvokeResponse(Guid Id, bool Success, object? Result, string? Error);
```

这对类型是后端内部协议，**不会**直接序列化到前端——前端只看到 `ResponseMessage` 的 `{result, error}` 结构，从而保持前后端契约的稳定性。

## 10. 总结

Wails.Net 传输层的设计体现了三层解耦：

1. **传输介质解耦**——`ITransport` + `IWailsEventListener` + `IAssetServerTransport` 让桌面 WebView 拦截、HTTP 轮询、WebSocket 双工三种介质共用同一份 [`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 与 [`AssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs)；
2. **路由策略解耦**——`MessageProcessor.ProcessAsync` 通过点分命名空间 + 兜底命令派发，把 `call`/`event`/`window` 等业务路由与传输层完全分离，新加命名空间只需注册插件命令即可，无需修改传输层；
3. **资源来源解耦**——`AssetServer` 通过中间件链 + 虚方法 `ReadAssetCore` 把资源读取抽象化，[`FileAssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/FileAssetServer.cs) 与 [`BundledAssetServer`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/BundledAssetServer.cs) 可互换，开发模式与发布模式共享同一套 Range/ETag/MIME 逻辑。

运行时 JS 通过 `AddScriptToExecuteOnDocumentCreatedAsync` 在页面脚本前注入，与 HTTP 路由解耦——即使无 AssetServer 也能使用 `window.wails.*` API。这套架构让 Wails.Net 既能复刻 Wails v3 的桌面 WebView 体验，又能以 Server 模式部署到容器与远程调试环境，迁移成本与协议一致性都得到保证。
