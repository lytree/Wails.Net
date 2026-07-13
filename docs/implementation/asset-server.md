# 资源服务器实现

> 本文档详细描述 Wails.Net 项目中 AssetServer 资源服务器的设计与实现，包括核心类、中间件管道、文件系统与嵌入式资源两种资源来源，以及 Windows / Linux 两个平台的集成方式。

---

## 1. 概述

`AssetServer` 为 Wails.Net 应用提供 HTTP 静态资源服务，对应 Wails v3 Go 版本 `internal/assetserver/assetserver.go` 中的 `AssetServer` 结构。其核心目标是用 **`http://wails.localhost/`（Windows）** 和 **`wails://localhost/`（Linux）** 虚拟主机替代 `file://` 协议加载前端资源，避免以下问题：

- `file://` 协议受 WebView2 / WebKitGTK 安全策略限制，无法发起 XHR / fetch 请求
- 前端路由（Vue/React/Angular）在 `file://` 下无法回退到 `index.html`
- CSP、CORS、Range、ETag 等高级 HTTP 特性在 `file://` 下不可用

资源服务器位于 [Wails.Net.AssetServer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer) 程序集，被 [Wails.Net.Application](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application) 引用，最终由各平台 Webview 窗口通过虚拟主机协议拦截调用。

---

## 2. AssetServer 核心类

`AssetServer` 是所有资源服务器的基类，定义在 [AssetServer.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs)。它提供两条独立的处理路径：基于路径的简单读取与完整的 HTTP 处理。

### 2.1 ServeAsync —— 简单路径处理

```csharp
public virtual async Task<byte[]> ServeAsync(string path)
{
    ArgumentException.ThrowIfNullOrEmpty(path);
    var result = await _middlewareChain.ExecuteAsync(path, p => Task.FromResult(ReadAssetCore(p)));
    return result ?? [];
}
```

仅返回资源字节内容，适用于不依赖完整 HTTP 上下文的调用方（如 Windows / Linux 平台 Webview 拦截器）。请求先经过路径中间件链，最终由派生类重写的 `ReadAssetCore` 兜底。

### 2.2 ServeHttpAsync —— 完整 HTTP 处理

`ServeHttpAsync(HttpListenerContext, CancellationToken)` 是对应 Go 版本 `AssetServer.ServeHTTP` 的完整实现，依次执行：

1. **CORS 头**：写入 `Access-Control-Allow-Origin: *`
2. **CSP 头**：若通过 `SetCspHeader` 注入了内容安全策略，写入 `Content-Security-Policy`
3. **OPTIONS 预检**：返回 204 No Content
4. **路径解析**：取 `Url.AbsolutePath` 并去除查询参数
5. **HTTP 中间件链**：通过 `ExecuteHttpAsync` 执行，中间件可短路返回
6. **核心资源处理**（`ServeAssetCoreAsync`）：读取内容 → MIME → ETag → 304/Range/200

### 2.3 MIME 类型映射

`GetMimeType` 按文件扩展名返回 MIME 类型，覆盖常见前端资源类型：

```csharp
return extension switch
{
    ".html" or ".htm" => "text/html",
    ".css" => "text/css",
    ".js" or ".mjs" => "application/javascript",
    ".json" => "application/json",
    ".svg" => "image/svg+xml",
    ".woff2" => "font/woff2",
    ".wasm" => "application/wasm",
    _ => "application/octet-stream"
};
```

**关键陷阱**：Windows 平台拦截器调用 `GetMimeType` 时必须传入规范化后的 `assetPath`（如 `index.html`），而非原始 `path`（可能是 `/`）。否则根路径请求会得到 `application/octet-stream`，导致 WebView2 把 HTML 当作下载文件渲染（白屏 + 下载按钮）。

### 2.4 ETag 计算

ETag 基于资源内容的 SHA-256 哈希**前 8 字节（16 个十六进制字符）**，用双引号包裹：

```csharp
private static string ComputeETag(byte[] content)
{
    var hash = SHA256.HashData(content);
    var sb = new StringBuilder(40);
    sb.Append('"');
    for (var i = 0; i < 8; i++)
    {
        sb.Append(hash[i].ToString("x2"));
    }
    sb.Append('"');
    return sb.ToString();
}
```

服务端在响应中写入 `ETag` 头，并检查请求的 `If-None-Match`：匹配则返回 **304 Not Modified**，避免重复传输。`Cache-Control` 默认为 `no-cache`，强制客户端每次校验 ETag。

### 2.5 Range 请求处理

支持标准 `Range: bytes=start-end` 头，用于大文件分段下载与媒体文件拖动播放。`ParseRangeHeader` 解析以下三种格式：

