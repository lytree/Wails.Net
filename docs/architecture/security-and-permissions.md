# 安全与权限模型

## 1. 概述

Wails.Net 的安全体系借鉴 **Tauri v2 的多层防护策略**，将安全职责分散到六个相互独立又彼此配合的层面，从外部资源加载到内部命令执行形成完整防护链：

| 防护层 | 职责 | 核心类型 |
|--------|------|----------|
| 内容安全策略（CSP） | 限制 WebView 内可加载的脚本、样式、连接来源 | `CspOptions` |
| URL 白名单 | 限制可打开的外部链接与导航目标 | `UrlWhitelist` |
| IPC 来源验证 | 校验 IPC 消息的来源 Origin 是否可信 | `IpcOriginValidator` |
| 能力声明（Capability） | 静态声明命令所需的能力标识 | `Capability`、`RequireCapabilityAttribute` |
| 权限管理器 | 运行时校验命令调用是否已授权对应能力 | `PermissionManager` |
| 文件系统沙箱 | 限制文件命令可访问的路径范围 | `FileSystemPlugin`、`PersistedScopePlugin` |
| 加密存储 | 安全保存密码、密钥等敏感数据 | `StrongholdPlugin` |

核心安全代码位于 `src/Wails.Net.Application/Security/`，文件系统与加密插件位于 `src/Wails.Net.Application/Plugins/BuiltIn/`。

设计原则：

1. **默认安全**：CSP 默认启用且使用 `'self'` 严格策略；文件系统沙箱可选但推荐启用。
2. **向后兼容**：权限检查默认关闭（`PermissionOptions.Enabled = false`），未启用时全部放行，避免破坏既有应用。
3. **零信任文件路径**：所有文件命令统一过 `GetSafePath` 算法，路径穿越攻击在沙箱模式下被阻断。
4. **加密静态数据**：敏感数据通过 AES-GCM 认证加密落盘，密码从不以明文形式持久化。

## 2. 内容安全策略（CSP）

### 2.1 CspOptions — CSP 头部配置

[CspOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/CspOptions.cs) 定义 CSP 头部各指令的来源策略，对应 Tauri v2 的 CSP 安全配置。默认值遵循"最小权限"原则：

| 指令 | 默认值 | 说明 |
|------|--------|------|
| `default-src` | `'self'` | 默认来源仅限同源 |
| `script-src` | `'self'` | 脚本仅限同源（禁止 inline） |
| `style-src` | `'self' 'unsafe-inline'` | 样式允许 inline（兼容常见 CSS 框架） |
| `img-src` | `'self' data:` | 图片允许 data URI |
| `font-src` | `'self'` | 字体仅限同源 |
| `connect-src` | `'self'` | XHR/Fetch/WebSocket 仅限同源 |
| `frame-src` | `'none'` | 禁止 iframe 嵌入 |
| `object-src` | `'none'` | 禁止 `<object>`/`<embed>` |

`BuildHeader()` 方法将各指令拼接为标准 CSP 头部字符串；当 `Enabled = false` 时返回空字符串以禁用 CSP。

### 2.2 AssetServer.SetCspHeader — 响应头注入

