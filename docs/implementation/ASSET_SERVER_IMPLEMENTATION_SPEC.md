# AssetServer Implementation Specification

> **本文件是 Wails.Net AssetServer 的唯一实现规范（Single Source of Truth）。**
> 所有 AI 智能体（包括 GLM-5.2）在修改、扩展或重写 AssetServer 时，**必须**严格遵循本规范。
> 本规范描述的是**当前已实现的代码状态**，而非理想化设计。

---

## 0. Document Rules（文档规则）

### 0.1 规范优先级

本文件是 AssetServer 模块的**唯一实现依据**。

```
AGENTS.md（项目级规范）
    ↓ 服从
本规范（ASSET_SERVER_IMPLEMENTATION_SPEC.md）
    ↓ 服从
现有代码（src/Wails.Net.AssetServer/）
```

### 0.2 AI 智能体强制规则

- **禁止 AI 自由发挥**：所有命名、结构、API 必须严格遵循本文件。
- **所有公共 API 必须保持兼容**：不得删除已有接口、不得修改方法签名（除非本规范明确要求）。
- **不得修改对象关系**：`AssetServer` → `FileAssetServer` / `BundledAssetServer` 的继承关系不可变更。
- **不得自行新增概念**：如需新增 Provider、Middleware、Result 类型，必须先更新本规范。
- **优先使用 Microsoft.Extensions.\***：DI、Logging、Options、Configuration 全栈使用，禁止自行重新设计。
- **禁止重新设计 Hosting**：使用 Generic Host + `DesktopApplicationBuilder`，不得另起炉灶。
- **禁止重新设计 DI**：使用 `Microsoft.Extensions.DependencyInjection`，禁止自研容器。
- **禁止重新设计 Logging**：使用 `Microsoft.Extensions.Logging`，禁止自研日志抽象。

### 0.3 现有实现状态

| 组件 | 状态 | 说明 |
|------|------|------|
| `AssetServer` 基类 | ✅ 已实现 | HTTP 处理、ETag、Range、MIME、404/500 |
| `FileAssetServer` | ✅ 已实现 | 文件系统读取、路径穿越防护、Last-Modified |
| `BundledAssetServer` | ✅ 已实现 | 嵌入式资源、多程序集、通配符匹配 |
| `MiddlewareChain` | ✅ 已实现 | 双中间件接口（路径型 + HTTP 型） |
| `NonceInjector` | ✅ 已实现 | CSP nonce 生成与注入 |
| `IsolationInjector` | ✅ 已实现 | Isolation iframe 注入 |
| `AssetOptions` | ✅ 已实现 | 配置选项（含 Security、MIME、SPA 回退） |
| `IAssetProvider` 抽象 | ✅ 已实现（M7） | 组合模式接口，`FileAssetProvider`/`BundledAssetProvider` |
| Trie 路由 | ⏳ 未实现 | 当前使用路径直接匹配 |
| `IAssetResult` 抽象 | ✅ 已实现（M8） | Result 模式，`BytesResult`/`StatusResult`/`ErrorResult` |
| `ILogger<AssetServer>` 集成 | ✅ 已实现（M12） | 可选日志器注入，结构化日志事件分类 |
| Memory/Plugin/Generated Provider | ⏳ 未实现 | 仅有 File 和 Bundled 两种 |

> **重要**：标记为 ⏳ 的项目是**未来扩展点**，AI 智能体不得擅自实现，必须先更新本规范并经人类确认。

---

## 1. Goals（目标）

### 1.1 核心目标

实现 Wails.Net Runtime 的资源请求系统，为前端 Webview 提供 HTTP 静态资源服务。

### 1.2 必须支持的能力

| 能力 | 说明 | 实现位置 |
|------|------|----------|
| **Embedded** | 从程序集嵌入资源读取 | `BundledAssetServer` |
| **Physical** | 从文件系统读取 | `FileAssetServer` |
| **Static** | 静态文件服务 | `AssetServer.ServeAsync` |
| **Range** | 分段下载（206 Partial Content） | `AssetServer.ServeAssetCoreAsync` |
| **ETag** | 协商缓存（304 Not Modified） | `AssetServer.ComputeETag` |
| **Last-Modified** | 协商缓存（If-Modified-Since） | `FileAssetServer.GetLastModified` |
| **CSP** | 内容安全策略头注入 | `AssetServer.SetCspHeader` |
| **Nonce** | CSP nonce 防注入 | `NonceInjector` |
| **Isolation** | iframe 隔离模式 | `IsolationInjector` |
| **SPA Fallback** | 单页应用路由回退 | `AssetServer.TryReadAssetWithSpaFallbackAsync` |
| **Middleware** | 双中间件链（路径型 + HTTP 型） | `MiddlewareChain` |
| **CORS** | 跨域支持 | `AssetServer.ServeHttpAsync` |

### 1.3 虚拟主机协议

| 平台 | 协议 | 拦截方式 |
|------|------|----------|
| Windows | `http://wails.localhost/` | WebView2 `AddWebResourceRequestedFilter` |
| Linux | `wails://localhost/` | WebKitGTK `RegisterUriScheme` |
| Android | `https://wails.localhost/` | Android WebView `ShouldInterceptRequest` |

### 1.4 对应关系

本模块对应 Wails v3 Go 版本：

```
internal/assetserver/
├── assetserver.go      → AssetServer.cs
├── options.go          → AssetOptions.cs
├── fileassets/         → FileAssetServer.cs
├── bundledassets/      → BundledAssetServer.cs
└── common.go           → AssetServer.Headers
```

安全部分对应 Tauri v2：

```
tauri/runtime/security/
├── csp.rs              → NonceInjector.cs
└── isolation.rs        → IsolationInjector.cs
```

---

## 2. Constraints（约束）

### 2.1 框架约束

| 约束项 | 值 | 说明 |
|--------|-----|------|
| Framework | `net10.0` | 仅支持 .NET 10，不得降级 |
| Language | C# 13+ | `LangVersion=latest` |
| Nullable | `enable` | 可空引用类型必须启用 |
| ImplicitUsings | `enable` | 隐式 using 启用 |
| TreatWarningsAsErrors | `true` | **警告视为错误，零警告** |

### 2.2 AOT 与 Trim 约束

| 约束 | 要求 |
|------|------|
| NativeAOT | 应兼容（当前未强制启用） |
| Trim | 应兼容 |
| Reflection | **最小化使用**，`BundledAssetServer` 中的 `Assembly.GetManifestResourceStream` 是允许的反射 |
| Dynamic | 禁止 `Emit`、`DynamicMethod` |
| AppDomain | 禁止使用 |

### 2.3 依赖约束

| 允许 | 禁止 |
|------|------|
| `System.Net`（`HttpListener`） | `Microsoft.AspNetCore.*`（不引入 ASP.NET Core 管道） |
| `System.Security.Cryptography` | 第三方 HTTP 服务器库 |
| `Microsoft.Extensions.*`（DI/Logging/Options） | `PInvoke.*` |
| `System.Text.RegularExpressions` | Python 互操作 |

### 2.4 平台约束

- AssetServer 核心库（`Wails.Net.AssetServer`）**必须跨平台**，不得引用 Windows/Linux 专属 API。
- 平台特定的拦截逻辑位于各自平台项目（`Wails.Net.Application.Windows` / `.Linux` / `.Android`）。
- AssetServer 仅通过 `HttpListenerContext` 和 `byte[]` 与外界交互，不耦合任何 Webview SDK。

---

## 3. Coding Style（编码风格）

### 3.1 命名规范

| 类型 | 规范 | 示例 |
|------|------|------|
| 类名 | PascalCase | `AssetServer`, `FileAssetServer` |
| 接口 | I 前缀 | `IMiddleware`, `IHttpMiddleware` |
| 方法名 | PascalCase | `ServeAsync`, `ReadAssetCore` |
| 私有字段 | _camelCase | `_options`, `_middlewareChain` |
| 局部变量 | camelCase | `mimeType`, `rangeHeader` |
| 常量 | PascalCase | `ContentType`, `Range` |
| 命名空间 | PascalCase | `Wails.Net.AssetServer.Security` |

### 3.2 命名空间冲突避免

**禁止**类名与所在命名空间同名（CS0118 错误）：

```csharp
// ❌ 错误
namespace Wails.Net.AssetServer;
public class AssetServer { ... }  // 可能冲突

// ✅ 当前实现：类名 AssetServer 与命名空间 Wails.Net.AssetServer 不完全相同，可接受
```

### 3.3 异步策略

- 所有公共 IO 方法必须返回 `Task` 或 `Task<T>`。
- 异步方法名以 `Async` 后缀结尾（如 `ServeAsync`、`ServeHttpAsync`）。
- **平台拦截器例外**：`Win32WebviewWindow.OnWebResourceRequested` 和 `LinuxWebviewWindow.OnWailsSchemeRequest` 必须使用同步调用 `.GetAwaiter().GetResult()`，因为：
  - Win32 消息循环无 `SynchronizationContext`，`await` continuation 会跳到线程池
  - WebKitGTK 信号回调不支持 `async`
- `CancellationToken` 必须透传到所有异步 IO 操作。

