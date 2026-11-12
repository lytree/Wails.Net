# 绑定系统与命令调度

## 1. 概述

Wails.Net 在前端 IPC 调用与后端 C# 方法之间采用**双轨调用架构**，同时维护两条互补的调用通路：

| 调用通路 | 核心组件 | 注册方式 | 调用形式 | 性能特征 |
|---------|---------|---------|---------|---------|
| **绑定系统** | [`BindingManager`](../../src/Wails.Net.Application/Bindings/Bindings.cs) | `[Binding]` 特性 + 源生成器 | `wails.call("ClassName.MethodName", args)` | 强类型调用器，运行时零反射 |
| **命令调度** | [`CommandDispatcher`](../../src/Wails.Net.Application/Commands/CommandDispatcher.cs) | `[Command]` 特性 + 源生成器 / `MapCommand` | `wails.call("custom.command.name", args)` | 编译期闭包，运行时零反射 |

绑定系统直接移植自 Wails v3 Go 版本的 `bindings.go`，使用 `Namespace.ClassName.MethodName` 全限定名作为键、并以 FNV-1a 32 位哈希生成 ID，确保跨语言兼容；命令调度系统借鉴 ASP.NET Core Minimal API 的管道设计，并采用「核心即插件」的插件化设计，以命令名（如 `counter.increment`、`window.setTitle`）作为分发依据，并内建中间件管道、权限校验、超时取消等横切能力。传输层的 [`MessageProcessor`](../../src/Wails.Net.Application/Transport/MessageProcessor.cs) 在 `call` 消息中先查询 BindingManager，未命中再回退到 CommandDispatcher，使两条通路对前端完全透明。

```csharp
// 前端发起的 JSON 消息
{ "id": "1", "type": "call",
  "payload": { "name": "GreetingService.Greet", "args": ["World"] } }
//                  ↑ 命中 BindingManager（生成器调用器）

{ "id": "2", "type": "call",
  "payload": { "name": "counter.increment", "args": [{"by": 1}] } }
//                  ↑ 命中 CommandDispatcher（[Command] 注册）
```

### 1.1 职责边界与推荐路径（B-4 审计项）

两条调用通路可以并存，但**新代码必须按职责选择**，避免贡献者误用：

| 场景 | 推荐路径 | 说明 |
|------|---------|------|
| **应用业务 API**（登录、订单、业务查询等） | `[Binding]` + 源生成器 | 接近 Wails v3 的绑定体验，`ClassName.MethodName` 全限定名，TypeScript 类型由生成器输出 |
| **系统/插件能力**（文件、窗口、剪贴板、通知等） | `IPlugin` + `MapCommand` + capability | 采用自研能力模型，命令名（如 `fs.readFile`），必须声明权限 |
| **新增内置能力** | 一律通过 `CommandDispatcher` 注册 | 禁止绕过 `CommandDispatcher` 权限链直接暴露敏感原生操作 |

判定要点：

1. **方法归属**：业务方法属于"应用自身的服务"，放在 `[Binding]` 服务类；系统能力属于"框架/插件提供的通用能力"，走 `IPlugin.MapCommand`。
2. **权限要求**：需要 capability / permission / scope 保护的能力**必须**走命令通路——只有 `CommandDispatcher` 会执行 `PermissionManager` 权限链与 Scope 校验；`[Binding]` 绑定通路不参与权限校验。
3. **前端调用透明**：两条通路对前端完全透明（`MessageProcessor` 先查绑定、未命中再回退命令），但**建议前端按语义调用**：业务 API 用 `wails.call("XxxService.Method")`，插件能力用对应插件包的 TS wrapper。

### 1.1.1 命令命名与 capability 约定（B-4 审计项增补）

**命令别名命名规范**：

- 命令名采用 `plugin.action` 点分命名空间，如 `fs.readFile`、`window.setTitle`、`notification.show`；`MessageProcessor` 按首段路由，未识别命名空间统一走 `CommandDispatcher` 兜底。
- 插件命令必须以插件名（或能力前缀）开头，避免与业务 `[Binding]` 全限定名（`XxxService.Method`）混淆；同一插件内禁止注册重名命令。
- 新命令默认使用 `MapCommand` 的强类型重载（编译期构建调用器）；`[Command]` 特性 + 源生成器是等价的编译期路径，运行时禁止 `Type.GetMethod` / `MethodInfo.Invoke` 发现调用。