[AssetServer.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 在处理 HTTP 请求时统一注入 CSP 头。Application 层在配置阶段调用 `SetCspHeader` 注入由 `CspOptions.BuildHeader()` 构建的头部值：

```csharp
// AssetServer.ServeHttpAsync 中的注入逻辑
if (!string.IsNullOrEmpty(_cspHeader))
{
    response.Headers["Content-Security-Policy"] = _cspHeader;
}
```

注入点位于请求处理的最前端，所有响应（包括静态资源、OPTIONS 预检、404）都会携带 CSP 头，确保 WebView 加载的任何资源都受策略约束。

### 2.3 默认策略与自定义策略

默认策略适合大多数纯前端 SPA 应用。若需加载 CDN 资源或对接外部 API，可通过 `ApplicationOptions.Csp` 自定义：

```csharp
var options = new ApplicationOptions
{
    Csp = new CspOptions
    {
        Enabled = true,
        ScriptSrc = "'self' https://cdn.jsdelivr.net",
        StyleSrc  = "'self' 'unsafe-inline' https://cdn.jsdelivr.net",
        ConnectSrc = "'self' https://api.example.com wss://api.example.com",
        ImgSrc = "'self' data: https:",
    }
};
```

## 3. URL 白名单

### 3.1 UrlWhitelist — 允许的外部链接

[UrlWhitelist.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/UrlWhitelist.cs) 维护一组允许的 URL 模式集合，支持 `*` 通配符匹配（如 `https://*.example.com`）。匹配算法将通配符转为正则 `.*` 并对其余字符转义，使用 `RegexOptions.IgnoreCase` 进行大小写不敏感匹配。

```csharp
var whitelist = new UrlWhitelist();
whitelist.Add("https://*.example.com");
whitelist.Add("https://github.com/wailsapp/*");

bool ok = whitelist.IsAllowed("https://docs.example.com/intro");  // true
bool no = whitelist.IsAllowed("https://evil.com");                 // false
```

### 3.2 外部链接打开前的校验

[OpenerPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/OpenerPlugin.cs)（对应 Tauri v2 的 `@tauri-apps/plugin-opener`）复用 `UrlWhitelist` 进行通配符模式匹配，并通过协议白名单默认拦截 `file://`、`javascript:`、`vbscript:` 等危险协议：

```csharp
public class OpenerPlugin : IPlugin
{
    private static readonly HashSet<string> DefaultAllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto",
    };

    private readonly UrlWhitelist _urlWhitelist = new();

    public bool IsUrlAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!_allowedSchemes.Contains(uri.Scheme)) return false;
        if (_urlWhitelist.Patterns.Count > 0)
            return _urlWhitelist.IsAllowed(url);
        return true;
    }
}
```

`IsUrlAllowed` 采用**双闸门校验**：协议白名单（默认仅 http/https/mailto）+ URL 模式白名单（可选，配置后启用）。仅当两个闸门都通过时才允许打开，未配置模式白名单时仅校验协议。

`ApplicationOptions.AllowedUrls` 属性可将 `UrlWhitelist` 实例注入应用全局配置，供 IPC 层和 Opener 共享同一份白名单。

## 4. IPC 来源验证

### 4.1 IpcOriginValidator — IPC 请求来源校验

[IpcOriginValidator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/IpcOriginValidator.cs) 验证 WebView 发出的 IPC 消息来源是否可信，对应 Tauri v2 的 IPC 安全校验。校验逻辑分两级：

1. **本地源直通**：`wails://`、`https://wails.localhost`、`http://localhost`、`http://127.0.0.1` 视为可信来源，直接放行。
2. **外部源查表**：非本地源必须命中 `UrlWhitelist`，否则拒绝。

```csharp
public sealed class IpcOriginValidator
{
    private readonly UrlWhitelist _whitelist;

    public bool Validate(string? origin)
    {
        if (string.IsNullOrEmpty(origin)) return true;   // 无 Origin 头视为本地
        if (IsLocalOrigin(origin)) return true;            // 本地源直通
        return _whitelist.IsAllowed(origin);                // 外部源查白名单
    }
}
```

### 4.2 防止非授权来源的 IPC 调用

`IpcOriginValidator` 防御的核心场景是**恶意页面冒充应用调用 IPC**。例如：

- 钓鱼页面 `https://evil.com` 通过 iframe 嵌入应用资源并尝试 `window.postMessage` 调用 `fs.read`。
- 应用加载了第三方脚本，脚本向外部域名泄漏 IPC 协议后诱导回连。

配合 CSP 的 `frame-src 'none'` 与 `connect-src 'self'`，IPC 来源验证构成第二道防线：即使 CSP 被绕过，外部来源的 IPC 请求也会被 `IpcOriginValidator` 拒绝。空 Origin 视为本地（应用自身发起），保证原生宿主调用不受影响。

## 5. 能力声明（Capability）

### 5.1 运行时能力模型 — Security.Capability

[Capability.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Capability.cs) 是运行时能力声明模型，描述命令所需的能力标识，借鉴 Tauri v2 的 Capability 设计：

```csharp
public sealed class Capability
{
    public string Id { get; init; }              // 能力标识，如 "filesystem.read"
    public string Description { get; init; }    // 描述
    public string Plugin { get; init; }          // 所属插件
}
```

### 5.2 配置层能力模型 — Options.Capability

[Options/Capability.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Options/Capability.cs) 是配置层模型，对应 Tauri v2 的 Capabilities 配置，用于从 `appsettings.json` 或代码中声明应用所需的能力及作用窗口范围：

```csharp
public sealed class Capability
{
    public string Identifier { get; set; }          // 能力标识符
    public string Description { get; set; }         // 描述
    public List<string> Permissions { get; set; }   // 包含的权限标识列表
    public List<string> Windows { get; set; }       // 作用窗口名（空列表=全部窗口）
}
```

`ApplicationOptions.Capabilities` 持有该列表，可在应用启动时通过代码或配置文件注入。`Windows` 字段支持窗口级能力隔离：同一能力可仅对特定窗口生效，避免在次要窗口暴露敏感命令。

### 5.3 命令级权限标注 — RequireCapabilityAttribute

[CapabilityAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/CapabilityAttribute.cs) 实际定义的是 `RequireCapabilityAttribute`，用于在命令方法上标注所需能力，支持 `AllowMultiple = true` 与 `Inherited = true`：

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireCapabilityAttribute : Attribute
{
    public string Capability { get; }
    public RequireCapabilityAttribute(string capability) => Capability = capability;
}
```

在命令方法上叠加多个 `[RequireCapability]` 即声明该命令同时需要多项能力——`PermissionManager.ValidateCommand` 要求**所有标注的能力都已授权**才放行（AND 语义）。

```csharp
public class MyService
{
    [RequireCapability("filesystem.read")]
    [RequireCapability("user.sensitive")]
    public string ReadSensitiveData(string path) => File.ReadAllText(path);
}
```

## 6. 权限管理器

### 6.1 PermissionManager — 运行时权限校验

[PermissionManager.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionManager.cs) 是权限校验的运行时入口，维护两个集合：

- `_grantedCapabilities`：已授权的能力标识集合（`HashSet<string>`，大小写不敏感）。
- `_declaredCapabilities`：已声明的能力元数据字典（`Dictionary<string, Capability>`）。

核心方法 `ValidateCommand(MethodInfo)` 通过反射读取命令方法上的 `RequireCapabilityAttribute`，逐项校验是否已授权：

```csharp
public bool ValidateCommand(MethodInfo method)
{
    if (!_options.Enabled) return true;   // 未启用权限检查时全部放行

    var attrs = method.GetCustomAttributes<RequireCapabilityAttribute>();
    foreach (var attr in attrs)
    {
        if (!IsGranted(attr.Capability))
        {
            _logger?.LogWarning("权限拒绝: 命令 {Method} 需要能力 {Capability}",
                method.Name, attr.Capability);
            return false;
        }
    }
    return true;
}
```

`IsGranted` 在 `Enabled = false` 时**全部放行**，保证向后兼容；启用后采用**白名单语义**——只有显式授权的能力可用，未声明的能力默认拒绝（由 `DenyByDefault` 控制）。

运行时可通过 `Grant` / `Revoke` 动态调整权限，例如用户登录后授权 `user.sensitive`、注销时撤销。`Grant` 会记录日志便于审计。

### 6.2 CommandDispatcher.DispatchAsync 中的权限校验流程

[CommandDispatcher.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) 在 `DispatchAsync` 中将权限校验置于**命令查找之后、中间件管道之前**：

```csharp
public async Task<InvokeResponse> DispatchAsync(InvokeRequest request, ICommandContext? context = null)
{
    // ...超时令牌构建...

    var entry = _registry.Find(request.Method);
    if (entry == null)
        return new InvokeResponse(request.Id, false, null, $"Command not found: {request.Method}");

    // 权限校验（早于中间件管道，避免无权请求消耗中间件资源）
    if (_permissionManager is not null && !_permissionManager.ValidateCommand(entry.Method))
    {
        _logger?.LogWarning("权限拒绝: 命令 {Method}", request.Method);
        return new InvokeResponse(request.Id, false, null, $"Permission denied: {request.Method}");
    }

    // 构建中间件管道并执行...
}
```

校验流程的三个关键点：

1. **可选注入**：`PermissionManager` 作为可选构造参数（`PermissionManager?`），未注入时跳过校验，便于测试和向后兼容。
2. **早返回**：权限不足时立即返回 `InvokeResponse(success: false)`，不进入中间件管道，节省资源。
3. **错误信息脱敏**：返回 `"Permission denied: {Method}"` 仅暴露命令名，不泄漏所需能力标识，避免攻击者探测能力清单。

### 6.3 PermissionOptions 配置

[PermissionOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionOptions.cs) 从 `appsettings.json` 的 `Desktop:Permissions` 节绑定：

```json
{
  "Desktop": {
    "Permissions": {
      "Enabled": true,
      "DenyByDefault": true,
      "Permissions": [ "filesystem.read", "filesystem.write", "stronghold.unlock" ]
    }
  }
}
```

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `Enabled` | `false` | 是否启用权限检查（默认关闭，向后兼容） |
| `DenyByDefault` | `true` | 未声明能力是否拒绝 |
| `Permissions` | `[]` | 已授权能力标识列表 |

> **注意**：`Enabled` 默认为 `false` 是为了不破坏既有应用——升级后未配置权限的应用仍可正常运行。生产环境强烈建议显式启用。

### 6.4 PermissionServiceExtensions DI 注册

[PermissionServiceExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionServiceExtensions.cs) 提供两个 DI 扩展方法：

```csharp
// 注册权限服务（绑定配置 + 注册 PermissionManager 单例）
builder.Services.AddPermissions(opts =>
{
    opts.Enabled = true;
    opts.Permissions.Add("filesystem.read");
});

// 单独启用权限检查（在 AddPermissions 之后调用）
builder.Services.EnablePermissions();
```

`AddPermissions` 通过 `BindConfiguration("Desktop:Permissions")` 绑定配置节，再调用可选的 `configure` 回调覆盖配置，最后注册 `PermissionManager` 为单例。`EnablePermissions` 是便捷方法，等价于 `Configure<PermissionOptions>(o => o.Enabled = true)`。

## 7. 文件系统安全

### 7.1 FileSystemPlugin 沙箱根（SandboxRoot）

[FileSystemPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/FileSystemPlugin.cs) 提供文件读写命令，对应 Tauri v2 的 `@tauri-apps/api/fs`。沙箱根通过构造函数注入：

```csharp
// 无沙箱（受信任应用，前端可访问任意路径）
var fs = new FileSystemPlugin();

// 带沙箱（推荐，前端只能访问指定根目录）
var fs = new FileSystemPlugin("/var/appdata/myapp/sandbox");
```

所有文件命令（`fs.read`、`fs.write`、`fs.exists`、`fs.delete`、`fs.copy`、`fs.mkdir` 等）都通过 `GetSafePath` 算法进行路径校验，确保前端无法通过 `../` 或绝对路径逃逸沙箱。

### 7.2 GetSafePath 路径校验算法

`GetSafePath` 是文件系统安全的核心算法，分两阶段防护：

```csharp
private string GetSafePath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("路径不能为空。", nameof(path));

    // 阶段1：路径规范化
    var fullPath = Path.IsPathRooted(path)
        ? Path.GetFullPath(path)                            // 绝对路径直接规范化
        : _sandboxRoot is not null
            ? Path.GetFullPath(path, _sandboxRoot)           // 相对路径以沙箱根为基
            : Path.GetFullPath(path);                        // 无沙箱时按当前目录

    // 阶段2：沙箱边界校验
    if (_sandboxRoot is not null)
    {
        var rootWithSep = _sandboxRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _sandboxRoot
            : _sandboxRoot + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase    // Windows 文件系统大小写不敏感
            : StringComparison.Ordinal;            // Linux 严格大小写

        if (!fullPath.StartsWith(rootWithSep, comparison) && fullPath != _sandboxRoot)
            throw new UnauthorizedAccessException($"路径超出沙箱范围: {path}");
    }

    return fullPath;
}
```

算法关键点：

1. **`Path.GetFullPath` 规范化**：解析 `..`、`.`、符号链接（部分），将路径转为绝对路径，使 `../etc/passwd` 这类穿越攻击在规范化阶段即被消解为真实路径。
2. **前缀匹配校验**：规范化后的路径必须以 `<sandboxRoot><目录分隔符>` 开头，或等于沙箱根本身。补充分隔符是为了防止 `/var/appdata/myapp` 沙箱被 `/var/appdata/myapp-secret` 绕过（前者是后者的前缀但不在沙箱内）。
3. **跨平台大小写**：Windows 使用 `OrdinalIgnoreCase` 匹配文件系统大小写不敏感特性，Linux 使用 `Ordinal` 严格匹配。
4. **抛出 `UnauthorizedAccessException`**：违反沙箱边界时抛出权限异常而非参数异常，便于上层捕获并区分语义。

无沙箱模式（`_sandboxRoot = null`）下 `GetSafePath` 仍执行路径规范化（阶段1），可消除 `..` 等冗余，但不阻止访问任意路径——仅适用于完全受信任的应用。

### 7.3 PersistedScopePlugin — 文件系统范围持久化

[PersistedScopePlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/PersistedScopePlugin.cs) 对应 Tauri v2 的 `@tauri-apps/plugin-persisted-scope`，允许运行时动态添加/移除允许访问的路径，并将范围变更持久化到 JSON 文件，应用重启后恢复之前的 scope 配置。

`FileSystemScope` 内部维护 `_allowedPaths`（`HashSet<string>`，大小写不敏感），支持通配符匹配：

| 通配符 | 含义 | 示例 |
|--------|------|------|
| `*` | 匹配单层路径段 | `/data/*.txt` |
| `**` | 匹配多层路径段 | `/data/**/secret.json` |

`IsAllowed` 采用**精确匹配优先 + 通配符回退**策略，先检查路径是否直接在允许集合中，再遍历所有模式做正则匹配。`MatchPattern` 将 `**` 转为 `.*`、`*` 转为 `[^/\\]*`，避免跨目录匹配意外放行。

```csharp
// 前端调用示例
await wails.call("scope.addPath", ["/data/user1/**", null]);
await wails.call("scope.isAllowed", ["/data/user1/docs/file.txt", null]);  // true
await wails.call("scope.isAllowed", ["/data/user2/docs/file.txt", null]); // false
```

`PersistedScopePlugin` 与 `FileSystemPlugin` 沙箱是**互补关系**：沙箱提供静态边界（启动时固定），scope 提供动态边界（运行时可调整）。生产环境建议两者结合——沙箱限制最大可达范围，scope 在沙箱内进一步细分。

## 8. 加密存储

### 8.1 StrongholdPlugin — 加密安全存储

[StrongholdPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/StrongholdPlugin.cs) 对应 Tauri v2 的 `@tauri-apps/plugin-stronghold`，提供密码、密钥等敏感数据的安全存储。采用 **AES-GCM 认证加密 + PBKDF2 密钥派生**，加密数据持久化到 JSON 文件。

金库文件格式：

```json
{
  "salt": "<Base64 PBKDF2 盐>",
  "nonce": "<Base64 AES-GCM nonce>",
  "ciphertext": "<Base64 加密后的 JSON 字典>",
  "tag": "<Base64 AES-GCM 认证标签>"
}
```

### 8.2 密码保护与密钥派生

密钥派生使用 `Rfc2898DeriveBytes.Pbkdf2`，参数如下：

| 参数 | 值 | 说明 |
|------|----|------|
| 算法 | `HashAlgorithmName.SHA256` | HMAC-SHA256 作为伪随机函数 |
| 迭代次数 | `100_000` | `Pbkdf2Iterations` 常量，抵御暴力破解 |
| 盐长度 | 16 字节 | 每个金库独立盐值，`RandomNumberGenerator.GetBytes` 生成 |
| 密钥长度 | 32 字节（256 位） | AES-256 |
| nonce 长度 | 12 字节 | AES-GCM 标准 nonce 长度 |
| 认证标签 | 16 字节（128 位） | `TagSize` 常量，提供完整性保护 |

### 8.3 保险库锁定/解锁

`StrongholdVault` 内部类维护内存中的解密密钥与秘密字典，提供锁定/解锁生命周期：

- **`Unlock(password, iterations)`**：文件存在则验证密码并解密；文件不存在则创建新金库。密码错误时通过 `CryptographicException` 捕获并返回 `false`，密钥被立即 `ZeroMemory` 清零。
- **`Lock()`**：清除内存中的密钥、盐、nonce 和所有秘密数据，全部 `CryptographicOperations.ZeroMemory` 清零，防止内存转储攻击。
- **`ChangePassword(old, new, iterations)`**：验证旧密码（重新派生密钥与当前密钥比对），通过后生成新盐和新密钥，重新加密落盘。
- **`IsUnlocked`**：仅当 `_key` 非空时为 `true`，所有读写操作前均检查此状态。

```csharp
// 前端调用示例
await wails.call("stronghold.unlock", ["my-password", null]);
await wails.call("stronghold.saveSecret", ["api-key", "sk-xxx", null]);
var key = await wails.call("stronghold.getSecret", ["api-key", null]);
await wails.call("stronghold.lock", [null]);
```

所有秘密操作（`saveSecret`、`getSecret`、`deleteSecret`、`listKeys`）在金库未解锁时返回 `false` 或 `null`，不抛出异常，便于前端做容错处理。`_vaults` 静态字典按文件路径隔离多个金库实例，支持同一应用管理多个独立保险库。

> **安全提示**：AES-GCM 的 nonce 在每次 `SaveVault` 时**不重新生成**（仅 `ChangePassword` 时刷新），多次写入同一金库会复用 nonce+key 对——这在 AES-GCM 中是危险的（同一密钥+nonce 加密不同明文会破坏机密性）。建议生产环境每次写入时重新生成 nonce 并迁移密钥派生，或改用 `RandomNumberGenerator` 为每次写入生成新 nonce。

## 9. 安全最佳实践

### 9.1 推荐配置

**生产环境最小安全配置**：

```csharp
var builder = DesktopApplication.CreateBuilder(args);