- `bytes=0-1023`：从 0 到 1023 字节
- `bytes=1024-`：从 1024 到末尾
- `bytes=-512`：最后 512 字节（后缀范围）

解析成功则返回 **206 Partial Content**，写入 `Content-Range: bytes start-end/total`，分块（81920 字节）写入响应流；解析失败返回 **416 Range Not Satisfiable**。

### 2.6 Headers 常量与错误处理

`AssetServer.Headers` 内部类集中定义所有 HTTP 头名称常量（`Content-Type`、`ETag`、`Range`、`x-wails-window-id` 等），与 Go 版本 `common.go` 保持一致。

`ServeNotFound` 和 `ServeError` 受 `AssetOptions.ErrorHandler` 保护：若配置了自定义错误处理器则委托调用，否则返回默认 `404 Not Found: {path}` 或 `500 Internal Server Error: {ex.Message}` 纯文本响应。

---

## 3. 中间件管道

中间件机制对应 Wails v3 Go 版本 assetserver 的中间件编排逻辑，由 [IMiddleware.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Middleware/IMiddleware.cs) 和 [MiddlewareChain.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Middleware/MiddlewareChain.cs) 实现，提供两套互补的接口。

### 3.1 IMiddleware —— 基于路径的中间件

```csharp
public interface IMiddleware
{
    Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next);
}
```

只接收资源路径，返回字节数组或 `null`。适用于纯内容转换场景（如压缩、加密、内容注入）。

### 3.2 IHttpMiddleware —— 基于 HTTP 上下文的中间件

```csharp
public interface IHttpMiddleware
{
    Task<bool> ProcessAsync(HttpListenerContext context, Func<Task> next);
}
```

接收完整 `HttpListenerContext`，返回 `true` 表示已完全处理请求（后续中间件和最终处理器不再执行），返回 `false` 则交由链继续处理。适用于需要读写响应头、状态码、Range 请求的场景。

### 3.3 MiddlewareChain —— 链式执行

`MiddlewareChain` 维护两条独立的中间件列表，分别通过 `Use(IMiddleware)` 和 `Use(IHttpMiddleware)` 注册。

**`ExecuteAsync`** 使用**倒序构建委托链**，使最先注册的中间件最先执行：

```csharp
Func<string, Task<byte[]?>> pipeline = finalHandler;
for (int i = _middlewares.Count - 1; i >= 0; i--)
{
    var current = _middlewares[i];
    var next = pipeline;
    pipeline = p => current.ProcessAsync(p, next);
}
return pipeline(path);
```

**`ExecuteHttpAsync`** 同样倒序构建，并通过 `handled` 标志支持短路：任一中间件返回 `true` 则后续中间件和 `finalHandler` 均不再执行。

`AssetServer.Use(IMiddleware)` 和 `AssetServer.Use(IHttpMiddleware)` 是对 `_middlewareChain.Use` 的薄包装，向应用层暴露注册入口。

---

## 4. FileAssetServer

`FileAssetServer` 从文件系统读取资源，对应 Go 版本 `internal/assetserver/fileassets`，定义在 [FileAssetServer.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/FileAssetServer.cs)。开发模式下前端构建产物（如 Vite 的 `dist/`）直接由文件系统提供，支持热重载与调试源映射。

### 4.1 文件系统读取

```csharp
public byte[]? ReadFile(string path)
{
    if (string.IsNullOrEmpty(path)) return null;
    var fullPath = ResolveFullPath(path);
    if (fullPath is null || !File.Exists(fullPath)) return null;
    return File.ReadAllBytes(fullPath);
}
```

`ReadAssetCore` 重写为委托 `ReadFile`，使基类的 `ServeAsync` 与 `ServeAssetCoreAsync` 能透明调用文件系统。

### 4.2 路径规范化与安全防护

`ResolveFullPath` 是防止**路径穿越攻击**的关键防线：

```csharp
private string? ResolveFullPath(string path)
{
    var normalizedPath = path.TrimStart('/', '\\');
    var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedPath));
    var fullRoot = Path.GetFullPath(_rootPath);

    // 确保解析后的路径仍在根路径之下，防止路径穿越。
    if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }
    return fullPath;
}
```

即使请求路径包含 `../` 或符号链接，`Path.GetFullPath` 会规范化为绝对路径，再与根路径前缀比较，确保资源不会越界读取系统文件。

### 4.3 Last-Modified 头

`FileAssetServer` 重写 `ServeHttpAsync`，在调用基类之前写入 `Last-Modified` 头（RFC 1123 格式）：