### 3.4 ConfigureAwait 策略

AssetServer 库内部**不使用** `ConfigureAwait(false)`，原因：

- AssetServer 不在 ASP.NET Core 同步上下文中运行
- 调用方（平台拦截器）使用 `.GetAwaiter().GetResult()` 同步等待，ConfigureAwait 无意义
- 保持代码简洁

### 3.5 Dispose 策略

- `AssetServer` 无需实现 `IDisposable`（无持有非托管资源）
- `Stream` 必须使用 `using` 或 `using var` 确保释放
- `HttpListenerContext` 由调用方管理生命周期

### 3.6 异常策略

- 参数校验使用 `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrEmpty`
- 反射调用需解包 `TargetInvocationException`（当前 AssetServer 不涉及反射调用，但 `BundledAssetServer` 涉及程序集读取）
- `HttpListenerException`（客户端断开）**静默吞掉**，不传播
- 其他异常通过 `ServeError` 返回 500
- **禁止**抛出 `Exception` 基类，必须使用具体异常类型

### 3.7 文档注释

- **所有公共 API 必须有 XML 文档注释**（使用 `///`）
- 注释语言：**中文**
- 必须描述对应 Wails v3 Go 版本的源文件

```csharp
/// <summary>
/// 资源服务器，处理 HTTP 请求并提供静态资源服务。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/assetserver.go</c> 中的 AssetServer 结构。
/// </summary>
```

### 3.8 线程安全

- 静态共享状态必须使用 `Interlocked` 或 `lock`
- 并发集合优先使用 `ConcurrentDictionary`
- `MiddlewareChain` 的 `_middlewares` / `_httpMiddlewares` 列表在注册阶段写入、请求阶段只读，无需加锁
- `BundledAssetServer._resourceNamesCache` 使用 `Dictionary`，**非线程安全**，多线程并发读取需调用方保证（当前由平台拦截器的单线程模型保证）

---

## 4. Project Layout（项目结构）

### 4.1 目录结构

```
src/Wails.Net.AssetServer/
├── Wails.Net.AssetServer.csproj
├── AssetServer.cs              # 基类：HTTP 处理、ETag、Range、MIME
├── AssetOptions.cs             # 配置选项
├── FileAssetServer.cs          # 文件系统 Provider
├── BundledAssetServer.cs       # 嵌入式资源 Provider
├── Middleware/
│   ├── IMiddleware.cs          # IMiddleware + IHttpMiddleware 接口
│   └── MiddlewareChain.cs      # 中间件链执行器
└── Security/
    ├── SecurityOptions.cs      # 安全选项聚合
    ├── NonceOptions.cs         # CSP nonce 配置
    ├── NonceInjector.cs        # nonce 生成与注入
    ├── IsolationOptions.cs     # Isolation 配置
    └── IsolationInjector.cs    # iframe 隔离注入
```

### 4.2 目录职责

| 目录 | 职责 | 禁止 |
|------|------|------|
| 根目录 | 核心类（AssetServer、Options、Provider） | 放置中间件实现 |
| `Middleware/` | 中间件接口与链执行器 | 放置具体业务中间件实现 |
| `Security/` | 安全相关（CSP、Nonce、Isolation） | 放置非安全逻辑 |

### 4.3 测试目录

```
tests/Wails.Net.AssetServer.Tests/
├── Wails.Net.AssetServer.Tests.csproj
├── AssetServerTests.cs           # 基类测试
├── AssetOptionsTests.cs          # 选项测试
├── FileAssetServerTests.cs       # 文件 Provider 测试
├── BundledAssetServerTests.cs    # 嵌入资源 Provider 测试
├── MiddlewareChainTests.cs       # 中间件链测试
├── NonceInjectorTests.cs         # nonce 注入测试
└── IsolationInjectorTests.cs     # isolation 注入测试
```

### 4.4 项目依赖关系

```
Wails.Net.AssetServer（核心库，无平台依赖）
    ↑
Wails.Net.Application（引用 AssetServer，提供 AssetServerTransport 适配器）
    ↑
Wails.Net.Application.Windows / .Linux / .Android（平台拦截器）
```

**关键约束**：`Wails.Net.AssetServer` 不得反向引用 `Wails.Net.Application`，保持核心库独立可测试。

---

## 5. Public API（公共 API）

### 5.1 AssetServer 类

```csharp
public class AssetServer
{
    public AssetOptions Options { get; }

    public AssetServer(AssetOptions options);
    public AssetServer(AssetOptions options, IAssetProvider provider);  // M7 新增
    public AssetServer(AssetOptions options, ILogger<AssetServer> logger);  // M12 新增
    public AssetServer(AssetOptions options, IAssetProvider provider, ILogger<AssetServer> logger);  // M12 新增

    // 中间件注册
    public void Use(IMiddleware middleware);
    public void Use(IHttpMiddleware middleware);

    // 自定义资源读取器（优先级低于 IAssetProvider）
    public void SetAssetReader(Func<string, Stream?> reader);

    // CSP 头设置
    public void SetCspHeader(string? cspHeader);

    // 日志器设置（M12 新增，用于构造后注入）
    public void SetLogger(ILogger<AssetServer> logger);

    // 核心服务方法
    public virtual Task<byte[]> ServeAsync(string path);
    public virtual Task ServeHttpAsync(HttpListenerContext context, CancellationToken cancellationToken = default);

    // MIME 解析
    public string GetMimeType(string path);

    // 最后修改时间（委托给 IAssetProvider，派生类可重写）
    public virtual DateTime? GetLastModified(string path);

    // Headers 常量
    public static class Headers { ... }
}
```

### 5.1.1 IAssetProvider 接口（M7 新增）

```csharp
public interface IAssetProvider
{
    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default);
    DateTime? GetLastModified(string path);
}
```

### 5.1.2 内置 Provider（M7 新增）

```csharp
public class FileAssetProvider : IAssetProvider
{
    public string RootPath { get; }
    public FileAssetProvider(string rootPath);
}

public class BundledAssetProvider : IAssetProvider
{
    public IReadOnlyList<Assembly> Assemblies { get; }
    public BundledAssetProvider(Assembly assembly);
    public BundledAssetProvider(IEnumerable<Assembly> assemblies);
    public void AddAssembly(Assembly assembly);
}
```

### 5.2 FileAssetServer 类

```csharp
public class FileAssetServer : AssetServer
{
    public string RootPath { get; }

    public FileAssetServer(string rootPath);
    public FileAssetServer(string rootPath, bool enableSpaFallback, string defaultDocument);

    public byte[]? ReadFile(string path);
}
```

### 5.3 BundledAssetServer 类

```csharp
public class BundledAssetServer : AssetServer
{
    public IReadOnlyList<Assembly> Assemblies { get; }

    public BundledAssetServer(Assembly assembly);
    public BundledAssetServer(IEnumerable<Assembly> assemblies);

    public void AddAssembly(Assembly assembly);
    public byte[]? ReadResource(string path);
}
```

### 5.4 中间件接口

```csharp
public interface IMiddleware
{
    Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next);
}

public interface IHttpMiddleware
{
    Task<bool> ProcessAsync(HttpListenerContext context, Func<Task> next);
}
```

### 5.5 配置入口（位于 Wails.Net.Application）

```csharp
// appsettings.json
{
  "Desktop": {
    "Assets": {
      "RootPath": "dist",
      "DefaultDocument": "index.html",
      "EnableSpaFallback": true
    }
  }
}

// DesktopApplicationBuilder.Build() 自动装配 FileAssetServer
// 应用代码可手动覆盖：application.AssetServer = new BundledAssetServer(Assembly.GetExecutingAssembly());
```

### 5.6 API 兼容性规则

- **禁止删除**已有公共方法、属性
- **禁止修改**已有公共方法签名（参数类型、顺序、返回类型）
- **禁止重命名**已有公共类型
- **新增**公共方法必须提供默认实现，不得破坏现有调用方
- ** obsolete 标记**需保留至少一个版本周期才可删除

---

## 6. Object Model（对象模型）

### 6.1 核心对象关系

```
AssetOptions（配置）
    ↓ 注入
AssetServer（基类）
    ├── 持有 MiddlewareChain
    ├── 持有 SecurityOptions（通过 AssetOptions.Security）
    ├── 持有 _cspHeader（通过 SetCspHeader 注入）
    ├── 持有 _customAssetReader（通过 SetAssetReader 注入）
    ↓ 继承
FileAssetServer                  BundledAssetServer
    ├── _rootPath                    ├── _assemblies: List<Assembly>
    └── ResolveFullPath              ├── _resourceNamesCache
                                     └── NormalizeResourceName
```

### 6.2 AssetOptions 完整属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Handler` | `string` | `""` | 处理器名（`"file"` / `"bundled"`） |
| `RootPath` | `string` | `""` | 资源根路径 |
| `Middleware` | `Dictionary<string, string>` | `new()` | 中间件配置字典 |
| `ErrorHandler` | `Action<HttpListenerContext, Exception>?` | `null` | 自定义错误处理 |
| `HandlerTimeout` | `TimeSpan` | `30s` | 请求超时 |
| `EnableSpaFallback` | `bool` | `false` | SPA 回退开关 |
| `DefaultDocument` | `string` | `"index.html"` | 默认文档 |
| `CustomMimeTypes` | `Dictionary<string, string>` | `new()` | 自定义 MIME 映射 |
| `MimeTypeResolver` | `Func<string, string?>?` | `null` | 自定义 MIME 解析器 |
| `Security` | `SecurityOptions` | `new()` | 安全选项 |