// 1. 启用严格 CSP
builder.Services.Configure<ApplicationOptions>(opts =>
{
    opts.Csp = new CspOptions
    {
        Enabled = true,
        DefaultSrc = "'self'",
        ScriptSrc = "'self'",
        ConnectSrc = "'self' https://api.example.com",
        FrameSrc = "'none'",
        ObjectSrc = "'none'",
    };

    // 2. 配置 URL 白名单（如需打开外部链接）
    opts.AllowedUrls = new UrlWhitelist();
    opts.AllowedUrls.Add("https://*.example.com");
});

// 3. 启用权限检查并显式授权
builder.Services.AddPermissions(opts =>
{
    opts.Enabled = true;
    opts.DenyByDefault = true;
    opts.Permissions.Add("filesystem.read");
    opts.Permissions.Add("opener.openUrl");
});
builder.Services.EnablePermissions();

// 4. 文件系统插件启用沙箱
builder.UsePlugin(new FileSystemPlugin(appDataPath));
```

**`appsettings.json` 等价配置**：

```json
{
  "Desktop": {
    "Permissions": {
      "Enabled": true,
      "DenyByDefault": true,
      "Permissions": [ "filesystem.read", "filesystem.write", "opener.openUrl" ]
    }
  }
}
```

### 9.2 注意事项

1. **权限检查默认关闭**：升级既有应用时务必显式设置 `Enabled = true`，否则所有命令无门槛可调。
2. **沙箱根绝对路径**：`FileSystemPlugin` 构造函数接收的 `sandboxRoot` 会被 `Path.GetFullPath` 规范化，建议传入绝对路径避免依赖工作目录。
3. **敏感数据用 Stronghold**：API Key、OAuth Token 等绝不要写入 `appsettings.json` 或普通文件，统一通过 `StrongholdPlugin` 加密存储。
4. **CSP 与开发环境**：开发时若使用 Vite/Webpack Dev Server，需在 `ConnectSrc` 中加入 `ws://localhost:*` 和 `http://localhost:*`，否则 HMR 失效。
5. **权限粒度**：`RequireCapability` 的能力标识建议采用 `<plugin>.<action>` 命名（如 `filesystem.read`、`stronghold.unlock`），与 Tauri v2 习惯一致，便于跨项目复用。
6. **IPC Origin 兜底**：`IpcOriginValidator.Validate` 对空 Origin 返回 `true`（视为本地），若 WebView 配置允许跨域请求，需额外在 IPC 入口处校验 `Origin` 头非空。
7. **审计日志**：`PermissionManager.Grant` 与 `ValidateCommand` 拒绝路径都会记录日志，生产环境应将日志接入 SIEM 监控异常授权或频繁拒绝事件。
8. **金库文件备份**：`stronghold.vault.json` 丢失即丢失所有秘密，建议纳入备份策略；密码遗忘则数据不可恢复（PBKDF2 单向派生）。