```csharp
public override async Task ServeHttpAsync(HttpListenerContext context, CancellationToken ct = default)
{
    var path = context.Request.Url?.AbsolutePath.Split('?')[0] ?? "/";
    var lastModified = GetLastModified(path);
    if (lastModified is not null)
    {
        context.Response.Headers[Headers.LastModified] =
            lastModified.Value.ToString("R", CultureInfo.InvariantCulture);
    }
    await base.ServeHttpAsync(context, ct);
}
```

### 4.4 SPA 路由回退与 DefaultDocument

**重要**：SPA 路由回退（`EnableSpaFallback` → `index.html`）和 `DefaultDocument` 配置项定义在 [DesktopHostOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostOptions.cs) 的 `AssetsOptions` 中，但**实际回退逻辑由各平台 Webview 窗口实现**（见第 7、8 节），而非 `FileAssetServer` 本身。`FileAssetServer` 仅负责按路径读取文件，回退策略由上层根据 `EnableSpaFallback` 配置决定。

---

## 5. BundledAssetServer

`BundledAssetServer` 从程序集嵌入资源中读取文件，对应 Go 版本 `internal/assetserver/bundledassets`，定义在 [BundledAssetServer.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/BundledAssetServer.cs)。生产模式下前端构建产物通过 `<EmbeddedResource>` 嵌入主程序集，实现单文件部署。

### 5.1 多程序集合并查找

```csharp
public BundledAssetServer(IEnumerable<Assembly> assemblies)
    : base(CreateOptions(assemblies.FirstOrDefault()))
{
    foreach (var assembly in assemblies)
    {
        _assemblies.Add(assembly);
    }
}
```

支持通过 `AddAssembly` 动态添加程序集。查找时按注册顺序遍历，第一个匹配即返回，便于将插件资源与主应用资源分层组合。

### 5.2 资源名规范化

`NormalizeResourceName` 将 URL 风格的路径转换为 .NET 嵌入资源名（点分命名）：

```csharp
private static string NormalizeResourceName(string path)
{
    var normalized = path.Replace('/', '.').Replace('\\', '.');
    return normalized.TrimStart('.');
}
```

例如 `/assets/index.html` 被规范化为 `assets.index.html`。

### 5.3 三级查找策略

`ReadResource` 对每个程序集依次尝试三种匹配方式：

1. **精确匹配**：`assembly.GetManifestResourceStream(resourceName)`
2. **通配符匹配**：若资源名包含 `*` 或 `?`，调用 `FindByPattern`
3. **后缀匹配**：在资源名前添加程序集默认命名空间（`assemblyName + "." + resourceName`）

第三级匹配解决了 .NET 嵌入资源默认带命名空间前缀的约定（如 `MyApp.assets.index.html`），使前端代码可以使用相对路径请求。

### 5.4 通配符匹配 FindByPattern

```csharp
private string? FindByPattern(Assembly assembly, string pattern)
{
    var regexPattern = "^" + Regex.Escape(pattern)
        .Replace("\\*", ".*")
        .Replace("\\?", ".") + "$";
    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

    foreach (var name in GetResourceNames(assembly))
    {
        if (regex.IsMatch(name)) return name;
    }
    return null;
}
```

将 `*` 转为 `.*`、`?` 转为 `.` 构造正则，遍历程序集资源名列表（带缓存）返回首个匹配。`GetResourceNames` 缓存了排序后的资源名列表，避免每次请求都反射。

---

## 6. AssetOptions 配置

[AssetOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetOptions.cs) 描述资源服务器自身的选项，对应 Go 版本 `internal/assetserver/options.go`：

| 属性 | 类型 | 说明 |
|------|------|------|
| `Handler` | `string` | 资源处理器名称（`"file"` / `"bundled"`） |
| `RootPath` | `string` | 资源根路径（文件系统路径或程序集名） |
| `Middleware` | `Dictionary<string, string>` | 中间件配置字典（键值参数） |
| `ErrorHandler` | `Action<HttpListenerContext, Exception>?` | 自定义错误处理回调 |
| `HandlerTimeout` | `TimeSpan` | 单次请求超时，默认 30 秒 |

`FileAssetServer` 和 `BundledAssetServer` 各自在构造时通过私有 `CreateOptions` 方法生成默认 `AssetOptions`。

---

## 7. Windows 平台集成

Windows 平台通过 WebView2 的 `AddWebResourceRequestedFilter` 拦截虚拟主机请求，实现位于 [Win32WebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs)。

### 7.1 虚拟主机过滤注册

