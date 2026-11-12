# 源代码生成器与代码生成

> 本文档详细描述 Wails.Net 项目的两套代码生成系统：编译期源生成器（`Wails.Net.SourceGenerators`）与运行时代码生成器（`Wails.Net.Generator`），以及命令调用器（源生成器 / `MapCommand` 闭包）与运行时 JavaScript 生成器的设计与实现。

---

## 1. 概述

Wails.Net 采用**两套互补**的代码生成系统以消除运行时反射、提升性能并支持 AOT 裁剪：

| 系统 | 程序集 | 执行时机 | 主要产物 |
|------|--------|----------|----------|
| **编译期源生成器** | `Wails.Net.SourceGenerators` | 编译期（Roslyn 增量生成器） | 强类型调用器委托、绑定元数据 |
| **运行时代码生成器** | `Wails.Net.Generator` | CLI 命令 `wails.net generate` | TypeScript 类型定义、调用封装、事件常量 |
| **运行时 JS 生成器** | `@wails-net/runtime` | 应用启动期 | 注入 Webview 的 `window.wails` API |
| **命令调用器闭包** | `Wails.Net.Application.Commands` | 命令注册点（`MapCommand` 调用时） | `CompiledCommandInvoker` 委托 |

设计原则：

1. **编译期能确定的，绝不留到运行时**——元数据、调用器、类型映射在编译期完成
2. **零反射调用**——由源生成器调用器与 `MapCommand` 泛型闭包实现
3. **AOT 友好**——避免 `MethodInfo.Invoke` 与运行时反射分析
4. **多平台一致**——FNV-1a 哈希与 TypeScript 类型映射在编译期与运行时完全一致

---

## 2. 编译期源生成器（Wails.Net.SourceGenerators）

### 2.1 项目配置

源生成器项目以 `netstandard2.1` 为目标框架（Roslyn 增量生成器的标准要求），并启用 `IsRoslynComponent` 与 `EnforceExtendedAnalyzerRules`：

参考 [Wails.Net.SourceGenerators.csproj](../../src/Wails.Net.SourceGenerators/Wails.Net.SourceGenerators.csproj)：

```xml
<PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <NoWarn>RS1041</NoWarn>
</PropertyGroup>
<ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
</ItemGroup>
```

`IsExternalInitPolyfill.cs` 为 `netstandard2.1` 提供 `record` 类型所需的 `IsExternalInit` polyfill：

```csharp
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
```

### 2.2 IIncrementalGenerator 增量生成器模式