### 6.3 SecurityOptions 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Nonce` | `NonceOptions` | `new()` | CSP nonce 配置 |
| `Isolation` | `IsolationOptions` | `new()` | Isolation 配置 |

### 6.4 NonceOptions 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableNonce` | `bool` | `false` | 启用 nonce 注入 |
| `CspPolicy` | `string` | `"default-src 'self'; script-src 'self'; style-src 'self'"` | 基础 CSP 策略 |
| `InjectIntoHtml` | `bool` | `true` | 是否注入到 HTML 标签 |

### 6.5 IsolationOptions 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `false` | 启用 Isolation |
| `IsolationDir` | `string` | `"isolation"` | 资源目录名 |
| `IsolationSrc` | `string` | `"/isolation/index.html"` | iframe URL |
| `Sandbox` | `string` | `"allow-scripts"` | sandbox 属性 |
| `FrameName` | `string` | `"isolation-frame"` | iframe name |

### 6.6 对象不变量（Invariants）

- `AssetServer._options` 构造后不可为 `null`
- `AssetServer._middlewareChain` 构造后不可为 `null`
- `FileAssetServer._rootPath` 构造后不可为 `null` 或空
- `BundledAssetServer._assemblies` 构造后至少包含一个程序集
- `SecurityOptions.Nonce` 和 `.Isolation` 永不为 `null`（构造时初始化）

---

## 7. Hosting（托管）

### 7.1 托管模型

AssetServer **不自行启动 HTTP 服务器**，而是由各平台 Webview 拦截器调用：

```
GenericHost
    ↓
DesktopApplicationBuilder.Build()
    ↓ 创建 Application
    ↓ 检测 DesktopHostOptions.Assets.RootPath
    ↓ 创建 FileAssetServer(rootPath)
Application.AssetServer = fileAssetServer
    ↓
平台 Webview 窗口初始化
    ↓ 检测 Application.AssetServer != null
    ↓ Navigate("http://wails.localhost/")  // Windows
    ↓ WebResourceRequested 事件
    ↓ assetServer.ServeAsync(path).GetAwaiter().GetResult()
```

### 7.2 Server 模式（无 GUI）

对于容器化部署，`AssetServerTransport` 适配器将 AssetServer 绑定到 `HttpListener`：

```csharp
public class AssetServerTransport : IAssetServerTransport
{
    public async Task ServeAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        if (_assetServer is null)
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }
        await _assetServer.ServeHttpAsync(context, ct);
    }
}
```

通过 `AssetServerTransport.BindToTransport(transport, assetServer)` 静态方法注入。

### 7.3 生命周期

- AssetServer 由 `Application` 持有，生命周期与 `Application` 相同
- `Application.AssetServer` 是 `public` 可读写属性，允许应用代码在 `Build()` 后替换
- AssetServer **不实现** `IHostedService`，因为它由 Webview 驱动而非 Host 驱动

### 7.4 禁止行为

- ❌ 在 AssetServer 内部启动 `HttpListener`
- ❌ 实现 `IHostedService` 或 `IAsyncDisposable`
- ❌ 自行管理线程或后台任务
- ❌ 引用 `Microsoft.AspNetCore.*` 或 `Microsoft.Extensions.Hosting`（除 `Application` 层外）

---

## 8. Pipeline（管道）

### 8.1 ServeHttpAsync 处理流程

```
HttpListenerContext 进入
    ↓
[1] 写入 CORS 头：Access-Control-Allow-Origin: *
    ↓
[2] CSP 头处理
    ├── 若 Security.Nonce.EnableNonce == true
    │   ├── 生成 per-request nonce
    │   ├── 构建带 nonce 的 CSP 头
    │   └── 写入 Content-Security-Policy
    └── 否则若 _cspHeader 非空
        └── 写入 Content-Security-Policy
    ↓
[3] OPTIONS 预检 → 返回 204 No Content
    ↓
[4] 路径解析：request.Url.AbsolutePath，去除查询参数
    ↓
[5] HTTP 中间件链执行（ExecuteHttpAsync）
    ├── 任一中间件返回 true → 短路，结束
    └── 全部返回 false → 进入核心处理
    ↓
[6] ServeAssetCoreAsync 核心处理
    ├── TryReadAssetWithSpaFallbackAsync 读取资源
    │   ├── 中间件链 ExecuteAsync
    │   ├── ReadAssetCore（派生类实现）
    │   └── SPA 回退到 DefaultDocument
    ├── 资源为空 → ServeNotFound (404)
    ├── MIME 推导
    ├── HTML 安全注入（nonce + isolation）
    ├── ETag 计算 + 写入
    ├── Last-Modified 写入（若派生类提供）
    ├── Cache-Control: no-cache
    ├── If-None-Match 匹配 → 304
    ├── If-Modified-Since 匹配 → 304
    ├── Range 请求
    │   ├── 解析成功 → 206 Partial Content
    │   └── 解析失败 → 416 Range Not Satisfiable
    └── 完整响应 → 200 + Content-Length
    ↓
[7] 异常处理
    ├── HttpListenerException → 静默吞掉
    └── 其他异常 → ServeError (500)
```

### 8.2 ServeAsync 简单路径

```
string path 进入
    ↓
TryReadAssetWithSpaFallbackAsync
    ├── MiddlewareChain.ExecuteAsync（路径中间件链）
    │   └── ReadAssetCore（派生类实现）
    └── SPA 回退
    ↓
返回 byte[]（资源不存在返回空数组 []）
```

### 8.3 中间件链顺序

**注册顺序 = 执行顺序**（先注册先执行）。

```
注册：Use(M1) → Use(M2) → Use(M3)
执行：M1 → M2 → M3 → finalHandler
```

通过倒序构建委托链实现：

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

### 8.4 短路语义

**HTTP 中间件**（`IHttpMiddleware`）支持短路：

- 返回 `true` → 已完全处理，后续中间件和 `finalHandler` 不执行
- 返回 `false` → 交由链继续

**路径中间件**（`IMiddleware`）不支持短路：

- 返回非 `null` → 链终止，该结果作为最终内容
- 返回 `null` → 继续到下一节点

---

## 9. Middleware（中间件）

### 9.1 双中间件接口

| 接口 | 输入 | 输出 | 适用场景 |
|------|------|------|----------|
| `IMiddleware` | `string path` | `Task<byte[]?>` | 纯内容转换（压缩、加密、内容注入） |
| `IHttpMiddleware` | `HttpListenerContext` | `Task<bool>` | 完整 HTTP 处理（读写头、状态码、Range） |

### 9.2 IMiddleware 接口

```csharp
public interface IMiddleware
{
    Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next);
}
```

**语义**：
- `path`：请求的资源路径
- `next`：链中下一个处理器
- 返回值：处理后的内容；`null` 表示未处理，交由下一节点

### 9.3 IHttpMiddleware 接口

```csharp
public interface IHttpMiddleware
{
    Task<bool> ProcessAsync(HttpListenerContext context, Func<Task> next);
}
```

**语义**：
- `context`：完整 HTTP 上下文
- `next`：链中下一个处理器
- 返回值：`true` 已完全处理（短路）；`false` 继续链

### 9.4 MiddlewareChain 执行器

```csharp
public class MiddlewareChain
{
    public void Use(IMiddleware middleware);
    public void Use(IHttpMiddleware middleware);

    public Task<byte[]?> ExecuteAsync(string path, Func<string, Task<byte[]?>> finalHandler);
    public Task<bool> ExecuteHttpAsync(HttpListenerContext context, Func<Task> finalHandler);

    public int Count { get; }
    public int HttpMiddlewareCount { get; }
}
```

### 9.5 中间件实现规范

未来新增中间件必须遵循：

1. 实现 `IMiddleware` 或 `IHttpMiddleware` 接口
2. 类名以 `Middleware` 或 `Injector` 结尾
3. 放置在 `Middleware/` 或 `Security/` 目录
4. 必须包含完整 XML 文档注释
5. 必须有对应单元测试

### 9.6 内置安全中间件

| 类 | 接口 | 说明 |
|----|------|------|
| `NonceInjector` | 静态工具类（非中间件） | 生成 nonce、注入 HTML、构建 CSP 头 |
| `IsolationInjector` | 静态工具类（非中间件） | 注入 isolation iframe |

> **注意**：当前 `NonceInjector` 和 `IsolationInjector` 不是独立中间件，而是由 `AssetServer.ServeAssetCoreAsync` 在 HTML 处理阶段直接调用。未来若需解耦，可包装为 `IHttpMiddleware` 实现。

---

## 10. Routing（路由）

### 10.1 当前路由模型

AssetServer **不实现复杂路由**，使用**路径直接匹配**：