```csharp
_webview.AddWebResourceRequestedFilter(
    "http://wails.localhost/*", CoreWebView2WebResourceContext.All);
_webview.AddWebResourceRequestedFilter(
    "https://wails.localhost/*", CoreWebView2WebResourceContext.All);
_webview.WebResourceRequested += OnWebResourceRequested;
```

同时注册 `http://` 和 `https://` 两个变体，兼容 WebView2 在不同 Windows 版本下的行为差异。

### 7.2 自动导航到虚拟主机

窗口初始化时，若未显式设置 URL/HTML 且 `Application.AssetServer` 已配置，自动导航到 `http://wails.localhost/`：

```csharp
var app = Application.Get();
if (app?.AssetServer is not null)
{
    const string wailsUrl = "http://wails.localhost/";
    _webview.Navigate(wailsUrl);
    _currentUrl = wailsUrl;
}
```

### 7.3 同步处理 OnWebResourceRequested

`OnWebResourceRequested` 事件处理器**必须使用同步方法**（`GetAwaiter().GetResult()`），原因如下：

> Win32 消息循环无 `SynchronizationContext`，`await` 后的 continuation 会在线程池线程运行，而 WebView2 要求 `CoreWebView2` 成员只能在 UI 线程访问（否则抛出 `InvalidOperationException`）。

处理流程：

1. 通过 `args.GetDeferral()` 获取延迟对象，确保异步工作完成前不会超时
2. **IPC 端点**：`POST /wails/*` 转发到 `Application.HandleMessageFromFrontend`，返回 JSON
3. **资源端点**：调用 `AssetServer.ServeAsync(path).GetAwaiter().GetResult()` 同步读取
4. **SPA 回退**：资源不存在且路径无扩展名时，回退到 `index.html`（`text/html`）
5. 通过 `Environment.CreateWebResourceResponse` 构造响应，**MIME 必须用规范化后的 `assetPath`**（不能用原始 `/`）
6. `finally` 块调用 `deferral.Complete()`

### 7.4 AssetServerTransport 适配器

[AssetServerTransport.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/AssetServerTransport.cs) 是将 `AssetServer` 绑定到通用传输层（`HttpTransport` / `WebSocketTransport`）的适配器，实现 `IAssetServerTransport` 接口：

```csharp
public async Task ServeAsync(HttpListenerContext context, CancellationToken ct = default)
{
    if (_assetServer is null) { context.Response.StatusCode = 404; context.Response.Close(); return; }
    await _assetServer.ServeHttpAsync(context, ct);
}
```

通过 `AssetServerTransport.BindToTransport(transport, assetServer)` 静态方法便捷注入。此适配器主要用于 Server 模式（无 GUI 的容器化部署），桌面模式下 Windows/Linux 窗口直接调用 `AssetServer.ServeAsync`。

---

## 8. Linux 平台集成

Linux 平台通过 WebKitGTK 的 `WebContext.RegisterUriScheme` 注册自定义 URI scheme，实现位于 [LinuxWebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxWebviewWindow.cs)。

### 8.1 wails:// 协议注册

```csharp
private static bool _wailsSchemeRegistered;

private void EnsureWailsSchemeRegistered()
{
    if (_wailsSchemeRegistered || _webView is null) return;
    _wailsSchemeRegistered = true;
    var context = _webView.GetContext();
    context.RegisterUriScheme("wails", OnWailsSchemeRequest);
}
```

使用**静态标志**确保全局只注册一次（所有窗口共享同一 `WebContext`），避免重复注册导致 WebKitGTK 报错。

### 8.2 OnWailsSchemeRequest 处理

```csharp
private void OnWailsSchemeRequest(URISchemeRequest request)
{
    var path = request.GetPath().TrimStart('/');
    if (string.IsNullOrEmpty(path)) path = "index.html";

    var assetServer = WailsApplication.Get()?.AssetServer;
    var content = assetServer.ServeAsync(path).GetAwaiter().GetResult();
    // ...
    var mimeType = assetServer.GetMimeType(path);
    FinishResponse(request, content, mimeType, 200, "OK");
}
```

同样使用 `GetAwaiter().GetResult()` 同步调用（WebKitGTK 信号回调不支持 `async`）。支持 SPA 路由回退：当资源不存在且路径无扩展名时，回退到 `index.html`。

### 8.3 URISchemeResponse 构造

```csharp
private static void FinishResponse(URISchemeRequest request, byte[] content,
    string contentType, uint statusCode, string reasonPhrase)
{
    var bytes = GLib.Bytes.New(content);
    var stream = MemoryInputStream.NewFromBytes(bytes);
    var response = URISchemeResponse.New(stream, content.Length);
    response.SetContentType(contentType);
    response.SetStatus(statusCode, reasonPhrase);
    request.FinishWithResponse(response);
}
```