**capability 与 `[RequireCapability]` 来源优先级**：

| 声明来源 | 位置 | 优先级 |
|---------|------|--------|
| `[RequireCapability]` / `[Command]` 特性 | 命令容器类或方法上 | 注册期硬编码，最高 |
| `MapCommand(..., params string[] requiredCapabilities)` | 插件 `Configure` 中 | 与特性同源，编译期确定 |
| capabilities/*.json 声明 | 运行时加载 | 授权时与注册要求取交集（都必须满足） |
| `PermissionManager.Grant/Deny` | 运行时 | deny 始终优先于 grant |

命令调度时 `CommandDispatcher` 会同时校验注册要求与 capability 授权；manifest 一致性检查（`FindUncoveredCapabilities`）保证注册要求**至少被一个 capability 覆盖**，未覆盖的命令必须显式声明匿名（`requiredCapabilities` 为空）。

**禁止新增生产反射绑定路径的检查清单**：

- [ ] 新命令是否通过 `MapCommand` 强类型重载 / `[Command]` + 源生成器注册，而非 `Type.GetMethods` 扫描？
- [ ] 是否引入 `Activator.CreateInstance` / `MethodInfo.Invoke` / `Delegate.CreateDelegate`（生产代码）？
- [ ] 敏感能力是否绕过 `CommandDispatcher` 直接暴露原生操作？
- [ ] `using System.Reflection;` 是否仅出现在白名单场景（§3.4.3）或测试代码？
- [ ] 新增命令是否同步更新 manifest / 插件文档命令表 / 前端 TS wrapper？

### 1.2 机器可读清单（P1-2 审计项）

命令与 capability 的一致性由 [`ManifestGenerator`](../../src/Wails.Net.Application/Manifests/ManifestGenerator.cs) 提供机器可读校验：

- `GenerateCommandsManifest(registry)` → `commands.manifest.json`（命令名 + `requiredCapabilities` + `anonymous` 标记 + `parameters` 参数 TS 类型声明）
- `GenerateCapabilitiesManifest(capabilities)` → `capabilities.manifest.json`（identifier + permissions + windows/remote/platforms）
- `FindUncoveredCapabilities(registry, capabilities)` → 未覆盖清单（**敏感命令的能力必须被至少一个 capability 覆盖**；`!` 前缀显式拒绝不计入覆盖；匿名命令即 `requiredCapabilities` 为空，不参与校验）

**命令签名元数据（P2-1）**：`CommandRegistry` 的命令条目携带业务参数类型（由 `MapCommand` 重载在注册时通过 `ExtractBusinessParameterTypes` 提取，排除 `ICommandContext`/`CancellationToken`/`IServiceProvider` 等运行时注入参数），manifest 将其映射为 TypeScript 类型声明（`TypeToTypeScript`：string→`string`、数值→`number`、bool→`boolean`、枚举/Guid/DateTime→`string`、集合→数组、可空剥壳）。

**TypeScript wrapper 生成（P2-1）**：[`CommandTypeScriptGenerator`](../../src/Wails.Net.Generator/CommandTypeScriptGenerator.cs) 消费命令清单生成 `commands.ts` 强类型调用封装（每个命令一个函数，基于 `@wails-net/runtime` 的 `wails.call`），以清单为唯一来源消除前后端 API 漂移。该清单与生成器同时是插件文档命令表、CLI `plugin inspect` 的统一数据源。

**P2-1 收尾（CLI 闭环）**：应用侧可通过 `DesktopApplication.GenerateCommandsManifest()` / `WriteCommandsManifestToDirectory()` 导出 `commands.manifest.json`（清单文件名常量 `ManifestGenerator.ManifestFileName`）；CLI 侧执行：

```bash
# 应用构建/运行期导出清单后（文件在 bindings/commands.manifest.json）：
wails generate --commands-manifest bindings/commands.manifest.json --output src/frontend/src
```

生成 `src/frontend/src/commands.ts`（文件名 `CommandTypeScriptGenerator.TypeScriptFileName`）。测试示例：

```csharp
var uncovered = ManifestGenerator.FindUncoveredCapabilities(registry, capabilities);
Assert.That(uncovered, Is.Empty); // 所有敏感命令必须被 capability 覆盖
```

## 2. 绑定系统

### 2.1 BindingManager — 实例注册与调用器查找

[`BindingManager`](../../src/Wails.Net.Application/Bindings/Bindings.cs) 是绑定系统的核心，对应 Wails v3 Go 版本 `bindings.go` 中的 `Bindings` 结构。去反射后它只维护**一张**内部字典：

```csharp
// 类型全名 → 实例（供源生成器生成的调用器在调用时按类型全名反查目标实例）
private readonly Dictionary<string, object> _instancesByTypeName = new(StringComparer.Ordinal);
```

> **与早期实现的区别**：`BoundMethod` 类型与 `_boundMethods`、`_boundByID` 两张表已随「去反射改源生成器」（commit `0fd6144`）删除。方法名 → 调用器的映射改由源生成器在编译期写入 [`GeneratedBindingRegistry`](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)，`BindingManager` 不再枚举实例的公共方法（AGENTS.md §3.4 禁令）。

`RegisterInstance(object instance)`（兼容别名 `Add`）只做一件事：按 `Namespace.ClassName` 登记实例，方法枚举与调用器生成全部交给编译期：

```csharp
var typeFullName = "Wails.Net.Example.GreetingService";   // Namespace.ClassName
_instancesByTypeName[typeFullName] = instance;
```

方法名别名的注册同样在编译期完成：生成的 `[ModuleInitializer]` 为每个方法注册**全限定名**、**短名称**、**自定义名**三类键（见 §4.2），Wails v3 的「包名前缀 + 短名」双键语义因此保持不变，只是搬到了生成代码里。按 ID 调用时，`Call(uint id, ...)` 在 `GeneratedBindingsMetadata.Methods` 中反查全名，再委托给字符串重载。全局静态入口由 [`BindingRegistry`](../../src/Wails.Net.Application/Bindings/BindingRegistry.cs) 提供（`BindingRegistry.Global`、`Initialize()`、`SetGlobal()`）。

### 2.2 FNV-1a 32 位哈希

绑定方法 ID 采用 FNV-1a 32 位哈希，**必须与 Go 版本 `fnv.New32a()` 完全一致**，保证跨语言互通。算法常量在 [`BindingManager.FNV1aHash`](../../src/Wails.Net.Application/Bindings/Bindings.cs) 与 [`BindingSourceGenerator.ComputeFnv1aHash`](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 中独立实现（编译期与运行时各一份），任何修改都会破坏前后端协议：

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
| [`[Binding(Name = ...)]`](../../src/Wails.Net.Application/Bindings/BindingAttribute.cs) | Method | 标记服务方法暴露给前端，自动使用 `ClassName.MethodName` 作为绑定名；`Name` 可指定别名 |
| [`[Command("name")]`](../../src/Wails.Net.Application/Commands/CommandAttribute.cs) | Method | 标记方法为命令，命令名为必填参数，前端通过自定义名称调用 |
| [`[DesktopCommand]`](../../src/Wails.Net.Application/Commands/DesktopCommandAttribute.cs) | Class | 标记类为命令容器，类内公共非 void 方法自动按 `"类名.方法名".ToLowerInvariant()` 注册 |

`[Binding]` 与 `[Command]` 都会被 [`BindingSourceGenerator`](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 扫描并生成强类型调用器（详见第 4 节）；`[DesktopCommand]` 的程序集扫描路径已停用，见 §2.4。

### 2.4 暴露边界由特性决定

早期实现靠扫描公共方法并手工排除三类成员（`ServiceName`/`ServiceStartup`/`ServiceShutdown`、`IsSpecialName`、`DeclaringType == typeof(object)`）。去反射后**不再有运行时枚举**，暴露边界改由编译期决定：

| 边界 | 承载者 |
|------|--------|
| 哪些方法可被前端调用 | 只有标记 `[Binding]` / `[Command]` 的方法才会被 [`BindingSourceGenerator`](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 生成调用器 |
| 哪些成员永不暴露 | 未标记方法、属性访问器、运算符、事件访问器、`object` 继承成员——生成器根本不扫描 |
| 生命周期方法 | `ServiceStartup` / `ServiceShutdown`（`IServiceStartup` / `IServiceShutdown`）不在特性白名单内，天然不可调用 |

生成器自身的跳过规则是三条：**抽象方法、泛型方法、静态方法**（`methodSymbol.IsAbstract || IsGenericMethod || IsStatic`）。

> `[DesktopCommand]` 的程序集扫描（`CommandRegistry.RegisterFromAssembly` / `RegisterCommands`）已随反射实现移除而停用，调用会直接抛 `NotSupportedException`；需要批量注册时请改用 `[Command]` 特性，由源生成器在编译期完成。

## 3. 命令调度系统

### 3.1 CommandRegistry — 命令注册表

[`CommandRegistry`](../../src/Wails.Net.Application/Commands/CommandRegistry.cs) 使用 `ConcurrentDictionary<string, CommandEntry>` 存储命令名到条目的映射，是 BindingManager 的现代替代方案。注册入口只接受**已构建好的调用器委托**（[`CompiledCommandInvoker`](../../src/Wails.Net.Application/Commands/CompiledCommandInvoker.cs)），不再接收 `MethodInfo`：

```csharp
// 1. 显式注册：直接给出调用器（可附带业务参数类型，供 manifest 生成）
registry.Register("counter.increment", (instance, parameters, ctx) => ..., new[] { typeof(int) });

// 2. Minimal API 风格：MapCommand 泛型重载在注册点构造闭包（详见 §5.2）
registry.MapCommand("greet", (string name) => $"Hello, {name}!");
registry.MapCommandAsync("saveFile", async (string path) => { ... });

// 3. 特性驱动：编写 [Command] 方法，由源生成器在 [ModuleInitializer] 中自动注册
```

`CommandEntry` 只持有 `Invoker` 委托，外加 `ParameterTypes`、`RequiredCapabilities`、`Security` 三类元数据，**没有 `Method` 字段**——`MethodInfo` 既不参与调用，也不再用于权限校验等元数据查询（能力与 Scope 元数据来自 `GeneratedBindingRegistry` 与 `CommandSecurity`）。

### 3.2 MapCommand 扩展方法

[`MapCommandExtensions`](../../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) 借鉴 ASP.NET Core Minimal API 风格，每个重载都以**强类型泛型签名**在注册点构造闭包，闭包捕获 handler，参数在编译期已知的 `T` 上反序列化：

```csharp
public static CommandRegistry MapCommand<T>(this CommandRegistry registry, string name, Action<T> handler)
{
    var options = _jsonOptions;
    return registry.Register(name, (instance, parameters, ctx) =>
    {
        var arg = Deserialize<T>(parameters, options);   // 单业务参数：整体反序列化
        handler(arg);
        return Task.FromResult<object?>(null);
    }, new[] { typeof(T) });                             // 参数类型写入 CommandEntry 元数据
}
```

重载覆盖 `Action`、`Action<T>`、`Action<ICommandContext, T>`、`Func<TResult>`、`Func<T, TResult>`、`Func<Task>`、`Func<T, Task<TResult>>` 以及 2–4 个业务参数的组合，使匿名 Lambda 可直接注册为命令。参数解析约定见 §5.2。

### 3.3 CommandDispatcher — 调度流程

[`CommandDispatcher.DispatchAsync`](../../src/Wails.Net.Application/Commands/CommandDispatcher.cs) 是命令调用的总入口，完整调度流程如下：

```
InvokeRequest 到达
    │
    ├─ 1. 创建超时/取消令牌（_defaultTimeout + CancellationTokenSource.CreateLinkedTokenSource）
    ├─ 2. _registry.Find(request.Method)  ─→ 未找到返回 InvokeResponse(success:false, "Command not found")
    ├─ 3. Strict 模式：无安全声明的命令直接拒绝（Compatibility 模式跳过）
    ├─ 4. 能力校验：RequiredCapabilities + Security.RequiredCapabilities + 生成期能力列表（三来源去重）
    │       └─ Scope 校验：Security.ScopeMappings 优先，其次生成期 ScopeExtractor 委托
    ├─ 5. 构建中间件管道（从尾到头反向包装，使第一个中间件最先执行）
    │       pipeline = ExecuteCommandAsync（终端）
    │       for i = _middlewares.Count - 1 downto 0:
    │           pipeline = () => middlewares[i].InvokeAsync(ctx, request, pipeline)
    ├─ 6. await pipeline()
    │       └─ 终端调用 entry.Invoker（编译期构建的强类型委托），Invoker 为 null 直接报错，无反射回退
    └─ 7. 异常处理：OperationCanceledException 区分超时/取消，其余异常按 ex.Message 返回
```

终端处理器 `ExecuteCommandAsync` 只做一件事——`await` 调用器并包装结果。**不存在反射回退分支**：

```csharp
if (entry.Invoker is null)
{
    throw new InvalidOperationException(
        $"命令 '{request.Method}' 未注册调用器（Invoker 为 null），请检查 MapCommand 注册代码");
}

// CompiledCommandInvoker 统一返回 Task<object?>：闭包内部已 await 异步方法并装箱结果
var returnValue = await entry.Invoker(entry.Instance, request.Parameters, ctx).ConfigureAwait(false);
return new InvokeResponse(request.Id, true, returnValue, null);
```

### 3.4 参数绑定策略

[`ICommandContext`](../../src/Wails.Net.Application/Commands/ICommandContext.cs) 定义命令调用上下文，提供 `Services`、`WindowId`、`CancellationToken` 三项运行时环境信息（实现类 [`CommandContext`](../../src/Wails.Net.Application/Commands/CommandContext.cs) 为 `internal sealed`）。命令方法可声明以下**特殊参数类型**，由调度器自动注入，不暴露给前端：

| 参数类型 | 注入来源 |
|---------|---------|
| `ICommandContext` | 当前命令上下文实例（含 Services、WindowId、CancellationToken） |
| `CancellationToken` | `ctx.CancellationToken`（已合并超时令牌） |
| `IServiceProvider` | `ctx.Services`（用于在命令方法内解析其他服务） |
| 其他类型 | `JsonSerializer.Deserialize(request.Parameters, paramType)` |

绑定系统侧的参数注入同样在**编译期**完成（见 §4.1 生成的调用器）：`CancellationToken` 参数直接接收调用器入参 `cancellationToken`，其余参数按位置从 `JsonElement[] args` 反序列化，越界或值为 `null` 时取类型默认值。需要注意 `params` 参数的行为变化：不再「按可变参数合并填充」，而是按单个 JSON 数组反序列化，前端需把 `params` 传成一个数组。

### 3.5 ICommandMiddleware 中间件管道

[`ICommandMiddleware`](../../src/Wails.Net.Application/Commands/ICommandMiddleware.cs) 借鉴 ASP.NET Core 的中间件管道模式，可在命令执行前后插入横切逻辑：

```csharp
public interface ICommandMiddleware
{
    Task<InvokeResponse> InvokeAsync(
        ICommandContext context,
        InvokeRequest request,
        Func<Task<InvokeResponse>> next);
}
```

中间件按注册顺序组成管道：第一个中间件最先执行，可短路管道（不调用 `next` 直接返回响应）。典型应用场景包括日志记录、审计、限流、指标收集。

**DI 注入闭环（A-2 审计项）**：`DesktopApplicationBuilder.Build()` 会从 DI 容器收集全部 `ICommandMiddleware` 一次性注入 `CommandDispatcher`（未注册时为 `[]`）。应用侧以 ASP.NET Core 风格注册中间件：

```csharp
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// Fluent API（推荐）
builder.UseCommandMiddleware<AuditLogMiddleware>();
builder.UseCommandMiddleware<RateLimitMiddleware>();

// 或直接注册到 DI
builder.Services.AddSingleton<ICommandMiddleware, AuditLogMiddleware>();
```

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

超时触发时捕获 `OperationCanceledException` 并检查 `timeoutCts.IsCancellationRequested`，返回 `"Command timed out after {ms}ms"` 响应；`finally` 中释放 `linkedCts` 和 `timeoutCts` 避免句柄泄漏。注意超时只有**命令级**一个配置点：绑定系统侧没有按方法粒度的超时（原 `BoundMethod.Timeout` 已随反射路径删除），绑定调用的超时由调用方传入的 `CancellationToken` 决定。

## 4. 源代码生成器集成

### 4.1 BindingSourceGenerator — 编译期生成强类型调用器

[`BindingSourceGenerator`](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 基于 `IIncrementalGenerator` 实现，扫描 `[Binding]` 与 `[Command]` 特性的方法，在编译期生成调用代码：

```csharp
var bindingMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
    "Wails.Net.Application.Bindings.BindingAttribute",
    predicate: static (node, _) => node is MethodDeclarationSyntax,
    transform: static (ctx, _) => new MethodMarker((IMethodSymbol)ctx.TargetSymbol, "Binding", ...));

var commandMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
    "Wails.Net.Application.Commands.CommandAttribute", ...);
```

跳过抽象方法、泛型方法、静态方法，为每个有效方法生成一段调用器。同步方法与异步方法生成的形态不同（同步方法不加 `async`，避免 CS1998）：

```csharp
// 异步方法 Task<string>：await 后直接返回结果（装箱由编译器完成）
public static async System.Threading.Tasks.Task<object?> GreetingService_GreetAsync(
    object instance, JsonElement[] args, CancellationToken cancellationToken)
{
    var svc = (global::MyApp.GreetingService)instance;
    var p0 = args.Length > 0 && !args[0].ValueKind.Equals(JsonValueKind.Null)
        ? args[0].Deserialize<string>(JsonOptions.DefaultSerializerOptions)!
        : default(string)!;
    var result = await svc.GreetAsync(p0).ConfigureAwait(false);   // 直接方法调用，无 MethodInfo.Invoke 开销
    return result;
}

// 同步方法 string：用 Task.FromResult 包装
public static System.Threading.Tasks.Task<object?> GreetingService_Greet(
    object instance, JsonElement[] args, CancellationToken cancellationToken)
{
    var svc = (global::MyApp.GreetingService)instance;
    var p0 = /* 同上 */;
    var result = svc.Greet(p0);
    return System.Threading.Tasks.Task.FromResult<object?>(result);
}
```

调用器统一返回 `Task<object?>`，`CancellationToken` 参数按位置注入入参 `cancellationToken`，其余参数按顺序消费 `args`。

### 4.2 GeneratedBindingRegistry — [ModuleInitializer] 自动注册

生成器输出的 `WailsGeneratedBindings.g.cs` 中包含一个用 `[ModuleInitializer]` 标记的 `Register` 方法，在程序集加载时自动将所有调用器登记到 [`GeneratedBindingRegistry`](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)：

```csharp
[global::System.Runtime.CompilerServices.ModuleInitializer]
public static void Register()
{
    GeneratedBindingRegistry.Register(
        "MyApp.GreetingService.Greet", GreetingService_Greet, "MyApp.GreetingService");
    GeneratedBindingsMetadata.Register(new BoundMethodInfo(...));
}
```

`GeneratedBindingRegistry` 内部维护六张静态结构：

- `_invokers`：方法全名 → `GeneratedInvoker` 委托
- `_methodToTypeName`：方法全名 → 类型全名（用于在 `BindingManager._instancesByTypeName` 中查找实例）
- `_typeMethods`：类型全名 → 方法名列表（用于快速判断某类型是否有生成器调用器）
- `_methodCapabilities`：方法全名 → 所需能力列表（`[RequireCapability]` 编译期提取）
- `_methodScopeExtractors`：方法全名 → Scope 提取器委托（`[ScopeParameter]` 编译期生成）
- `_ambiguousKeys`：被判为歧义的名称键（`ConcurrentDictionary`，fail-closed 拒绝注册与解析）

### 4.3 替代运行时反射，支持 AOT 裁剪

`BindingManager.Call(string fullName, ...)` 只有一条调用路径：**源生成器调用器**（`GeneratedBindingRegistry.TryGetInvoker`）。命中后按类型全名取出实例、直接强类型调用（如 `svc.Greet(p0)`），完全跳过 `MethodInfo.Invoke` 的反射开销，并为 .NET Native AOT 裁剪提供了可静态分析的调用图。未命中时**不再回退反射**：

- 若该名称由多个类型注册（跨程序集同名类、重复命令名），注册表把它标记为**歧义键**并 fail-closed，返回携带冲突类型说明的 `ReferenceError`；
- 否则返回 `ReferenceError`（"未找到名为 … 的绑定方法"），由 `MessageProcessor` / `InvocationGateway` 回退到 `CommandDispatcher` 走命令路径。

同时 [`GeneratedBindingsMetadata`](../../src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs) 提供参数/返回类型的 TypeScript 字符串，供 `TypeScriptGenerator` 生成前端类型声明文件，**彻底消除运行时反射分析**。

## 5. 调用器的两种构建方式

去反射后不存在「把 `MethodInfo` 编译成表达式树」的环节。调用器只有两种来源，都在编译期或注册点确定，产出同一个委托类型。

### 5.1 源生成器调用器（`[Binding]` / `[Command]`）

见 §4.1：为每个特性方法生成 `public static Task<object?> X(object instance, JsonElement[] args, CancellationToken ct)`，方法体是强类型直接调用。

### 5.2 MapCommand 泛型闭包（运行时注册的命令）

[`MapCommandExtensions`](../../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) 的每个重载都以强类型泛型签名在注册点构造闭包，闭包捕获 handler，参数在编译期已知的 `T` 上反序列化：

| 业务参数个数 | 参数解析 |
|--------------|----------|
| 0 | 忽略 `parameters` |
| 1 | `parameters` 整体反序列化为 `T` |
| ≥2 | 视 `parameters` 为 JSON 数组，按位置反序列化 `T1..Tn`，缺位取默认值 |

`JsonSerializerOptions` 固定为 `JsonSerializerDefaults.Web` 加 `JsonStringEnumConverter`（驼峰命名），因此前端可以传 `"copy"` 这类语义化枚举字符串，无需依赖枚举序号。

### 5.3 委托签名与结果装箱

```csharp
public delegate Task<object?> CompiledCommandInvoker(
    object? instance, JsonElement parameters, ICommandContext? ctx);