```
请求路径 /assets/index.html
    ↓
TryReadAssetWithSpaFallbackAsync("/assets/index.html")
    ↓
ReadAssetCore("/assets/index.html")
    ├── FileAssetServer：Path.Combine(rootPath, "assets/index.html")
    └── BundledAssetServer：NormalizeResourceName → "assets.index.html"
    ↓
命中 → 返回内容
未命中 + EnableSpaFallback → 回退到 DefaultDocument
```

### 10.2 SPA 回退规则

```
若 ReadAssetCore(path) 返回 null 或空数组：
    若 EnableSpaFallback == true && DefaultDocument 非空：
        尝试 ReadAssetCore(DefaultDocument)
        若命中 → 返回 (content, DefaultDocument)
    否则 → 返回 (null, path)
```

**关键点**：`servedPath`（实际服务的路径）用于 MIME 推导。回退到 `index.html` 时，MIME 必须按 `index.html` 推导为 `text/html`，而非原始路径。

### 10.3 IPC 端点路由

IPC 端点（`POST /wails/*`）由**平台拦截器**处理，不经过 AssetServer：

```
Windows: Win32WebviewWindow.OnWebResourceRequested
    ├── POST /wails/* → Application.HandleMessageFromFrontend → 返回 JSON
    └── 其他 → AssetServer.ServeAsync(path)
```

### 10.4 路由扩展约束

- ❌ **禁止**在 AssetServer 内部实现 Trie/Template 路由
- ❌ **禁止**引入 ASP.NET Core 路由系统
- 路由复杂性由上层（平台拦截器、Application）处理
- AssetServer 仅负责"给定路径 → 返回内容"

---

## 11. Providers（资源提供者）

### 11.1 Provider 架构（M7 已实现）

AssetServer 同时支持**组合模式**（通过 `IAssetProvider` 接口）和**继承模式**（通过重写 `ReadAssetCore`），组合模式优先：

```
AssetServer（基类）
    ├── _provider: IAssetProvider?（组合模式，优先）
    ├── _customAssetReader（轻量级扩展）
    ├── ReadAssetCore virtual
    │   ├── 优先委托给 _provider（若已设置）
    │   ├── 其次调用 _customAssetReader
    │   └── 最后返回 null
    ↓ 继承（向后兼容）
FileAssetServer                  BundledAssetServer
    └── 内部持有 FileAssetProvider    └── 内部持有 BundledAssetProvider
        作为 _provider                    作为 _provider

IAssetProvider（接口）
    ├── ReadAsync(path) → byte[]?
    └── GetLastModified(path) → DateTime?

FileAssetProvider : IAssetProvider    BundledAssetProvider : IAssetProvider
    └── ReadAsync → File.ReadAllBytes     └── ReadAsync → Assembly.GetManifestResourceStream
```

### 11.2 IAssetProvider 接口

```csharp
public interface IAssetProvider
{
    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default);
    DateTime? GetLastModified(string path);
}
```

**语义**：
- `ReadAsync`：异步读取资源，返回 `byte[]?`（null 表示未找到）
- `GetLastModified`：返回资源最后修改时间（UTC），null 表示不可用
- 实现必须线程安全（至少支持并发读）

### 11.3 FileAssetProvider

**职责**：从文件系统读取资源（开发模式）。实现 `IAssetProvider`。

| 方法 | 说明 |
|------|------|
| `RootPath` | 资源根路径（只读属性） |
| `ReadAsync(path, ct)` | 读取文件，返回 `byte[]?` |
| `GetLastModified(path)` | 返回 `File.GetLastWriteTimeUtc` |
| `ResolveFullPath(path)` | 私有，路径规范化 + 路径穿越防护 |

**路径穿越防护**：

```csharp
private string? ResolveFullPath(string path)
{
    var normalizedPath = path.TrimStart('/', '\\');
    var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedPath));
    var fullRoot = Path.GetFullPath(_rootPath);

    if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
    {
        return null;  // 路径穿越，拒绝
    }
    return fullPath;
}
```

### 11.4 BundledAssetProvider

**职责**：从程序集嵌入资源读取（生产模式，单文件部署）。实现 `IAssetProvider`。

| 方法 | 说明 |
|------|------|
| `Assemblies` | 程序集列表（只读） |
| `ReadAsync(path, ct)` | 读取嵌入资源，返回 `byte[]?` |
| `GetLastModified(path)` | 返回 null（嵌入资源无修改时间） |
| `AddAssembly(assembly)` | 动态添加程序集 |
| `NormalizeResourceName(path)` | 私有，`/` → `.` |
| `FindByPattern(assembly, pattern)` | 私有，通配符匹配 |

**三级查找策略**：

```
对每个程序集依次尝试：
1. 精确匹配：assembly.GetManifestResourceStream(resourceName)
2. 通配符匹配（若 resourceName 含 * 或 ?）：FindByPattern
3. 后缀匹配：assemblyName + "." + resourceName
```

### 11.5 FileAssetServer / BundledAssetServer（向后兼容）

`FileAssetServer` 和 `BundledAssetServer` 保留继承关系以维持向后兼容，但内部委托给对应的 Provider：

- `FileAssetServer : AssetServer` — 构造时通过 `base(options, provider)` 注入 `FileAssetProvider`
- `BundledAssetServer : AssetServer` — 构造时通过 `base(options, provider)` 注入 `BundledAssetProvider`

这两个类不新增业务逻辑，仅作为 Provider 的便捷包装。**新代码应优先直接使用 `IAssetProvider` + `AssetServer(options, provider)`。**

### 11.6 AssetServer 与 Provider 的集成

```csharp
// 新构造函数重载（组合模式）
public AssetServer(AssetOptions options, IAssetProvider provider) : this(options)
{
    _provider = provider;
}

// ReadAssetCore 优先委托给 _provider
protected virtual byte[]? ReadAssetCore(string path)
{
    if (_provider is not null)
    {
        return _provider.ReadAsync(path).GetAwaiter().GetResult();
    }
    // 回退到 _customAssetReader（轻量级扩展）
    if (_customAssetReader is not null) { ... }
    return null;
}

// GetLastModified 委托给 _provider
public virtual DateTime? GetLastModified(string path)
{
    return _provider?.GetLastModified(path);
}
```

### 11.7 未来 Provider 扩展点

**当前未实现**以下 Provider，AI 智能体不得擅自实现：

| Provider | 说明 | 状态 |
|----------|------|------|
| `MemoryProvider` | 从内存字典读取 | ⏳ 未实现 |
| `PluginProvider` | 从插件读取 | ⏳ 未实现 |
| `GeneratedProvider` | 动态生成内容 | ⏳ 未实现 |
| `CompositeProvider` | 组合多个 Provider | ⏳ 未实现 |

**扩展规则**：若未来需要新增 Provider，必须：
1. 先更新本规范第 11 章
2. 实现 `IAssetProvider` 接口
3. 经人类确认后才可实现

### 11.8 自定义读取器

`AssetServer.SetAssetReader(Func<string, Stream?>)` 提供**轻量级**扩展点（优先级低于 `IAssetProvider`）：

- 在 `ReadAssetCore` 中，`_provider` 未命中后调用
- 返回非 `null` 则使用其结果
- 返回 `null` 则返回 null

**优先级顺序**：`_provider` → `_customAssetReader` → null

---

## 12. Results（响应结果）

### 12.1 响应模型（M8 已实现）

AssetServer 使用 `IAssetResult` 抽象表示响应结果，由 `ServeAssetCoreAsync` 生成 Result 对象，再由 `WriteResultAsync` 统一写入 `HttpListenerResponse`：

```
ServeAssetCoreAsync
    ↓ 生成 IAssetResult
WriteResultAsync(result, context, ct)
    ↓ 根据 result 类型写入响应
HttpListenerResponse
```

### 12.2 IAssetResult 接口（M8 新增）

```csharp
public interface IAssetResult
{
    /// <summary>HTTP 状态码。</summary>
    int StatusCode { get; }

    /// <summary>内容类型（MIME），可为 null（如 304/404 无需 Content-Type）。</summary>
    string? ContentType { get; }

    /// <summary>
    /// 异步将结果写入 HTTP 响应。
    /// 实现者负责设置状态码、头部并写入响应体。
    /// </summary>
    Task WriteAsync(HttpListenerResponse response, CancellationToken cancellationToken = default);
}
```

**语义**：
- `StatusCode`：HTTP 状态码（200/206/304/404/416/500）
- `ContentType`：MIME 类型，可为 null
- `WriteAsync`：将结果写入响应，包括头部和响应体

### 12.3 内置 Result 类型（M8 新增）

| 类 | 说明 | 场景 |
|----|------|------|
| `BytesResult` | 字节数组内容 | 200 完整响应、206 Range 响应 |
| `StatusResult` | 纯状态码（无响应体） | 304 Not Modified、404 Not Found、416 Range Not Satisfiable |
| `ErrorResult` | 错误响应（文本体） | 500 Internal Server Error |