通过 `MemoryInputStream` 包装字节数组，使用 `URISchemeResponse.SetStatus` 设置状态码与原因短语。**故意避免构造 `GLib.Error`**——GirCore 0.8.0 未公开便利构造器，使用 `URISchemeResponse` 是更稳定的方案。404/500 错误响应通过 `FinishWithStatus` 使用空响应体完成。

---

## 9. 配置

### 9.1 DesktopHostOptions.Assets 节

[DesktopHostOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostOptions.cs) 中的 `AssetsOptions` 描述静态资源配置：

```csharp
public class AssetsOptions
{
    public string RootPath { get; set; } = string.Empty;
    public string DefaultDocument { get; set; } = "index.html";
    public bool EnableSpaFallback { get; set; } = true;
}
```

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `RootPath` | `""` | 静态资源根路径；为空时不启用 AssetServer。支持相对路径（相对于 `AppContext.BaseDirectory`）和绝对路径 |
| `DefaultDocument` | `"index.html"` | 默认文档名，请求根路径时自动追加 |
| `EnableSpaFallback` | `true` | 启用 SPA 路由回退；请求资源不存在且无扩展名时回退到 `DefaultDocument` |

`appsettings.json` 配置示例：

```json
{
  "Desktop": {
    "Assets": {
      "RootPath": "dist",
      "DefaultDocument": "index.html",
      "EnableSpaFallback": true
    }
  }
}
```

### 9.2 DesktopApplicationBuilder 自动创建

[DesktopApplicationBuilder.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs) 在 `Build()` 阶段自动装配 `FileAssetServer`：

```csharp
if (!string.IsNullOrWhiteSpace(desktopOpts.Assets.RootPath))
{
    var rootPath = Path.IsPathRooted(desktopOpts.Assets.RootPath)
        ? desktopOpts.Assets.RootPath
        : Path.Combine(AppContext.BaseDirectory, desktopOpts.Assets.RootPath);

    if (Directory.Exists(rootPath))
    {
        var fileAssetServer = new FileAssetServer(rootPath);
        application.AssetServer = fileAssetServer;
    }
}
```

关键点：
- 仅当 `RootPath` 非空且目录真实存在时才创建，避免运行时 `FileNotFoundException`
- 相对路径基于 `AppContext.BaseDirectory`（程序集所在目录）解析，与 .NET 部署惯例一致
- 创建后赋值到 `Application.AssetServer`，所有窗口共享同一实例
- 若需使用 `BundledAssetServer`，应用代码可在 `Build()` 之后手动覆盖 `application.AssetServer`

### 9.3 端到端流程

```
appsettings.json (Desktop.Assets.RootPath)
        ↓
DesktopApplicationBuilder.Build()
        ↓ 创建 FileAssetServer(rootPath)
Application.AssetServer = fileAssetServer
        ↓
窗口初始化（无 URL/HTML）
        ↓ 检测 AssetServer != null
Navigate("http://wails.localhost/")  // Windows
Navigate("wails://localhost/")      // Linux
        ↓
WebResourceRequested / OnWailsSchemeRequest
        ↓ assetServer.ServeAsync(path)
MiddlewareChain.ExecuteAsync
        ↓
FileAssetServer.ReadAssetCore → File.ReadAllBytes
        ↓
CreateWebResourceResponse / URISchemeResponse
        ↓
WebView 渲染
```

---

## 10. 设计要点总结

1. **协议抽象**：Windows 用 `http://wails.localhost/*`，Linux 用 `wails://localhost/`，但都委托到同一 `AssetServer.ServeAsync`，平台差异最小化
2. **同步调用**：两个平台的 Webview 拦截器都必须用 `GetAwaiter().GetResult()` 同步调用，避免 SynchronizationContext 缺失导致的线程亲和性问题
3. **安全防护**：`FileAssetServer.ResolveFullPath` 防止路径穿越；CSP 头通过 `SetCspHeader` 注入
4. **缓存优化**：ETag（SHA-256 前 16 字符）+ `If-None-Match` 实现 304 协商缓存；`BundledAssetServer` 缓存程序集资源名列表
5. **大文件支持**：Range 请求支持 206 Partial Content，分块（81920 字节）写入响应流
6. **扩展点**：双中间件接口（`IMiddleware` 路径型 / `IHttpMiddleware` HTTP 型）覆盖纯内容转换与完整 HTTP 处理两类场景
7. **资源来源解耦**：`ReadAssetCore` 是虚方法，派生类自由选择文件系统或嵌入式资源，未来可扩展到数据库、网络等来源
