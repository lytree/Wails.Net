# 绑定系统与命令调度

## 1. 概述

Wails.Net 在前端 IPC 调用与后端 C# 方法之间采用**双轨调用架构**，同时维护两条互补的调用通路：

| 调用通路 | 核心组件 | 注册方式 | 调用形式 | 性能特征 |
|---------|---------|---------|---------|---------|
| **绑定系统** | [`BindingManager`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) | 反射扫描 + 源生成器 | `wails.call("ClassName.MethodName", args)` | 优先走强类型调用器，回退反射 |
| **命令调度** | [`CommandDispatcher`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) | `[Command]` / `MapCommand` / `[DesktopCommand]` | `wails.call("custom.command.name", args)` | 表达式树编译后零反射 |

绑定系统直接移植自 Wails v3 Go 版本的 `bindings.go`，使用 `Namespace.ClassName.MethodName` 全限定名作为键、并以 FNV-1a 32 位哈希生成 ID，确保跨语言兼容；命令调度系统借鉴 ASP.NET Core Minimal API 与 Tauri v2 的"核心即插件"哲学，以命令名（如 `counter.increment`、`window.setTitle`）作为分发依据，并内建中间件管道、权限校验、超时取消等横切能力。传输层的 [`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 在 `call` 消息中先查询 BindingManager，未命中再回退到 CommandDispatcher，使两条通路对前端完全透明。

```csharp
// 前端发起的 JSON 消息
{ "id": "1", "type": "call",
  "payload": { "name": "GreetingService.Greet", "args": ["World"] } }
//                  ↑ 命中 BindingManager（生成器调用器）

{ "id": "2", "type": "call",
  "payload": { "name": "counter.increment", "args": [{"by": 1}] } }
//                  ↑ 命中 CommandDispatcher（[Command] 注册）
```

## 2. 绑定系统

### 2.1 BindingManager — 反射扫描与注册

[`BindingManager`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) 是绑定系统的核心，对应 Wails v3 Go 版本 `bindings.go` 中的 `Bindings` 结构。它维护三张内部字典：

```csharp
private readonly Dictionary<string, BoundMethod> _boundMethods = new();       // 全限定名 → BoundMethod
private readonly Dictionary<uint, BoundMethod>   _boundByID     = new();       // FNV-1a ID → BoundMethod
private readonly Dictionary<string, object>      _instancesByTypeName = new(StringComparer.Ordinal); // 类型全名 → 实例
```

`Add(object instance)` 方法通过反射扫描实例的公共实例方法（`BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly`），并为每个方法同时注册**全限定名**与**短名称**两个别名，对应 Wails v3 中包名前缀的做法：

```csharp
var fullName  = "Wails.Net.Example.GreetingService.Greet";   // Namespace.ClassName.MethodName
var shortName = "GreetingService.Greet";                     // ClassName.MethodName
_boundMethods[fullName]  = boundMethod;  _boundByID[FNV1aHash(fullName)]  = boundMethod;
_boundMethods[shortName] = boundMethod;  _boundByID[FNV1aHash(shortName)] = boundMethod;
```

实例同时登记到 `_instancesByTypeName`，供源生成器调用器在调用时按类型全名反查目标实例。全局静态入口由 [`BindingRegistry`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BindingRegistry.cs) 提供（`BindingRegistry.Global`、`Initialize()`、`SetGlobal()`）。

### 2.2 FNV-1a 32 位哈希

绑定方法 ID 采用 FNV-1a 32 位哈希，**必须与 Go 版本 `fnv.New32a()` 完全一致**，保证跨语言互通。算法常量在 [`BindingManager.FNV1aHash`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) 与 [`BindingSourceGenerator.ComputeFnv1aHash`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 中独立实现（编译期与运行时各一份），任何修改都会破坏前后端协议：

```csharp
public static uint FNV1aHash(string text)
{
    const uint offsetBasis = 2166136261u;   // FNV-1a 32-bit offset basis
    const uint prime       = 16777619u;     // FNV-1a 32-bit prime
    var hash = offsetBasis;
    foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
    {
        hash ^= b;
        hash *= prime;
    }
    return hash;
}
```

### 2.3 特性标记

| 特性 | 目标 | 用途 |
|------|------|------|
| [`[Binding(Name = ...)]`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BindingAttribute.cs) | Method | 标记服务方法暴露给前端，自动使用 `ClassName.MethodName` 作为绑定名；`Name` 可指定别名 |
| [`[Command("name")]`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandAttribute.cs) | Method | 标记方法为命令，命令名为必填参数，前端通过自定义名称调用 |
| [`[DesktopCommand]`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/DesktopCommandAttribute.cs) | Class | 标记类为命令容器，类内公共非 void 方法自动按 `"类名.方法名".ToLowerInvariant()` 注册 |

`[Binding]` 与 `[Command]` 都会被 [`BindingSourceGenerator`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 扫描并生成强类型调用器（详见第 5 节）；`[DesktopCommand]` 则通过 [`CommandRegistry.RegisterFromAssembly`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandRegistry.cs) 在启动时程序集扫描，借助 `ActivatorUtilities.CreateInstance` 实现构造函数依赖注入。

### 2.4 方法过滤规则

`BindingManager.Add` 在扫描公共方法时跳过以下三类，避免暴露不该被前端调用的成员：

1. **服务生命周期方法**：`ServiceName`、`ServiceStartup`、`ServiceShutdown`（由 `IServiceStartup`/`IServiceShutdown` 管理生命周期，不应被前端调用）
2. **特殊方法**：`method.IsSpecialName == true`（属性 `get_`/`set_`、运算符 `op_`、事件 `add_`/`remove_`）
3. **`Object` 继承的方法**：`method.DeclaringType == typeof(object)`（避免 `ToString`、`GetType`、`Equals` 等被误暴露）

```csharp
private static readonly HashSet<string> ExcludedMethodNames = new(StringComparer.Ordinal)
{
    "ServiceName", "ServiceStartup", "ServiceShutdown"
};

if (ExcludedMethodNames.Contains(method.Name)) continue;
if (method.IsSpecialName)                       continue;
if (method.DeclaringType == typeof(object))    continue;
```

## 3. 命令调度系统

### 3.1 CommandRegistry — 命令注册表

[`CommandRegistry`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandRegistry.cs) 使用 `ConcurrentDictionary<string, CommandEntry>` 存储命令名到条目的映射，是 BindingManager 的现代替代方案。它支持三种注册方式：

```csharp
// 1. 显式注册：通过 MethodInfo
registry.Register("counter.increment", instance, method);

// 2. Minimal API 风格：通过 MapCommand 扩展方法（详见 MapCommandExtensions）
registry.MapCommand("greet", (string name) => $"Hello, {name}!");
registry.MapCommandAsync("saveFile", async (string path) => { ... });

// 3. 程序集扫描：注册所有 [DesktopCommand] 类
registry.RegisterFromAssembly(Assembly.GetExecutingAssembly(), services);
```

`Register` 在注册时立即调用 [`CommandInvokerCompiler.Compile`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs) 将 `MethodInfo` 编译为表达式树委托（详见第 6 节），缓存到 `CommandEntry.Invoker` 中。`CommandEntry.Method` 字段仅用于权限校验等元数据查询，不再参与实际调用。

### 3.2 MapCommand 扩展方法

[`MapCommandExtensions`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/MapCommandExtensions.cs) 借鉴 ASP.NET Core Minimal API 风格，提供基于委托的链式注册：

```csharp
public static CommandRegistry MapCommand(this CommandRegistry registry, string name, Delegate handler)
{
    var method = handler.Method;
    var target = handler.Target;
    registry.Register(name, target!, method);
    return registry;
}
```

重载覆盖 `Action`、`Func<TResult>`、`Func<T, TResult>`、`Func<Task>`、`Func<T, Task>`、`Func<Task<TResult>>` 等常见委托形态，使匿名 Lambda 可直接注册为命令。

### 3.3 CommandDispatcher — 调度流程

[`CommandDispatcher.DispatchAsync`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) 是命令调用的总入口，完整调度流程如下：

```
InvokeRequest 到达
    │
    ├─ 1. 创建超时/取消令牌（_defaultTimeout + CancellationTokenSource.CreateLinkedTokenSource）
    ├─ 2. _registry.Find(request.Method)  ─→ 未找到返回 InvokeResponse(success:false, "Command not found")
    ├─ 3. _permissionManager.ValidateCommand(entry.Method) ─→ 失败返回 "Permission denied"
    ├─ 4. 构建中间件管道（从尾到头反向包装，使第一个中间件最先执行）
    │       pipeline = ExecuteCommandAsync（终端）
    │       for i = _middlewares.Count - 1 downto 0:
    │           pipeline = () => middlewares[i].InvokeAsync(ctx, request, pipeline)
    ├─ 5. await pipeline()
    │       ├─ 优先用 entry.Invoker（编译后的强类型委托）调用
    │       └─ 回退到反射 MethodInfo.Invoke（编译失败时）
    └─ 6. 处理异步返回值（Task → await；Task<T> → 提取 Result）
```

终端处理器 `ExecuteCommandAsync` 实现参数绑定、调用和异步返回值处理；当 `entry.Invoker` 不为 null 时零反射调用，否则按反射路径逐参数构造：

```csharp
if (entry.Invoker is not null)
{
    result = entry.Invoker(entry.Instance, request.Parameters, ctx);  // 编译路径
}
else
{
    // 回退反射路径：按参数类型逐个绑定
    for (var i = 0; i < parameters.Length; i++)
    {
        if (parameters[i].ParameterType == typeof(ICommandContext))      args[i] = ctx;
        else if (parameters[i].ParameterType == typeof(CancellationToken)) args[i] = ctx.CancellationToken;
        else if (parameters[i].ParameterType == typeof(IServiceProvider)) args[i] = ctx.Services;
        else args[i] = request.Parameters.Deserialize(parameters[i].ParameterType);
    }
    result = entry.Method.Invoke(entry.Instance, args);
}
```

### 3.4 参数绑定策略

[`ICommandContext`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/ICommandContext.cs) 定义命令调用上下文，提供 `Services`、`WindowId`、`CancellationToken` 三项运行时环境信息（实现类 [`CommandContext`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandContext.cs) 为 `internal sealed`）。命令方法可声明以下**特殊参数类型**，由调度器自动注入，不暴露给前端：

| 参数类型 | 注入来源 |
|---------|---------|
| `ICommandContext` | 当前命令上下文实例（含 Services、WindowId、CancellationToken） |
| `CancellationToken` | `ctx.CancellationToken`（已合并超时令牌） |
| `IServiceProvider` | `ctx.Services`（用于在命令方法内解析其他服务） |
| 其他类型 | `JsonSerializer.Deserialize(request.Parameters, paramType)` |

绑定系统 [`BoundMethod.BuildParameters`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BoundMethod.cs) 也采用类似策略：若方法首参为 `CancellationToken`，则自动注入 `cancellationToken`，并跳过 JSON 参数填充；末位 `params` 数组按可变参数方式合并填充。

### 3.5 ICommandMiddleware 中间件管道

[`ICommandMiddleware`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/ICommandMiddleware.cs) 借鉴 ASP.NET Core 的中间件管道模式，可在命令执行前后插入横切逻辑：

```csharp
public interface ICommandMiddleware
{
    Task<InvokeResponse> InvokeAsync(
        ICommandContext context,
        InvokeRequest request,
        Func<Task<InvokeResponse>> next);
}
```

中间件按注册顺序组成管道：第一个中间件最先执行，可短路管道（不调用 `next` 直接返回响应）。典型应用场景包括日志记录、审计、限流、指标收集。`CommandDispatcher` 接收 `IReadOnlyList<ICommandMiddleware>` 在构造时一次性注入；`_middlewares ?? []` 保证未配置时为空列表。

### 3.6 命令超时机制

`CommandDispatcher` 构造时可传入 `TimeSpan? defaultTimeout`。当启用时，每次调度创建独立的 `CancellationTokenSource(timeout)`，并通过 `CreateLinkedTokenSource` 与外部取消令牌合并，使命令方法可通过 `CancellationToken` 感知超时：

```csharp
if (_defaultTimeout is { } timeout)
{
    timeoutCts = new CancellationTokenSource(timeout);
    if (originalToken == CancellationToken.None)
        effectiveToken = timeoutCts.Token;
    else
    {
        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(originalToken, timeoutCts.Token);
        effectiveToken = linkedCts.Token;
    }
}
```

超时触发时捕获 `OperationCanceledException` 并检查 `timeoutCts.IsCancellationRequested`，返回 `"Command timed out after {ms}ms"` 响应；`finally` 中释放 `linkedCts` 和 `timeoutCts` 避免句柄泄漏。绑定系统侧 [`BoundMethod.Timeout`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BoundMethod.cs) 提供按方法粒度的超时配置，通过 `Task.WhenAny(task, Task.Delay(...))` 实现类似效果。

## 4. 源代码生成器集成

### 4.1 BindingSourceGenerator — 编译期生成强类型调用器

[`BindingSourceGenerator`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 基于 `IIncrementalGenerator` 实现，扫描 `[Binding]` 与 `[Command]` 特性的方法，在编译期生成调用代码：

```csharp
var bindingMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
    "Wails.Net.Application.Bindings.BindingAttribute",
    predicate: static (node, _) => node is MethodDeclarationSyntax,
    transform: static (ctx, _) => new MethodMarker((IMethodSymbol)ctx.TargetSymbol, "Binding", ...));

var commandMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
    "Wails.Net.Application.Commands.CommandAttribute", ...);
```

跳过抽象方法、泛型方法、静态方法，为每个有效方法生成一段形如下方的调用器：

```csharp
public static object? GreetingService_Greet(object instance, JsonElement[] args, CancellationToken cancellationToken)
{
    var svc = (global::MyApp.GreetingService)instance;
    var p0 = args.Length > 0 && !args[0].ValueKind.Equals(JsonValueKind.Null)
        ? args[0].Deserialize<string>(JsonOptions.DefaultSerializerOptions)!
        : default(string)!;
    return svc.Greet(p0);   // 直接方法调用，无 MethodInfo.Invoke 开销
}
```

### 4.2 GeneratedBindingRegistry — [ModuleInitializer] 自动注册

生成器输出的 `WailsGeneratedBindings.g.cs` 中包含一个用 `[ModuleInitializer]` 标记的 `Register` 方法，在程序集加载时自动将所有调用器登记到 [`GeneratedBindingRegistry`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)：

```csharp
[global::System.Runtime.CompilerServices.ModuleInitializer]
public static void Register()
{
    GeneratedBindingRegistry.Register(
        "MyApp.GreetingService.Greet", GreetingService_Greet, "MyApp.GreetingService");
    GeneratedBindingsMetadata.Register(new BoundMethodInfo(...));
}
```

`GeneratedBindingRegistry` 内部维护三张字典：

- `_invokers`：方法全名 → `GeneratedInvoker` 委托
- `_methodToTypeName`：方法全名 → 类型全名（用于在 `BindingManager._instancesByTypeName` 中查找实例）
- `_typeMethods`：类型全名 → 方法名列表（用于快速判断某类型是否有生成器调用器）

### 4.3 替代运行时反射，支持 AOT 裁剪

`BindingManager.Call(string fullName, ...)` 的调用路径优先级为：**源生成器调用器 → 反射 BoundMethod**。源生成器调用器直接对实例进行强类型方法调用（如 `svc.Greet(p0)`），完全跳过 `MethodInfo.Invoke` 的反射开销，并为 .NET Native AOT 裁剪提供了所需的可静态分析调用图。同时 [`GeneratedBindingsMetadata`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs) 提供参数/返回类型的 TypeScript 字符串，供 `TypeScriptGenerator` 生成前端类型声明文件，**彻底消除运行时反射分析**。

## 5. 表达式树编译

### 5.1 CommandInvokerCompiler — MethodInfo 到委托

[`CommandInvokerCompiler`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs) 借鉴 ASP.NET Core 的表达式树编译模式，在命令注册时一次性将 `MethodInfo` 编译为 `CompiledCommandInvoker` 委托：

```csharp
public delegate object? CompiledCommandInvoker(
    object instance, JsonElement parameters, ICommandContext? ctx);