```

- 同步方法：闭包内用 `Task.FromResult(result)` 包装；
- `Task` / `Task<T>` / `ValueTask`：闭包内 `await`，`Task<T>` 的 Result 直接返回（装箱由编译器完成）；
- 无返回值：返回 `null`。

因此 `CommandEntry.Invoker` 只是一个普通委托字段：既没有「编译缓存」，也不存在「编译失败回退反射」的分支。

## 6. 错误处理

[`CallError`](../../src/Wails.Net.Errors/CallError.cs) 是结构化错误对象，包含 `Message`、`Cause`、`Kind` 三字段，通过 `ToJson()` 序列化为字典返回给前端：

```csharp
public Dictionary<string, object?> ToJson() => new()
{
    ["message"] = Message,
    ["cause"]   = Cause,
    ["kind"]    = Kind.ToString()
};
```

[`CallErrorKind`](../../src/Wails.Net.Errors/CallError.cs) 对应 JavaScript 运行时错误类型：

| 值 | 含义 | 典型场景 |
|----|------|---------|
| `ReferenceError` | 引用错误：访问未定义的方法 | `BindingManager.Call` 未找到 ID/名称 |
| `TypeError` | 类型错误：参数类型不匹配 | `JsonException`、`ArgumentException` |
| `RuntimeError` | 运行时错误：执行中其他异常 | `InvalidOperationException`、超时 |

### 6.1 异常类型映射

调用器是直接方法调用，抛出的就是业务异常本身（不会被 `TargetInvocationException` 包裹），因此按具体异常类型映射即可：

```csharp
catch (InvalidOperationException ex) => ErrorResult(ex.Message, CallErrorKind.RuntimeError);
catch (ArgumentException ex)         => ErrorResult(ex.Message, CallErrorKind.TypeError);
catch (JsonException ex)            => ErrorResult($"参数反序列化失败: {ex.Message}", CallErrorKind.TypeError);
catch (OperationCanceledException)  => throw;   // 交由 MessageProcessor 统一处理为"调用已被取消"
catch (Exception ex)                => ErrorResult(ex.Message, CallErrorKind.RuntimeError);
```

> `TargetInvocationException` 解包逻辑随 `MethodInfo.Invoke` 一并移除（AGENTS.md §3.3 已注明：源生成器路径下直接捕获具体异常类型）。命令调度层同样不再解包，`ExecuteCommandAsync` 捕获 `Exception` 后直接取 `ex.Message`。

### 6.2 结果字典统一格式

无论绑定路径还是命令路径，返回前端的字典都遵循 `{ "result": object?, "error": CallError.ToJson() | null }` 结构，便于前端统一处理。`CommandDispatcher` 额外封装为 `InvokeResponse(request.Id, success, result, error)`，由调用方决定如何包装为最终响应。

## 7. 消息路由

[`MessageProcessor`](../../src/Wails.Net.Application/Transport/MessageProcessor.cs) 是传输层与绑定/命令系统之间的桥梁，对应 Wails v3 Go 版本 `messageprocessor.go`。它接收前端 JSON 消息，按 `type` 字段路由：

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

`ProcessCallAsync` 是绑定/命令双轨架构的关键节点。`CallPayload` 可携带 `id`（FNV-1a 哈希）或 `name`（方法全名）。宿主启动时会注入 [`InvocationGateway`](../../src/Wails.Net.Application/Invocation/InvocationGateway.cs)，由网关统一「先 Binding 后 Command」的顺序、上下文与错误模型：

```csharp
if (_gateway is not null)
{
    result = callPayload.Id is not null
        ? await _gateway.InvokeByIdAsync(callPayload.Id.Value, args, message.WindowId, message.Origin, callCts.Token)
        : await _gateway.InvokeAsync(callPayload.Name!, args, message.WindowId, message.Origin, callCts.Token);
}
```

网关未注入时（测试场景或独立构造的 `MessageProcessor`）退回本地路径，逻辑与网关一致：先 `_bindings.Call(...)`，返回 `ReferenceError` 且名称存在时再调 `TryDispatchCommandAsync` 回退到 `CommandDispatcher`。`IsNotFoundResult` 通过检查错误字典的 `message` 字段是否包含"未找到"和原方法名来判断是否回退（直接读字典而非 JSON 序列化，避免非 ASCII 字符转义导致匹配失败）。

### 7.2 未识别命名空间的命令回退

未识别的命名空间（如 `notification.show`、`tray.setIcon`、`application.hide`）通过 `_ => ProcessCommandFallbackAsync(message)` 兜底，遵循「核心即插件」的插件化设计：所有系统原生操作以插件命令形式注册到 `CommandDispatcher`，前端 `wails.<namespace>.<method>()` 调用通过消息类型本身作为命令名派发。

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
- [Bindings.cs (BindingManager)](../../src/Wails.Net.Application/Bindings/Bindings.cs) · [BindingAttribute.cs](../../src/Wails.Net.Application/Bindings/BindingAttribute.cs) · [BindingRegistry.cs](../../src/Wails.Net.Application/Bindings/BindingRegistry.cs) · [GeneratedBindingRegistry.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs) · [GeneratedBindingsMetadata.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs)
- [CommandAttribute.cs](../../src/Wails.Net.Application/Commands/CommandAttribute.cs) · [CommandDispatcher.cs](../../src/Wails.Net.Application/Commands/CommandDispatcher.cs) · [CommandRegistry.cs](../../src/Wails.Net.Application/Commands/CommandRegistry.cs) · [CompiledCommandInvoker.cs](../../src/Wails.Net.Application/Commands/CompiledCommandInvoker.cs) · [MapCommandExtensions.cs](../../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) · [ICommandContext.cs](../../src/Wails.Net.Application/Commands/ICommandContext.cs) · [ICommandMiddleware.cs](../../src/Wails.Net.Application/Commands/ICommandMiddleware.cs) · [CommandContext.cs](../../src/Wails.Net.Application/Commands/CommandContext.cs) · [InvokeRequest.cs](../../src/Wails.Net.Application/Commands/InvokeRequest.cs)
- [BindingSourceGenerator.cs](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) · [MessageProcessor.cs](../../src/Wails.Net.Application/Transport/MessageProcessor.cs) · [InvocationGateway.cs](../../src/Wails.Net.Application/Invocation/InvocationGateway.cs) · [CallError.cs](../../src/Wails.Net.Errors/CallError.cs)