[BindingSourceGenerator.cs](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 实现 `IIncrementalGenerator`，相比传统的 `ISourceGenerator` 具备增量编译能力——仅当受影响的方法变化时才重新生成代码：

```csharp
[Generator(LanguageNames.CSharp)]
public sealed class BindingSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var bindingMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Wails.Net.Application.Bindings.BindingAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => /* ... */);

        var commandMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Wails.Net.Application.Commands.CommandAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => /* ... */);

        var allMarkers = bindingMethods.Collect().Combine(commandMethods.Collect())
            .SelectMany(static (pair, _) => pair.Left.Concat(pair.Right));

        var withCompilation = allMarkers.Collect().Combine(context.CompilationProvider);
        context.RegisterSourceOutput(withCompilation, static (spc, pair) => { /* ... */ });
    }
}
```

关键设计点：

- 使用 `ForAttributeWithMetadataName` 直接按特性全限定名过滤语法节点，避免不必要的语法树遍历
- 所有 `transform` 与 `predicate` 委托标记为 `static` 以确保可序列化与无副作用
- 与 `CompilationProvider` 结合以获取完整 `ITypeSymbol` 信息（生成完全限定类型名）

### 2.3 扫描 [Binding] / [Command] 特性

源生成器扫描两类特性：

#### [Binding] 特性

参考 [BindingAttribute.cs](../../src/Wails.Net.Application/Bindings/BindingAttribute.cs)：

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BindingAttribute : Attribute
{
    public string? Name { get; set; }
}
```

- 应用于服务类实例方法
- 绑定名默认为 `ClassName.MethodName`
- 可通过 `Name` 属性指定别名

#### [Command] 特性

参考 [CommandAttribute.cs](../../src/Wails.Net.Application/Commands/CommandAttribute.cs)：

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CommandAttribute : Attribute
{
    public string Name { get; }
    public CommandAttribute(string name) { Name = name; }
}
```

- 命令名通过构造函数指定（必填）
- 支持跨类继承

#### 名称提取策略

`BindingSourceGenerator` 通过两种方式提取自定义名称：

```csharp
// [Binding(Name = "alias")] —— 通过命名参数
var customName = ExtractNamedArgument(ctx.Attributes.FirstOrDefault(), "Name");

// [Command("counter.increment")] —— 通过构造函数参数
var customName = ExtractConstructorArgument(ctx.Attributes.FirstOrDefault(), 0);
```

### 2.4 生成调用器委托（GeneratedInvoker）

源生成器为每个标记方法生成一个静态调用器方法，签名遵循 [GeneratedBindingRegistry.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs) 中的 `GeneratedInvoker` 委托：

```csharp
public delegate object? GeneratedInvoker(
    object instance,
    JsonElement[] args,
    CancellationToken cancellationToken);
```

生成的调用器示例：

```csharp
public static object? GreetingService_Greet(
    object instance,
    JsonElement[] args,
    CancellationToken cancellationToken)
{
    var svc = (Wails.Net.Examples.GreetingService)instance;
    var p0 = args.Length > 0 && !args[0].ValueKind.Equals(JsonValueKind.Null)
        ? args[0].Deserialize<string>(JsonOptions.DefaultSerializerOptions)!
        : default(string)!;
    return svc.Greet(p0);
}
```

参数绑定策略：

- `CancellationToken` 参数 → 直接传递 `cancellationToken`
- 其他类型 → 通过 `JsonElement.Deserialize<T>` 反序列化
- 非可空类型追加 `!`（null-forgiving 运算符）以避免 CS8604 警告
- 数组越界或 `JsonValueKind.Null` 时使用 `default(T)` 或 `null`

### 2.5 [ModuleInitializer] 自动注册

生成的代码通过 `[ModuleInitializer]` 在模块加载时自动注册到 `GeneratedBindingRegistry`：

```csharp
public static class GeneratedBindingsRegistration
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    public static void Register()
    {
        GeneratedBindingRegistry.Register(
            "Wails.Net.Examples.GreetingService.Greet",
            GreetingService_Greet,
            "Wails.Net.Examples.GreetingService");

        // 对于 [Binding]，同时注册短名称别名
        GeneratedBindingRegistry.Register(
            "GreetingService.Greet",
            GreetingService_Greet,
            "Wails.Net.Examples.GreetingService");

        // 同步注册元数据
        GeneratedBindingsMetadata.Register(
            new BoundMethodInfo(
                FullName: "Wails.Net.Examples.GreetingService.Greet",
                Id: 123456789u,
                Namespace: "Wails.Net.Examples",
                ClassName: "GreetingService",
                MethodName: "Greet",
                Parameters: new BoundParameterInfo[] { /* ... */ },
                ReturnTypeName: "string",
                IsAsync: false,
                IsCommand: false));
    }
}
```

注册时同时维护三张表（见 [GeneratedBindingRegistry.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)）：

- `_invokers`：方法全名 → 调用器委托
- `_methodToTypeName`：方法全名 → 所属类型全名
- `_typeMethods`：类型全名 → 方法名列表

### 2.6 编译期元数据（GeneratedBindingsMetadata）

参考 [GeneratedBindingsMetadata.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs)：

```csharp
public sealed record BoundMethodInfo(
    string FullName,
    uint Id,                       // FNV-1a 32位哈希
    string Namespace,
    string ClassName,
    string MethodName,
    IReadOnlyList<BoundParameterInfo> Parameters,
    string ReturnTypeName,        // TypeScript 类型字符串
    bool IsAsync,
    bool IsCommand);

public sealed record BoundParameterInfo(
    string Name,
    string TypeName,              // TypeScript 类型字符串
    bool IsVariadic,
    bool IsCancellationToken);
```

`BoundMethodInfo` 替代运行时 `System.Reflection.MethodInfo`，供 `BindingAnalyzer` 和 `TypeScriptGenerator` 使用。源生成器在编译期完成以下工作：

1. 计算返回类型的 TypeScript 表示（移植自 `TypeScriptTypeMapper.MapType`，适配 Roslyn 符号模型）
2. 计算 FNV-1a 哈希 ID
3. 解析每个参数的 TypeScript 类型、可变参数标记、CancellationToken 标记

### 2.7 FNV-1a 哈希在编译期计算

源生成器在编译期计算 FNV-1a 32 位哈希，与运行时 `BindingManager.FNV1aHash` 完全一致：

```csharp
private static uint ComputeFnv1aHash(string text)
{
    const uint offsetBasis = 2166136261u;
    const uint prime = 16777619u;
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    var hash = offsetBasis;
    foreach (var b in bytes)
    {
        hash ^= b;
        hash *= prime;
    }
    return hash;
}
```

哈希值在编译期作为字面量写入生成代码（如 `Id: 123456789u`），运行时无需计算。

### 2.8 AOT 裁剪支持

通过消除运行时反射与 `MethodInfo.Invoke`，源生成器使 Wails.Net 应用可参与 Native AOT 裁剪：

- 所有方法调用通过强类型委托，无 `MethodInfo.Invoke` 调用
- 元数据通过 `record` 静态填充，无需 `Assembly.GetTypes()` 反射
- `JsonElement.Deserialize<T>` 由 Roslyn 在编译期确定具体泛型参数，可被 AOT 分析器跟踪

### 2.9 生成的调用器签名

调用器统一签名为 `public static Task<object?> X(object instance, JsonElement[] args, CancellationToken cancellationToken)`，按方法返回类型分三条生成路径：

| 方法形态 | 生成的调用体 |
|---------|-------------|
| `void`（同步） | 调用后 `return Task.FromResult<object?>(null);` |
| `T`（同步返回值） | `var result = svc.M(args); return Task.FromResult<object?>(result);` |
| `Task` / `Task<T>` / `ValueTask` | 加 `async` 修饰，`await svc.M(args).ConfigureAwait(false)` 后返回结果（`Task<T>` 返回装箱结果，`Task` 返回 `null`） |

同步方法**不加** `async` 修饰，避免 CS1998（async 方法缺少 await）警告；调用方只 `await` 委托返回值，无需反射提取 `Task.Result`。

---

## 3. 命令调用器的构建（编译期闭包）

> **历史说明**：本节原为「表达式树编译器（`CommandInvokerCompiler`）」。该实现已在「去反射改源生成器」（commit `0fd6144`）中整体删除——它以 `MethodInfo` 为输入、本身依赖 `System.Reflection`，与 AGENTS.md §3.4 冲突。当前运行时注册的命令改由 [`MapCommandExtensions`](../../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) 的泛型重载在**注册点**构造闭包，参数类型在编译期已确定。

### 3.1 委托形态

```csharp
public delegate Task<object?> CompiledCommandInvoker(
    object? instance,
    JsonElement parameters,
    ICommandContext? ctx);
```

### 3.2 闭包构建

```csharp
public static CommandRegistry MapCommand<T>(this CommandRegistry registry, string name, Action<T> handler)
{
    var options = _jsonOptions;
    return registry.Register(name, (instance, parameters, ctx) =>
    {
        var arg = Deserialize<T>(parameters, options);   // T 在编译期已知
        handler(arg);
        return Task.FromResult<object?>(null);
    }, new[] { typeof(T) });
}
```

闭包捕获 `handler`，`CommandRegistry` 只保存委托，**不保存 `MethodInfo`**，因此也没有「编译缓存」这一环节。

### 3.3 参数解析策略

与源生成器调用器保持一致，按**业务参数个数**决定解析方式：

| 业务参数个数 | 解析方式 |
|--------------|----------|
| 0 | 忽略 `parameters` |
| 1 | `parameters` 整体反序列化为 `T` |
| ≥2 | `parameters` 视为 JSON 数组，按位置反序列化 `T1..Tn`，缺位取默认值 |

反序列化选项固定为 `JsonSerializerDefaults.Web` 加 `JsonStringEnumConverter`（驼峰命名），因此前端可传 `"copy"` 这类语义化枚举字符串，无需依赖枚举序号。`ICommandContext` 由重载签名直接注入，不参与 JSON 解析。

### 3.4 与源生成器的分工

| 维度 | 源生成器调用器 | MapCommand 闭包 |
|------|---------------|----------------|
| 触发时机 | 编译期（`[ModuleInitializer]`） | 注册点（`MapCommand` 调用时） |
| 输入 | `IMethodSymbol` | 强类型 `handler` 委托 |
| 输出 | 静态方法 | `CompiledCommandInvoker` 委托 |
| 适用场景 | `[Binding]` / `[Command]` 方法 | 运行时组装的命令、插件内部注册 |
| 元数据 | `GeneratedBindingsMetadata` 全量（含 TS 类型） | 仅 `ParameterTypes`（注册时显式传入） |

两条路径产出同一个委托类型，`CommandDispatcher` 无需区分来源；原「编译缓存」小节随 `CommandInvokerCompiler` 删除而不再适用。

---

## 4. 运行时代码生成器（Wails.Net.Generator）

### 4.1 BindingAnalyzer — 绑定方法分析器

[BindingAnalyzer.cs](../../src/Wails.Net.Generator/BindingAnalyzer.cs) 从 `GeneratedBindingsMetadata` 读取编译期填充的元数据，不再使用 `System.Reflection`：

```csharp
public List<BoundMethodModel> AnalyzeAssembly(Assembly assembly)
{
    return GeneratedBindingsMetadata.Methods.Select(ToModel).ToList();
}

public List<BoundMethodModel> AnalyzeType(Type type)
{
    var fullTypeName = string.IsNullOrEmpty(type.Namespace)
        ? type.Name
        : $"{type.Namespace}.{type.Name}";

    return GeneratedBindingsMetadata.Methods
        .Where(m => $"{m.Namespace}.{m.ClassName}" == fullTypeName)
        .Select(ToModel)
        .ToList();
}
```

`ToModel` 将 `BoundMethodInfo` 转换为 `BoundMethodModel`，供 TypeScript 生成器使用。

### 4.2 BindingIdGenerator — FNV-1a 哈希 ID 生成

[BindingIdGenerator.cs](../../src/Wails.Net.Generator/BindingIdGenerator.cs) 提供与 Go 版本 `fnv.New32a()` 完全一致的哈希计算：

```csharp
public static class BindingIdGenerator
{
    public const uint OffsetBasis = 2166136261u;
    public const uint Prime = 16777619u;

    public static uint Generate(string text)
    {
        var hash = OffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= Prime;
        }
        return hash;
    }

    public static string GetFullName(string @namespace, string className, string methodName)
        => $"{@namespace}.{className}.{methodName}";
}
```

与源生成器 `BindingSourceGenerator.ComputeFnv1aHash`、运行时 `BindingManager.FNV1aHash` 三方一致。

### 4.3 TypeScriptGenerator — TypeScript 代码生成

[TypeScriptGenerator.cs](../../src/Wails.Net.Generator/TypeScriptGenerator.cs) 生成三类文件：

#### 1. 类型定义文件（`.d.ts`）

```typescript
declare namespace Wails.Net.Examples {
  interface GreetingService {
    Greet(name: string): string;
    GetCurrentTimeAsync(ct?: void): Promise<string>;
  }
}
```

#### 2. 调用封装文件（`.ts`）

```typescript
import { wails } from '@wails/runtime';

export class GreetingService {
  static Greet(name: string): string {
    return wails.bindings.call(123456789, [name]);
  }
  static async GetCurrentTimeAsync(): Promise<string> {
    return await wails.bindings.call(987654321, []);
  }
}
```

#### 3. ID 映射文件

```typescript
export const bindingIds = {
  "Wails.Net.Examples.GreetingService.Greet": 123456789,
  "Wails.Net.Examples.GreetingService.GetCurrentTimeAsync": 987654321,
};
```

关键生成规则：

- 异步方法自动包装 `Promise<T>` 与 `async/await`
- `CancellationToken` 参数不暴露给前端（`IsCancellationToken` 过滤）
- 可变参数追加 `...` 前缀

### 4.4 TypeScriptTypeMapper — C# 类型映射

[TypeScriptTypeMapper.cs](../../src/Wails.Net.Generator/TypeScriptTypeMapper.cs) 定义运行时类型映射，源生成器中也有等价实现以在编译期完成映射：

| C# 类型 | TypeScript 类型 |
|---------|----------------|
| `void` | `void` |
| `bool` | `boolean` |
| `byte`/`int`/`long`/`float`/`double`/`decimal` | `number` |
| `char`/`string` | `string` |
| `object` | `unknown` |
| `DateTime`/`DateTimeOffset`/`TimeSpan`/`Guid`/`Uri` | `string` |
| `byte[]` | `number[]` |
| `Nullable<T>` | `T \| null` |
| `T[]` | `T[]` |
| `List<T>`/`IList<T>`/`IEnumerable<T>` | `T[]` |
| `Dictionary<K,V>` | `Record<K, V>` |
| `Task<T>`/`ValueTask<T>` | `T` |
| `Task`/`ValueTask` | `void` |
| `CancellationToken` | `void` |
| 枚举 | 枚举名 |
| 元组 `Tuple<...>` | `[T1, T2, ...]` |

### 4.5 EventGenerator — 事件常量生成

[EventGenerator.cs](../../src/Wails.Net.Generator/EventGenerator.cs) 从 C# 事件枚举生成 TypeScript 枚举与已知事件常量：

```typescript
// 从枚举生成
export enum WindowEvent {
  Created = 0,
  Activated = 1,
  Closed = 2
}

// 从 KnownEvents 常量类生成
export const KnownEvents = {
  WindowCreated: "window:created",
  WindowClosed: "window:closed",
};
```

注意：`EventGenerator` 仍使用 `Assembly.GetExportedTypes()` 与 `Enum.GetValues()` 反射，因为事件枚举类型无需 AOT 裁剪（CLI 工具运行环境）。

---

## 5. 运行时 JS 生成器（@wails-net/runtime）

### 5.1 Generate(options) 主入口

[@wails-net/runtime.cs](../../packages/wails-net-runtime/src/core/runtime.ts) 在应用启动时生成注入 Webview 的 JavaScript 运行时：

```csharp
public static string Generate(`Application.GenerateRuntimeJs` options)
{
    var flags = GenerateFlags(options);
    var api = GenerateApi(options);
    var transport = LoadTemplate(TransportTemplateFileName, options);
    var platformRuntime = options.IsServerMode
        ? transport (WebSocket).Generate(options)
        : DesktopRuntime.Generate(options);

    return $"{flags}\n{api}\n{transport}\n{platformRuntime}";
}
```

### 5.2 标志对象（GenerateFlags）

生成 `window._wails` 标志对象，包含平台、调试模式、Server 模式：

```javascript
window._wails = {
  platform: "windows",
  isDebug: true,
  isServerMode: false
};
```

### 5.3 API 对象（GenerateApi）

生成 `window.wails` API 对象，包含以下命名空间：

| 命名空间 | 功能 | 对应参考 |
|---------|------|---------|
| `wails.call` / `wails.bindings.call` | 绑定方法调用 | Wails v3 |
| `wails.events` | 事件订阅/发布 | Wails v3 |
| `wails.window` | 窗口管理（40+ 方法） | Wails v3 |
| `wails.tray` | 系统托盘 | —|
| `wails.windows` | 多窗口管理 | —|
| `wails.screen` / `wails.clipboard` / `wails.dialog` / `wails.menu` | 系统功能 | Wails v3 |
| `wails.application` | 应用级控制 | Wails v3 |
| `wails.stronghold` | 加密存储 | — |
| `wails.scope` | 持久化作用域 | — |
| `wails.localhost` | 嵌入式 HTTP 服务器 | — |
| `wails.fswatch` | 文件系统监听 | — |
| `wails.system` / `wails.power` / `wails.process` | 系统信息 | — |
| `wails.fs` / `wails.shell` | 文件系统/Shell | —|
| `wails.notification` / `wails.store` / `wails.log` | 通知/存储/日志 | —|

所有调用通过 `window._wailsInvoke(type, payload)` 转发到后端 `MessageProcessor`。

### 5.4 模板加载与占位符替换

`LoadTemplate` 从程序集嵌入资源加载模板：

```csharp
internal static string LoadTemplate(string templateFileName, `Application.GenerateRuntimeJs` options)
{
    var assembly = typeof(@wails-net/runtime).Assembly;
    var resourceName = assembly.GetManifestResourceNames()
        .FirstOrDefault(n => n.EndsWith(templateFileName, StringComparison.Ordinal));

    using var stream = assembly.GetManifestResourceStream(resourceName);
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var template = reader.ReadToEnd();
    return ReplacePlaceholders(template, options);
}
```

支持占位符：

| 占位符 | 替换值 |
|--------|--------|
| `{PLATFORM}` | `options.Platform`（如 `"windows"`） |
| `{IS_DEBUG}` | `true` / `false` |
| `{IS_SERVER_MODE}` | `true` / `false` |
| `{ASSET_SERVER_URL}` | 资源服务器 URL（Server 模式） |
| `{WEBSOCKET_URL}` | WebSocket URL（Server 模式） |

### 5.5 DesktopRuntime vs transport (WebSocket)

通过 `options.IsServerMode` 选择平台运行时：

```csharp
var platformRuntime = options.IsServerMode
    ? transport (WebSocket).Generate(options)   // 容器化部署、无 GUI
    : DesktopRuntime.Generate(options); // 桌面应用（WebView2 / WebKitGTK）
```

- `DesktopRuntime`：通过 Webview IPC 通信（`window._wailsInvoke` 由原生拦截）
- `transport (WebSocket)`：通过 WebSocket / HTTP 与浏览器通信（`{WEBSOCKET_URL}` 占位符）

---

## 6. 调用性能与元数据查询

### 6.1 调用路径

`BindingManager.Call` 只有一条路径——源生成器调用器：

```
BindingManager.Call(fullName, args)
       ↓
GeneratedBindingRegistry.TryGetInvoker(fullName)
       ├─ 命中   → await invoker(instance, args, cancellationToken)   直接委托调用
       ├─ 歧义键 → ReferenceError（fail-closed，附冲突类型说明）
       └─ 未命中 → ReferenceError → MessageProcessor / InvocationGateway 回退 CommandDispatcher
```

去反射后不再有「表达式树编译委托」与「反射 `MethodInfo.Invoke`」两级回退（即原「三层调用优化」）。

### 6.2 零反射调用路径

```csharp
if (GeneratedBindingRegistry.TryGetInvoker(fullName, out var invoker) && invoker is not null)
{
    return await InvokeGeneratedAsync(fullName, invoker, args, cancellationToken);
}
```

- 无反射，直接委托调用
- 参数反序列化使用编译期已知的泛型 `JsonElement.Deserialize<T>`
- AOT 完全可裁剪

### 6.3 性能对比

| 调用路径 | 首次调用 | 后续调用 | AOT 兼容 |
|---------|---------|---------|---------|
| 源生成器调用器 | 极快（直接委托） | 极快 | ✅ |
| `MapCommand` 闭包 | 快（`Deserialize<T>` + 直接调用） | 极快 | ✅（调用图静态可见） |
| 表达式树编译委托 | ~~慢~~ | — | 已随 `CommandInvokerCompiler` 删除 |
| 反射 `MethodInfo.Invoke` | — | — | 已禁止（AGENTS.md §3.4） |

### 6.4 元数据查询路径

绑定元数据只有一个来源：`GeneratedBindingsMetadata.Methods`（源生成器在编译期填充）。`BindingAnalyzer` 直接读取该列表，`TypeScriptGenerator` 通过 `BindingAnalyzer` 间接使用，因此不存在「未找到再反射分析程序集」的回退分支——`Assembly.GetTypes` 在 `src/` 内属禁止 API。这样生成的 TypeScript 类型定义与后端实际签名完全一致。

---

## 7. 相关源文件索引

### 编译期源生成器

| 文件 | 职责 |
|------|------|
| [BindingSourceGenerator.cs](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) | 增量生成器主入口，扫描特性并生成调用器 |
| [IsExternalInitPolyfill.cs](../../src/Wails.Net.SourceGenerators/IsExternalInitPolyfill.cs) | record 类型 polyfill |
| [Wails.Net.SourceGenerators.csproj](../../src/Wails.Net.SourceGenerators/Wails.Net.SourceGenerators.csproj) | 项目配置（Roslyn 组件） |
| [GeneratedBindingRegistry.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs) | 调用器委托与注册表 |
| [GeneratedBindingsMetadata.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs) | 编译期元数据容器 |
| [BindingAttribute.cs](../../src/Wails.Net.Application/Bindings/BindingAttribute.cs) | `[Binding]` 特性定义 |
| [CommandAttribute.cs](../../src/Wails.Net.Application/Commands/CommandAttribute.cs) | `[Command]` 特性定义 |

### 命令调用器构建

| 文件 | 职责 |
|------|------|
| [CompiledCommandInvoker.cs](../../src/Wails.Net.Application/Commands/CompiledCommandInvoker.cs) | 调用器委托定义（`Task<object?>`） |
| [MapCommandExtensions.cs](../../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) | 泛型闭包构建（运行时注册的命令） |
| [CommandRegistry.cs](../../src/Wails.Net.Application/Commands/CommandRegistry.cs) | 命令名 → 调用器注册表 |
| [CommandDispatcher.cs](../../src/Wails.Net.Application/Commands/CommandDispatcher.cs) | 调度、能力/Scope 校验、中间件管道、超时 |

### 运行时代码生成器

| 文件 | 职责 |
|------|------|
| [BindingAnalyzer.cs](../../src/Wails.Net.Generator/BindingAnalyzer.cs) | 从编译期元数据读取绑定方法 |
| [BindingIdGenerator.cs](../../src/Wails.Net.Generator/BindingIdGenerator.cs) | FNV-1a 哈希 ID 生成 |
| [TypeScriptGenerator.cs](../../src/Wails.Net.Generator/TypeScriptGenerator.cs) | TypeScript 定义/调用封装生成 |
| [TypeScriptTypeMapper.cs](../../src/Wails.Net.Generator/TypeScriptTypeMapper.cs) | C# → TypeScript 类型映射 |
| [EventGenerator.cs](../../src/Wails.Net.Generator/EventGenerator.cs) | 事件枚举/常量生成 |

### 运行时 JS 生成器

| 文件 | 职责 |
|------|------|
| [@wails-net/runtime.cs](../../packages/wails-net-runtime/src/core/runtime.ts) | `window.wails` API 与模板生成 |

---

## 8. 设计总结

Wails.Net 的代码生成系统通过**编译期分层**的策略实现了性能与灵活性的平衡：

1. **编译期完成尽可能多的工作**：调用器生成、元数据填充、FNV-1a 哈希、TypeScript 类型映射
2. **两条零反射注册路径覆盖静态与动态场景**：`[Binding]`/`[Command]` 走源生成器，运行时组装的命令走 `MapCommand` 泛型闭包；没有反射回退层
3. **三端哈希一致性**：源生成器、`BindingIdGenerator`、`BindingManager.FNV1aHash` 使用相同的 FNV-1a 算法
4. **类型映射一致性**：`TypeScriptTypeMapper.MapType`（运行时）与 `BindingSourceGenerator.MapTypeToTypeScript`（编译期）保持映射规则同步
5. **AOT 友好设计**：消除运行时反射，为 Native AOT 裁剪扫清障碍

这一架构既保留了 Wails v3 的对象模型与 IPC 设计，又融入了 ASP.NET Core Minimal API 的闭包注册风格与自研的插件能力模型，是 .NET 10 桌面应用框架的有益探索。