```

### 5.2 表达式构建逻辑

`CompileCore` 方法为每个参数按类型生成对应的绑定表达式：

```csharp
if (paramType == typeof(ICommandContext))
    args[i] = ctxParam;                                          // 直接注入上下文
else if (paramType == typeof(CancellationToken))
    args[i] = Expression.Property(ctxParam, ctxCancellationTokenProp);
else if (paramType == typeof(IServiceProvider))
    args[i] = Expression.Property(ctxParam, ctxServicesProp);
else
{
    // JsonSerializer.Deserialize(parameters.GetRawText(), paramType, options)
    var getRawTextCall = Expression.Call(parametersParam, getRawTextMethod);
    args[i] = Expression.Call(deserializeMethod, getRawTextCall,
        Expression.Constant(paramType, typeof(Type)),
        Expression.Constant(jsonOptions, typeof(JsonSerializerOptions)));
    args[i] = Expression.Convert(args[i], paramType);
}
```

最终构建 `(instance, parameters, ctx) => (T)instance.Method(args)` 形式的 `Expression<Lambda<CompiledCommandInvoker>>`，调用 `Compile()` 生成 IL 委托。

### 5.3 编译缓存

```csharp
private static readonly ConcurrentDictionary<MethodInfo, CompiledCommandInvoker?> _cache = new();