```csharp
public class BytesResult : IAssetResult
{
    public int StatusCode { get; }
    public string? ContentType { get; }
    public byte[] Content { get; }
    public string? ETag { get; }
    public DateTime? LastModified { get; }
    public (long Offset, long Length)? Range { get; }  // 206 响应时设置

    public BytesResult(byte[] content, string? contentType, int statusCode = 200, ...);
    public Task WriteAsync(HttpListenerResponse response, CancellationToken ct = default);
}

public class StatusResult : IAssetResult
{
    public int StatusCode { get; }
    public string? ContentType { get; }  // 始终为 null

    public StatusResult(int statusCode);
    public Task WriteAsync(HttpListenerResponse response, CancellationToken ct = default);
}

public class ErrorResult : IAssetResult
{
    public int StatusCode { get; }  // 500
    public string? ContentType { get; }  // "text/plain; charset=utf-8"
    public string Message { get; }

    public ErrorResult(string message, int statusCode = 500);
    public Task WriteAsync(HttpListenerResponse response, CancellationToken ct = default);
}
```

### 12.4 AssetServer 与 Result 的集成（M8 新增）

`AssetServer` 新增方法支持 Result 模式：

```csharp
// 生成 Result（供测试和扩展使用）
protected virtual IAssetResult CreateResult(byte[] content, string path, HttpListenerRequest request);

// 统一写入 Result
protected virtual Task WriteResultAsync(IAssetResult result, HttpListenerResponse response, CancellationToken ct);
```

`ServeAssetCoreAsync` 重构为：
1. 读取资源内容
2. 推导 MIME、计算 ETag、检查 Last-Modified
3. 检查 If-None-Match / If-Modified-Since → 返回 `StatusResult(304)`
4. 解析 Range → 返回 `BytesResult`（206）或 `StatusResult(416)`
5. 默认 → 返回 `BytesResult`（200）

### 12.5 支持的状态码

| 状态码 | 场景 | Result 类型 |
|--------|------|-------------|
| 200 OK | 完整内容响应 | `BytesResult` |
| 204 No Content | OPTIONS 预检 | `StatusResult` |
| 206 Partial Content | Range 请求成功 | `BytesResult`（带 Range） |
| 304 Not Modified | ETag / If-Modified-Since 匹配 | `StatusResult` |
| 404 Not Found | 资源不存在 | `StatusResult` |
| 416 Range Not Satisfiable | Range 解析失败 | `StatusResult` |
| 500 Internal Server Error | 异常 | `ErrorResult` |

### 12.6 响应头规范

**必须设置的头**（由 `WriteResultAsync` 统一设置）：

| 头 | 值 | 时机 |
|----|-----|------|
| `Access-Control-Allow-Origin` | `*` | 每个响应（CORS） |
| `Content-Type` | MIME 类型 | 200/206 响应 |
| `Accept-Ranges` | `bytes` | 200/206 响应 |
| `ETag` | `"<16位hex>"` | 200/206 响应 |
| `Cache-Control` | `no-cache` | 200/206 响应 |

**条件性头**：

| 头 | 条件 |
|----|------|
| `Content-Security-Policy` | `_cspHeader` 非空 或 Nonce 启用 |
| `Last-Modified` | `GetLastModified` 返回非 null |
| `Content-Range` | 206 响应 |
| `Content-Length` | 200/206 响应 |

### 12.7 ETag 计算

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

**规范**：
- 基于 SHA-256 哈希的**前 8 字节（16 个十六进制字符）**
- 用双引号包裹
- 必须与 Go 版本 `fnv` 或 `sha256` 一致（当前使用 SHA-256）

### 12.8 Range 请求规范

支持的格式：

| 格式 | 说明 | 示例 |
|------|------|------|
| `bytes=start-end` | 指定范围 | `bytes=0-1023` |
| `bytes=start-` | 从 start 到末尾 | `bytes=1024-` |
| `bytes=-suffix` | 最后 suffix 字节 | `bytes=-512` |

响应：

```
HTTP/1.1 206 Partial Content
Content-Range: bytes 0-1023/2048
Content-Length: 1024
```

分块写入（81920 字节缓冲区）：

```csharp
var buffer = new byte[Math.Min(length, 81920)];
while (remaining > 0)
{
    var toRead = (int)Math.Min(remaining, buffer.Length);
    Buffer.BlockCopy(content, (int)offset, buffer, 0, toRead);
    await response.OutputStream.WriteAsync(buffer.AsMemory(0, toRead), cancellationToken);
    offset += toRead;
    remaining -= toRead;
}
```

### 12.9 向后兼容

M8 重构保持 `ServeHttpAsync` 公共签名不变。Result 模式是**内部实现细节**，调用方（平台拦截器）无感知。`ServeAsync` 返回 `byte[]` 的签名也保持不变。

### 12.10 未来 Result 扩展

**当前未实现**以下 Result 类型，AI 智能体不得擅自实现：

| Result | 说明 | 状态 |
|--------|------|------|
| `StreamResult` | 流式响应（不加载到内存） | ⏳ 未实现 |
| `JsonResult` | JSON 序列化响应 | ⏳ 未实现 |
| `RedirectResult` | 重定向响应 | ⏳ 未实现 |
| `FileResult` | 文件流响应（带 Range 支持） | ⏳ 未实现 |

---

## 13. Static Files（静态文件）

### 13.1 FileAssetServer 静态文件服务

**开发模式**使用 `FileAssetServer`：

```csharp
// appsettings.json
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

### 13.2 DesktopApplicationBuilder 自动装配

```csharp
// 位于 Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs
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

**关键约束**：
- 仅当 `RootPath` 非空**且目录真实存在**时才创建
- 相对路径基于 `AppContext.BaseDirectory` 解析
- 创建后赋值到 `Application.AssetServer`，所有窗口共享同一实例
- 若需使用 `BundledAssetServer`，应用代码可在 `Build()` 后手动覆盖

### 13.3 Last-Modified 头

`FileAssetServer` 重写 `GetLastModified`：

```csharp
public override DateTime? GetLastModified(string path)
{
    var fullPath = ResolveFullPath(path);
    if (fullPath is null || !File.Exists(fullPath)) return null;
    return File.GetLastWriteTimeUtc(fullPath);
}
```

格式化为 RFC 1123（`"R"` 格式说明符）：

```csharp
response.Headers[Headers.LastModified] =
    lastModified.Value.ToString("R", CultureInfo.InvariantCulture);
```

### 13.4 MIME 类型查找顺序

```
1. AssetOptions.MimeTypeResolver（自定义解析器，优先级最高）
2. AssetOptions.CustomMimeTypes（自定义字典，按扩展名）
3. 内置扩展名映射（switch 表达式）
4. 默认 "application/octet-stream"
```

内置映射覆盖的扩展名：

```
.html .htm → text/html
.css → text/css
.js .mjs → application/javascript
.json → application/json
.xml → application/xml
.txt → text/plain
.svg → image/svg+xml
.png → image/png
.jpg .jpeg → image/jpeg
.gif → image/gif
.webp → image/webp
.ico → image/x-icon
.bmp → image/bmp
.woff → font/woff
.woff2 → font/woff2
.ttf → font/ttf
.otf → font/otf
.wasm → application/wasm
.pdf → application/pdf
.zip → application/zip
.map → application/json
.mp4 → video/mp4
.webm → video/webm
.mp3 → audio/mpeg
.ogg → audio/ogg
.wav → audio/wav
```

---

## 14. Dynamic Assets（动态资源）

### 14.1 当前状态

**AssetServer 不支持动态资源生成**。所有资源必须预先存在于文件系统或嵌入资源中。

### 14.2 有限的动态能力

通过 `SetAssetReader(Func<string, Stream?>)` 可实现简单的动态读取：

```csharp
assetServer.SetAssetReader(path =>
{
    if (path == "/api/config.json")
    {
        var json = GenerateConfigJson();
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
    return null;  // 回退到默认读取
});
```

### 14.3 未来扩展约束

**禁止**AI 智能体擅自实现以下动态资源能力：
- 动态路由（`MapGet`、`MapFallback`）
- 模板渲染
- 中间件生成内容
- 流式响应（`Results.Stream`）

若需要这些能力，必须先更新本规范并经人类确认。

---

## 15. Security（安全）

### 15.1 安全架构

借鉴 Tauri v2 的安全模型：

```
SecurityOptions
    ├── Nonce（CSP nonce 防注入）
    └── Isolation（iframe 隔离模式）
```

### 15.2 CSP Nonce 注入

**流程**：

```
请求进入 ServeHttpAsync
    ↓
若 Security.Nonce.EnableNonce == true：
    1. 生成 32 字节随机 nonce（base64 编码）
    2. 构建带 nonce 的 CSP 头
    3. 写入 Content-Security-Policy 头
    ↓
资源读取后，若 MIME 为 text/html 且 InjectIntoHtml == true：
    1. NonceInjector.InjectNonce(html, nonce)
    2. 注入到所有 <script> 和 <link rel="stylesheet"> 标签
```

**NonceInjector 规范**：

| 方法 | 说明 |
|------|------|
| `GenerateNonce()` | 32 字节 base64，使用 `RandomNumberGenerator` |
| `InjectNonce(html, nonce)` | 正则匹配未带 nonce 的 script/link 标签，注入 `nonce="..."` 属性 |
| `BuildCspHeader(baseCsp, nonce)` | 将 `'nonce-<value>'` 追加到 `script-src` 指令 |