public static CompiledCommandInvoker? Compile(MethodInfo method)
    => _cache.GetOrAdd(method, static (m, options) => CompileCore(m, options), _jsonOptions);
```

`ConcurrentDictionary` 保证同一 `MethodInfo` 只编译一次，且线程安全；若方法参数类型无法绑定则缓存 `null`，回退到反射路径。`CommandEntry.Invoker` 持有编译后的委托，运行时直接调用，**零反射开销**。

## 6. 错误处理

[`CallError`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Errors/CallError.cs) 是结构化错误对象，包含 `Message`、`Cause`、`Kind` 三字段，通过 `ToJson()` 序列化为字典返回给前端：

```csharp
public Dictionary<string, object?> ToJson() => new()
{
    ["message"] = Message,
    ["cause"]   = Cause,
    ["kind"]    = Kind.ToString()
};
```

[`CallErrorKind`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Errors/CallError.cs) 对应 JavaScript 运行时错误类型：

| 值 | 含义 | 典型场景 |
|----|------|---------|
| `ReferenceError` | 引用错误：访问未定义的方法 | `BindingManager.Call` 未找到 ID/名称 |
| `TypeError` | 类型错误：参数类型不匹配 | `JsonException`、`ArgumentException` |
| `RuntimeError` | 运行时错误：执行中其他异常 | `InvalidOperationException`、超时 |

### 6.1 TargetInvocationException 解包

反射调用（`MethodInfo.Invoke`）抛出的 `TargetInvocationException` 会包装真实异常，[`BoundMethod.Call`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BoundMethod.cs) 与 `CommandDispatcher.ExecuteCommandAsync` 都使用 `when` 过滤器解包并按异常类型映射错误类型：

```csharp
catch (TargetInvocationException ex) when (ex.InnerException is not null)
{
    return ErrorResult(ex.InnerException.Message,
        ex.InnerException is ArgumentException
            ? Errors.CallErrorKind.TypeError
            : Errors.CallErrorKind.RuntimeError);
}
catch (JsonException ex)        => ErrorResult($"参数反序列化失败: {ex.Message}", CallErrorKind.TypeError);
catch (ArgumentException ex)    => ErrorResult(ex.Message, CallErrorKind.TypeError);
catch (Exception ex)            => ErrorResult(ex.Message, CallErrorKind.RuntimeError);
```

### 6.2 结果字典统一格式

无论绑定路径还是命令路径，返回前端的字典都遵循 `{ "result": object?, "error": CallError.ToJson() | null }` 结构，便于前端统一处理。`CommandDispatcher` 额外封装为 `InvokeResponse(request.Id, success, result, error)`，由调用方决定如何包装为最终响应。

## 7. 消息路由

[`MessageProcessor`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 是传输层与绑定/命令系统之间的桥梁，对应 Wails v3 Go 版本 `messageprocessor.go`。它接收前端 JSON 消息，按 `type` 字段路由：

```
message.type             → 路由目标
─────────────────────────────────────────────────────────
"call"                   → ProcessCallAsync  → BindingManager → (回退) CommandDispatcher
"event"                  → ProcessEvent      → EventProcessor.Emit
"query"                 → ProcessQuery      → 查询 bindings/events 列表
"window" / "window.*"    → ProcessWindowAsync → (优先) CommandDispatcher → (回退) DispatchWindowAction
"drag"                   → ProcessDrag       → 转为 wails:drag 事件
"contextmenu"            → ProcessContextMenu → 转为 wails:contextmenu 事件
其他命名空间（如 "notification.show"） → ProcessCommandFallbackAsync → CommandDispatcher
```

### 7.1 call 消息的双轨路由

`ProcessCallAsync` 是绑定/命令双轨架构的关键节点。`CallPayload` 可携带 `id`（FNV-1a 哈希）或 `name`（方法全名），按以下顺序查找：

```csharp
if (callPayload.Id is not null)
    result = await _bindings.Call(callPayload.Id.Value, args, _cts.Token);
else if (!string.IsNullOrEmpty(callPayload.Name))
{
    result = await _bindings.Call(callPayload.Name!, args, _cts.Token);

    // BindingManager 未找到方法时回退到 CommandDispatcher（如 "counter.increment"）
    if (IsNotFoundResult(result, callPayload.Name!) && _commands is not null)
    {
        var commandResult = await TryDispatchCommandAsync(callPayload.Name!, args, message.WindowId);
        if (commandResult is not null) result = commandResult;
    }
}
```

`IsNotFoundResult` 通过检查错误字典的 `message` 字段是否包含"未找到"和原方法名来判断是否回退（直接读字典而非 JSON 序列化，避免非 ASCII 字符转义导致匹配失败）。

### 7.2 未识别命名空间的命令回退

未识别的命名空间（如 `notification.show`、`tray.setIcon`、`application.hide`）通过 `_ => ProcessCommandFallbackAsync(message)` 兜底，借鉴 Tauri v2 的"核心即插件"哲学：所有系统原生操作以插件命令形式注册到 `CommandDispatcher`，前端 `wails.<namespace>.<method>()` 调用通过消息类型本身作为命令名派发。

```csharp
private async Task<ResponseMessage?> ProcessCommandFallbackAsync(Message message)
{
    if (_commands is null) return null;
    var request = new InvokeRequest(Guid.NewGuid(), message.Type, message.Payload);
    var ctx = _services is not null
        ? new CommandContext(_services, message.WindowId, _cts.Token)
        : null;
    var response = await _commands.DispatchAsync(request, ctx);
    if (!response.Success) return null;   // 向后兼容
    return new ResponseMessage { Id = message.Id, Type = MessageTypes.Response,
                                  Result = new() { ["result"] = response.Result, ["error"] = null } };
}
```

### 7.3 窗口命令的双路径

`ProcessWindowAsync` 同样采用双路径设计：优先将 `window.<action>` 作为命令名派发到 `CommandDispatcher`（命中 `WindowPlugin` 注册的命令）；若命令未找到则回退到 `DispatchWindowAction` 的硬编码 `switch` 分发，保持向后兼容。两条路径都会同步广播 `wails:window:<action>` 事件，便于其他监听者感知窗口状态变化。

---

**相关文件**：
- [Bindings.cs (BindingManager)](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) · [BindingAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BindingAttribute.cs) · [BindingRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BindingRegistry.cs) · [BoundMethod.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BoundMethod.cs) · [GeneratedBindingRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs) · [GeneratedBindingsMetadata.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs)
- [CommandAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandAttribute.cs) · [CommandDispatcher.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) · [CommandInvokerCompiler.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs) · [CommandRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandRegistry.cs) · [ICommandContext.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/ICommandContext.cs) · [ICommandMiddleware.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/ICommandMiddleware.cs) · [MapCommandExtensions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/MapCommandExtensions.cs) · [DesktopCommandAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/DesktopCommandAttribute.cs) · [CommandContext.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandContext.cs) · [InvokeRequest.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/InvokeRequest.cs)
- [BindingSourceGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) · [MessageProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) · [CallError.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Errors/CallError.cs)