**正则规范**：

```csharp
// 匹配未带 nonce 的 <script> 标签
@"<script(?![^>]*\snonce=)([^]*)>"

// 匹配未带 nonce 的 <link rel="stylesheet"> 标签
@"<link(?![^>]*\snonce=)([^]*\brel=[""']stylesheet[""'][^]*)>"
```

**约束**：
- 已带 `nonce=` 属性的标签**不重复注入**
- 正则必须 `Compiled | IgnoreCase | CultureInvariant`
- nonce 生成必须使用密码学安全的 `RandomNumberGenerator`，**禁止** `Random` 或 `Guid`

### 15.3 Isolation iframe 注入

**流程**：

```
资源读取后，若 MIME 为 text/html 且 Security.Isolation.Enabled == true：
    1. IsolationInjector.InjectIsolationIframe(html, options)
    2. 在 <body> 开标签后插入隐藏 iframe
```

**生成的 iframe**：

```html
<iframe src="/isolation/index.html" name="isolation-frame"
        sandbox="allow-scripts"
        style="display:none;width:0;height:0;border:0;"></iframe>
```

**约束**：
- 仅在找到 `<body>` 标签时注入，否则抛出 `InvalidOperationException`
- iframe 必须作为 body 的第一个子元素
- 必须使用 `display:none` 隐藏

### 15.4 CSP 头注入（静态）

非 nonce 模式下，通过 `SetCspHeader` 注入静态 CSP：

```csharp
assetServer.SetCspHeader("default-src 'self'; script-src 'self'");
```

**优先级**：nonce 模式（`Security.Nonce.EnableNonce == true`）优先于静态 `_cspHeader`。启用 nonce 时，静态 CSP 头被忽略。

### 15.5 路径穿越防护

`FileAssetServer.ResolveFullPath` 防止 `../` 攻击：

```csharp
var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedPath));
var fullRoot = Path.GetFullPath(_rootPath);

if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
{
    return null;  // 拒绝
}
```

### 15.6 安全约束

- ❌ **禁止**在 AssetServer 内部实现鉴权（Auth）
- ❌ **禁止**实现自定义权限模型
- ❌ **禁止**存储或处理敏感信息（密钥、密码）
- ✅ 路径穿越防护**必须**存在
- ✅ CSP 头**必须**可配置
- ✅ nonce 生成**必须**使用密码学安全 RNG

---

## 16. Cache（缓存）

### 16.1 缓存策略

AssetServer 实现**协商缓存**，不实现强缓存。

### 16.2 ETag 协商缓存

```
请求头：If-None-Match: "<etag>"
    ↓
若与资源当前 ETag 匹配：
    返回 304 Not Modified（无响应体）
否则：
    返回 200 + 新 ETag
```

**ETag 计算规范**：
- 算法：SHA-256
- 取前 8 字节，16 个十六进制字符
- 格式：`"<16位hex>"`（双引号包裹）
- 比较：`StringComparison.Ordinal`（精确匹配）

### 16.3 Last-Modified 协商缓存

```
请求头：If-Modified-Since: <RFC 1123 日期>
    ↓
若资源 Last-Modified <= If-Modified-Since：
    返回 304 Not Modified
```

**仅当** `If-None-Match` 未命中时才检查 `If-Modified-Since`（符合 HTTP 缓存语义）。

**日期解析**：
- 优先 RFC 1123 格式（`"r"`）
- 回退到通用解析（RFC 850、asctime）
- 比较精度为秒

### 16.4 Cache-Control

**默认值**：`no-cache`

含义：客户端每次请求必须向服务器校验 ETag/Last-Modified，不允许直接使用本地缓存。

**禁止**：
- 设置 `max-age` 为大值（会导致客户端永不校验）
- 设置 `immutable`（会导致 ETag 失效）

### 16.5 资源名缓存

`BundledAssetServer` 缓存程序集资源名列表：

```csharp
private readonly Dictionary<Assembly, List<string>> _resourceNamesCache = new();

private List<string> GetResourceNames(Assembly assembly)
{
    if (!_resourceNamesCache.TryGetValue(assembly, out var names))
    {
        names = [.. assembly.GetManifestResourceNames()];
        names.Sort(StringComparer.Ordinal);
        _resourceNamesCache[assembly] = names;
    }
    return names;
}
```

**约束**：
- 缓存按程序集实例隔离
- 排序使用 `StringComparer.Ordinal`
- 缓存永不失效（程序集资源名在运行时不变）

---

## 17. Diagnostics（诊断）

### 17.1 当前状态

AssetServer **集成** `Microsoft.Extensions.Logging.ILogger<AssetServer>`（M12 已实现）。

- 日志器为**可选依赖**：未注入时 AssetServer 静默运行，不抛异常。
- 已有公共构造函数保持不变（向后兼容）。
- 新增构造函数与 `SetLogger` 方法允许在任意时刻注入日志器。

### 17.2 日志器注入方式

```csharp
// 方式 1：构造时注入（推荐）
public AssetServer(AssetOptions options, ILogger<AssetServer> logger);

// 方式 2：构造后通过 SetLogger 注入（用于 DesktopApplicationBuilder 流程）
public void SetLogger(ILogger<AssetServer> logger);
```

**约束**：
- 字段类型为 `ILogger<AssetServer>?`（可空），默认 `null`。
- **不**使用 `NullLogger<AssetServer>.Instance` 作为默认值，避免引入额外依赖。
- 所有日志调用使用 `_logger?.LogXxx(...)` 模式，logger 为 null 时直接跳过。
- 派生类（`FileAssetServer`、`BundledAssetServer`）通过 protected `Logger` 属性访问，构造时透传给基类。

### 17.3 日志事件分类

| 事件 | 日志级别 | 触发位置 | 消息格式 |
|------|---------|---------|---------|
| 请求到达 | `Debug` | `ServeHttpAsync` 入口 | `HTTP {Method} {Path}` |
| 资源未找到 | `Warning` | `ServeNotFound` | `资源未找到: {Path}` |
| Range 无效 | `Warning` | `CreateResult`（416 分支） | `Range 请求无效: {RangeHeader}, ContentLength={Length}` |
| 协商缓存命中 | `Trace` | `CreateResult`（304 分支） | `协商缓存命中 (304): {Path}, ETag={ETag}` |
| 请求处理完成 | `Information` | `WriteResultAsync` 结束 | `HTTP {Method} {Path} → {StatusCode} ({ContentLength} bytes)` |
| 客户端断开 | `Debug` | `ServeAssetCoreAsync` catch `HttpListenerException` | `客户端断开连接: {Path}` |
| 未处理异常 | `Error` | `ServeAssetCoreAsync` catch `Exception` | `处理请求时发生未处理异常: {Path}` |
| 自定义错误处理器被调用 | `Debug` | `ServeNotFound` / `ServeError` | `调用自定义错误处理器: {Path}` |

**约束**：
- **禁止记录请求体内容**（可能含敏感数据）。
- **禁止记录响应体内容**（可能含敏感数据）。
- **禁止记录完整头部**（含 `Authorization`、`Cookie` 等敏感字段）。
- 仅记录：HTTP 方法、路径、状态码、Content-Length、ETag、Range 头、异常对象。
- 异常通过 `LogError(exception, message, args)` 传递，由日志器决定是否记录堆栈。

### 17.4 错误处理

| 错误类型 | 处理方式 |
|----------|----------|
| `HttpListenerException` | 静默吞掉（客户端断开），Debug 级别记录 |
| 资源不存在 | `ServeNotFound` → 404，Warning 级别记录 |
| 其他异常 | `ServeError` → 500，Error 级别记录（含异常） |
| 自定义错误处理 | 通过 `AssetOptions.ErrorHandler` 注入，Debug 级别记录调用 |

### 17.5 错误响应格式

**默认 404**：

```
HTTP/1.1 404 Not Found
Content-Type: text/plain; charset=utf-8

404 Not Found: /path/to/resource
```

**默认 500**：

```
HTTP/1.1 500 Internal Server Error
Content-Type: text/plain; charset=utf-8

500 Internal Server Error: <exception message>
```

### 17.6 日志分类器（Logger Category）

- 日志器分类为 `Wails.Net.AssetServer.AssetServer`（即 `ILogger<AssetServer>`）。
- 派生类不创建独立日志器，统一使用基类持有的 `ILogger<AssetServer>`，避免日志分散。
- 若派生类需要更细粒度分类，可在派生类中重写 `Logger` 属性返回不同分类的日志器（当前未实现此需求）。

### 17.7 未来诊断扩展

**当前未实现**以下诊断能力，AI 智能体不得擅自实现：
- 指标收集（请求数、延迟、缓存命中率）
- 健康检查
- 分布式追踪

若需要这些能力，必须先更新本规范并经人类确认。

---

## 18. Performance（性能）

### 18.1 当前性能特征

| 操作 | 复杂度 | 说明 |
|------|--------|------|
| `ServeAsync` | O(N) | N = 中间件数量 |
| `ServeHttpAsync` | O(N + M) | N = HTTP 中间件数，M = 路径中间件数 |
| `GetMimeType` | O(1) | switch 表达式 + 字典查找 |
| `ComputeETag` | O(L) | L = 内容长度，SHA-256 |
| `BundledAssetServer.ReadResource` | O(A × R) | A = 程序集数，R = 资源名数（通配符匹配） |

### 18.2 内存使用规范

**允许使用**：

| API | 场景 |
|-----|------|
| `byte[]` | 资源内容（当前实现） |
| `MemoryStream` | 流到字节数组转换 |
| `StringBuilder` | ETag 构建 |
| `StringBuilder` | CSP 头构建 |

**当前未使用但允许**：

| API | 场景 |
|-----|------|
| `ArrayPool<byte>.Shared` | 大缓冲区复用 |
| `MemoryPool<byte>` | 高性能场景 |
| `PipeWriter` / `PipeReader` | 流式响应 |

**禁止使用**：

| API | 原因 |
|-----|------|
| `MemoryStream.ToArray()` 在热路径 | 产生额外拷贝（`BundledAssetServer.ReadStream` 中使用，可优化） |
| `File.ReadAllBytes` 在热路径 | 当前 `FileAssetServer.ReadFile` 使用，可优化为流式 |

### 18.3 性能优化约束

- ❌ **禁止**在未度量情况下进行"优化"
- ❌ **禁止**引入 `unsafe` 代码
- ❌ **禁止**使用 `GC.AllocateUninitializedArray`（除非 benchmark 证明必要）
- ✅ 缓冲区大小固定为 81920 字节（Range 写入）
- ✅ 正则表达式必须 `RegexOptions.Compiled`
- ✅ 字符串比较必须指定 `StringComparison`

### 18.4 Range 请求缓冲区

```csharp
var buffer = new byte[Math.Min(length, 81920)];
```

**约束**：
- 缓冲区上限 81920 字节（80KB，与 .NET `FileStream` 默认一致）
- 不得修改此值除非 benchmark 证明更优

---

## 19. Tests（测试）

### 19.1 测试框架

- **唯一允许**：TUnit 1.58.0
- 断言库：`TUnit.Assertions`
- **断言必须 `await`**

```csharp
[Test]
public async Task GetMimeType_Html_ReturnsTextHtml()
{
    var server = new StubAssetServer(_ => null);
    await Assert.That(server.GetMimeType("index.html")).IsEqualTo("text/html");
}
```

### 19.2 测试类组织

```
tests/Wails.Net.AssetServer.Tests/
├── AssetServerTests.cs           # 基类测试
├── AssetOptionsTests.cs          # 选项测试
├── FileAssetServerTests.cs       # 文件 Provider 测试
├── BundledAssetServerTests.cs    # 嵌入资源 Provider 测试
├── MiddlewareChainTests.cs       # 中间件链测试
├── NonceInjectorTests.cs         # nonce 注入测试
└── IsolationInjectorTests.cs     # isolation 注入测试
```

### 19.3 测试命名规范

格式：`Method_Scenario_ExpectedBehavior`

```
GetMimeType_Html_ReturnsTextHtml
Add_ExcludesServiceInternalMethods
Call_UnknownID_ReturnsReferenceError
```

### 19.4 测试类规范

```csharp
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

[NotInParallel]  // 共享状态测试必须标记
public sealed class AssetServerTests
{
    private sealed class StubAssetServer : AssetServer
    {
        private readonly Func<string, byte[]?> _reader;

        public StubAssetServer(Func<string, byte[]?> reader)
            : base(new AssetOptions { Handler = "stub" })
        {
            _reader = reader;
        }

        protected override byte[]? ReadAssetCore(string path)
        {
            return _reader(path) ?? base.ReadAssetCore(path);
        }
    }
}
```

### 19.5 覆盖要求

| 组件类型 | 覆盖要求 |
|---------|---------|
| 公共 API | 100% 方法覆盖 |
| 错误路径 | 所有 `catch` 分支必须测试 |
| 边界条件 | 空值、空集合、最大值 |
| MIME 映射 | 每个内置扩展名至少一个测试 |
| Range 请求 | 三种格式 + 无效格式 |
| ETag | 匹配 + 不匹配 |
| Security | nonce 启用/禁用、isolation 启用/禁用 |
| 中间件链 | 注册顺序 = 执行顺序、短路语义 |

### 19.6 运行测试

```bash
# 构建测试项目
dotnet build tests/Wails.Net.AssetServer.Tests/Wails.Net.AssetServer.Tests.csproj

# 运行测试（使用 dotnet run，不是 dotnet test）
dotnet run --project tests/Wails.Net.AssetServer.Tests/Wails.Net.AssetServer.Tests.csproj --no-build
```

**重要**：.NET 10 SDK 不再支持 `dotnet test`（VSTest 模式）。

### 19.7 当前测试统计

| 测试文件 | 测试数量（约） | 覆盖范围 |
|----------|---------------|----------|
| `AssetServerTests.cs` | 30+ | MIME、ETag、Range、404、CSP、CORS、OPTIONS |
| `AssetOptionsTests.cs` | 10+ | 选项默认值、自定义 MIME |
| `FileAssetServerTests.cs` | 15+ | 文件读取、路径穿越、Last-Modified |
| `BundledAssetServerTests.cs` | 15+ | 嵌入资源、多程序集、通配符 |
| `MiddlewareChainTests.cs` | 10+ | 链执行、短路、注册顺序 |
| `NonceInjectorTests.cs` | 15+ | nonce 生成、HTML 注入、CSP 构建 |
| `IsolationInjectorTests.cs` | 10+ | iframe 注入、验证 |
| `FileAssetProviderTests.cs` | 18+ | M7：文件 Provider 读取、路径穿越、Last-Modified |
| `BundledAssetProviderTests.cs` | 16+ | M7：嵌入资源 Provider 读取、多程序集、接口契约 |
| `AssetProviderIntegrationTests.cs` | 12+ | M7：组合模式集成、优先级回退、向后兼容 |
| `AssetResultTests.cs` | 25+ | M8：BytesResult/StatusResult/ErrorResult 属性与 WriteAsync 行为 |
| `AssetServerLoggingTests.cs` | 15+ | M12：ILogger 注入、各事件级别触发、null/Disabled 静默运行 |

**总计**：249 个测试全部通过（截至 2026-07-15，M12 完成）

---

## 20. Milestones（里程碑）

### 20.1 已完成里程碑

| 里程碑 | 内容 | 状态 |
|--------|------|------|
| M1: Core | `AssetServer` 基类、`AssetOptions`、HTTP 处理、ETag、Range | ✅ 完成 |
| M2: Providers | `FileAssetServer`、`BundledAssetServer` | ✅ 完成 |
| M3: Middleware | `IMiddleware`、`IHttpMiddleware`、`MiddlewareChain` | ✅ 完成 |
| M4: Security | `NonceInjector`、`IsolationInjector`、CSP 注入 | ✅ 完成 |
| M5: Integration | `DesktopApplicationBuilder` 装配、平台拦截器集成 | ✅ 完成 |
| M6: Tests | 163 个单元测试全部通过 | ✅ 完成 |
| M7: IAssetProvider | `IAssetProvider` 接口、`FileAssetProvider`、`BundledAssetProvider`、组合模式构造函数、Provider 优先级回退 | ✅ 完成（209 测试通过） |
| M8: IAssetResult | `IAssetResult` 接口、`BytesResult`/`StatusResult`/`ErrorResult`、`CreateResult`/`WriteResultAsync` 重构 | ✅ 完成（234 测试通过） |
| M12: Logging | `ILogger<AssetServer>` 集成、4 个新构造函数、`SetLogger`、8 类日志事件分类 | ✅ 完成（249 测试通过） |

### 20.2 未来里程碑（需人类确认）

| 里程碑 | 内容 | 前置条件 |
|--------|------|----------|
| M9: Memory Provider | 内存字典资源源 | M7 完成 ✅ |
| M10: Plugin Provider | 插件资源加载 | M7 完成 ✅ + 插件系统设计 |
| M11: Streaming | `PipeWriter`/`PipeReader` 流式响应 | 性能 benchmark 证明必要 |
| M13: Trie Routing | 路由树 + 模板匹配 | 本规范更新（当前禁止） |

### 20.3 里程碑规则

- **每个里程碑必须独立可交付**
- **不得跳过里程碑**（M7 未完成不得开始 M9）
- **每个里程碑必须包含完整测试**
- **里程碑完成后必须更新本规范第 20 章**

---

## 21. Implementation Rules for AI（AI 实现规则）

> **本章是 AI 智能体（包括 GLM-5.2）在本模块工作的强制规则。**
> 违反任何一条都将导致代码被拒绝。

### 21.1 工作流程规则

① **一次仅完成一个 Milestone**：不得同时进行多个里程碑的工作。

② **不得修改已完成 API**：已实现的公共方法签名、属性、类名不得变更。

③ **必须保持 ABI 稳定**：任何修改不得破坏现有调用方。

④ **不得重构公共接口**：如需重构，必须先更新本规范并经人类确认。

### 21.2 代码生成规则

⑤ **所有新增类必须实现对应接口**：新增中间件必须实现 `IMiddleware` 或 `IHttpMiddleware`；新增 Provider 必须继承 `AssetServer` 或实现未来定义的 `IAssetProvider`。

⑥ **禁止生成 Demo**：不得创建示例应用、Demo 项目。

⑦ **禁止生成 Sample**：不得创建示例代码文件。

⑧ **禁止生成 README**：不得创建或修改 README，除非人类明确要求。

⑨ **优先生成 Runtime**：运行时代码优先于测试代码，但测试必须在同一里程碑内完成。

⑩ **所有 TODO 必须实现**：代码中不得遗留 `// TODO` 注释，所有功能必须完整实现。

### 21.3 架构规则

⑪ **禁止引入 ASP.NET Core**：AssetServer 使用 `HttpListener`，不使用 Kestrel/ASP.NET Core 管道。

⑫ **禁止引入新依赖**：不得添加 NuGet 包，除非本规范明确允许。

⑬ **禁止使用反射**（除允许场景）：仅 `BundledAssetServer` 的 `Assembly.GetManifestResource*` 允许。

⑭ **禁止使用 `dynamic`**：所有类型必须静态已知。

⑮ **禁止使用 `Emit`/`DynamicMethod`**：不支持动态代码生成。

### 21.4 命名规则

⑯ **类名不得与命名空间同名**：避免 CS0118 错误。

⑰ **接口必须以 `I` 开头**：`IMiddleware`、`IHttpMiddleware`、`IAssetProvider`（未来）。

⑱ **异步方法以 `Async` 结尾**：`ServeAsync`、`ServeHttpAsync`、`ExecuteAsync`。

⑲ **私有字段以 `_` 开头**：`_options`、`_middlewareChain`、`_rootPath`。

### 21.5 安全规则

⑳ **路径穿越防护必须存在**：任何从文件系统读取的 Provider 必须实现 `ResolveFullPath` 等价逻辑。

㉑ **nonce 必须使用密码学 RNG**：`RandomNumberGenerator`，禁止 `Random`/`Guid`。

㉒ **CSP 头必须可配置**：不得硬编码 CSP 策略。

### 21.6 测试规则

㉓ **所有公共方法必须有单元测试**：100% 方法覆盖。

㉔ **测试必须使用 TUnit**：禁止 MSTest/xUnit/NUnit。

㉕ **断言必须 `await`**：`await Assert.That(x).IsEqualTo(y)`。

㉖ **测试类必须 `[NotInParallel]`**：当涉及共享状态时。

㉗ **测试必须 `dotnet run` 执行**：不使用 `dotnet test`。

### 21.7 文档规则

㉘ **所有公共 API 必须有 XML 文档注释**：使用 `///`，中文描述。

㉙ **必须描述对应 Wails v3 源文件**：如"对应 Wails v3 Go 版本 `assetserver.go`"。

㉚ **禁止创建未请求的文档文件**：除非人类明确要求。

### 21.8 修改规则

㉛ **修改前必须阅读本规范全文**：确保理解所有约束。

㉜ **修改后必须更新本规范**：若修改了公共 API 或行为，必须同步更新本规范对应章节。

㉝ **修改必须通过所有现有测试**：不得破坏现有 163 个测试。

㉞ **修改必须零警告**：`TreatWarningsAsErrors=true`，任何警告视为错误。

---

## 附录 A：Headers 常量定义

```csharp
public static class Headers
{
    public const string ContentType = "Content-Type";
    public const string ContentLength = "Content-Length";
    public const string ContentRange = "Content-Range";
    public const string AcceptRanges = "Accept-Ranges";
    public const string Range = "Range";
    public const string ETag = "ETag";
    public const string IfNoneMatch = "If-None-Match";
    public const string LastModified = "Last-Modified";
    public const string IfModifiedSince = "If-Modified-Since";
    public const string CacheControl = "Cache-Control";
    public const string AccessControlAllowOrigin = "Access-Control-Allow-Origin";
    public const string WindowId = "x-wails-window-id";
    public const string WindowName = "x-wails-window-name";
}
```

**约束**：
- 与 Wails v3 Go 版本 `common.go` 保持一致
- 不得删除已有常量
- 新增常量必须更新本附录

---

## 附录 B：MIME 类型映射表

| 扩展名 | MIME 类型 |
|--------|-----------|
| `.html` `.htm` | `text/html` |
| `.css` | `text/css` |
| `.js` `.mjs` | `application/javascript` |
| `.json` | `application/json` |
| `.xml` | `application/xml` |
| `.txt` | `text/plain` |
| `.svg` | `image/svg+xml` |
| `.png` | `image/png` |
| `.jpg` `.jpeg` | `image/jpeg` |
| `.gif` | `image/gif` |
| `.webp` | `image/webp` |
| `.ico` | `image/x-icon` |
| `.bmp` | `image/bmp` |
| `.woff` | `font/woff` |
| `.woff2` | `font/woff2` |
| `.ttf` | `font/ttf` |
| `.otf` | `font/otf` |
| `.wasm` | `application/wasm` |
| `.pdf` | `application/pdf` |
| `.zip` | `application/zip` |
| `.map` | `application/json` |
| `.mp4` | `video/mp4` |
| `.webm` | `video/webm` |
| `.mp3` | `audio/mpeg` |
| `.ogg` | `audio/ogg` |
| `.wav` | `audio/wav` |
| （其他） | `application/octet-stream` |

---

## 附录 C：端到端请求流程

```
1. appsettings.json 配置 Desktop.Assets.RootPath = "dist"
    ↓
2. DesktopApplicationBuilder.Build() 读取配置
    ↓
3. 解析 RootPath（相对路径基于 AppContext.BaseDirectory）
    ↓
4. 检查 Directory.Exists(rootPath) → 创建 FileAssetServer
    ↓
5. Application.AssetServer = fileAssetServer
    ↓
6. 窗口初始化（无显式 URL/HTML）
    ↓
7. 检测 Application.AssetServer != null
    ↓
8. Navigate("http://wails.localhost/")  // Windows
   Navigate("wails://localhost/")      // Linux
    ↓
9. Webview 拦截请求（WebResourceRequested / OnWailsSchemeRequest）
    ↓
10. IPC 端点检查（POST /wails/* → Application.HandleMessageFromFrontend）
    ↓
11. assetServer.ServeAsync(path).GetAwaiter().GetResult()
    ↓
12. MiddlewareChain.ExecuteAsync
    ├── IMiddleware 链执行
    └── ReadAssetCore（FileAssetServer.ReadFile / BundledAssetServer.ReadResource）
    ↓
13. SPA 回退（若未命中且 EnableSpaFallback）
    ↓
14. 返回 byte[]
    ↓
15. 平台拦截器构造响应
    ├── Windows: Environment.CreateWebResourceResponse
    └── Linux: URISchemeResponse.New
    ↓
16. Webview 渲染
```

---

## 附录 D：与 Wails v3 Go 版本对照

| Go 类型 | C# 类型 | 说明 |
|---------|---------|------|
| `assetserver.AssetServer` | `AssetServer` | 基类 |
| `assetserver.Options` | `AssetOptions` | 选项 |
| `fileassets.FileAssetServer` | `FileAssetServer` | 文件 Provider |
| `bundledassets.BundledAssetServer` | `BundledAssetServer` | 嵌入资源 Provider |
| `common.Headers` | `AssetServer.Headers` | 头常量 |
| `assetserver.GetMimetype` | `AssetServer.GetMimeType` | MIME 解析 |
| `assetserver.ServeHTTP` | `AssetServer.ServeHttpAsync` | HTTP 处理 |

**差异**：
- Go 版本使用 `http.Handler` 接口，C# 版本使用 `HttpListenerContext`
- Go 版本中间件使用 `func(http.Handler) http.Handler`，C# 版本使用 `IMiddleware`/`IHttpMiddleware` 接口
- Go 版本 ETag 使用 FNV-1a，C# 版本使用 SHA-256 前 16 字符（**注意**：此处与 Go 不一致，是已知差异）

---

## 附录 E：与 Tauri v2 安全模型对照

| Tauri 概念 | Wails.Net 实现 | 说明 |
|------------|----------------|------|
| CSP nonce | `NonceInjector` | 32 字节 base64 nonce |
| CSP 头构建 | `NonceInjector.BuildCspHeader` | 追加到 `script-src` |
| Isolation Pattern | `IsolationInjector` | iframe 隔离 |
| Isolation iframe | `IsolationInjector.BuildIframeTag` | 隐藏 iframe |
| Capability | （未实现） | 当前无 Capability 系统 |
| Permission | （未实现） | 当前无 Permission 系统 |
| Scope | （未实现） | 当前无 Scope 系统 |

**约束**：Capability/Permission/Scope 系统属于 `Wails.Net.Application` 的插件安全层，**不属于** AssetServer。AssetServer 仅负责 CSP 和 Isolation。

---

## 变更记录

| 日期 | 版本 | 变更 |
|------|------|------|
| 2026-07-15 | 1.0 | 初始规范，描述当前已实现的 AssetServer（M1-M6 全部完成） |

---

**本规范结束。任何对本模块的修改必须先阅读本规范全文，并遵循第 21 章的 AI 实现规则。**
