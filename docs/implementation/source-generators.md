# 源代码生成器与代码生成

> 本文档详细描述 Wails.Net 项目的两套代码生成系统：编译期源生成器（`Wails.Net.SourceGenerators`）与运行时代码生成器（`Wails.Net.Generator`），以及表达式树编译器与运行时 JavaScript 生成器的设计与实现。

---

## 1. 概述

Wails.Net 采用**两套互补**的代码生成系统以消除运行时反射、提升性能并支持 AOT 裁剪：

| 系统 | 程序集 | 执行时机 | 主要产物 |
|------|--------|----------|----------|
| **编译期源生成器** | `Wails.Net.SourceGenerators` | 编译期（Roslyn 增量生成器） | 强类型调用器委托、绑定元数据 |
| **运行时代码生成器** | `Wails.Net.Generator` | CLI 命令 `wails.net generate` | TypeScript 类型定义、调用封装、事件常量 |
| **运行时 JS 生成器** | `Wails.Net.Runtime.Js` | 应用启动期 | 注入 Webview 的 `window.wails` API |
| **表达式树编译器** | `Wails.Net.Application.Commands` | 命令注册时（首次调用前） | `CompiledCommandInvoker` 委托 |

设计原则：

1. **编译期能确定的，绝不留到运行时**——元数据、调用器、类型映射在编译期完成
2. **零反射调用**——通过源生成器调用器与表达式树编译委托实现
3. **AOT 友好**——避免 `MethodInfo.Invoke` 与运行时反射分析
4. **多平台一致**——FNV-1a 哈希与 TypeScript 类型映射在编译期与运行时完全一致

---

## 2. 编译期源生成器（Wails.Net.SourceGenerators）

### 2.1 项目配置

源生成器项目以 `netstandard2.1` 为目标框架（Roslyn 增量生成器的标准要求），并启用 `IsRoslynComponent` 与 `EnforceExtendedAnalyzerRules`：

参考 [Wails.Net.SourceGenerators.csproj](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/Wails.Net.SourceGenerators.csproj)：

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

[BindingSourceGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 实现 `IIncrementalGenerator`，相比传统的 `ISourceGenerator` 具备增量编译能力——仅当受影响的方法变化时才重新生成代码：

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

参考 [BindingAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BindingAttribute.cs)：

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

参考 [CommandAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandAttribute.cs)：

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

源生成器为每个标记方法生成一个静态调用器方法，签名遵循 [GeneratedBindingRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs) 中的 `GeneratedInvoker` 委托：

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

注册时同时维护三张表（见 [GeneratedBindingRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)）：

- `_invokers`：方法全名 → 调用器委托
- `_methodToTypeName`：方法全名 → 所属类型全名
- `_typeMethods`：类型全名 → 方法名列表

### 2.6 编译期元数据（GeneratedBindingsMetadata）

参考 [GeneratedBindingsMetadata.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs)：

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

调用器按方法返回类型分三种生成路径：

```csharp
// void 方法（非异步）
sb.AppendLine($"            {callExpr};");
sb.AppendLine("            return null;");

// Task / Task<T>（异步）
sb.AppendLine($"            return {callExpr};");

// 同步返回值
sb.AppendLine($"            return {callExpr};");
```

异步方法返回 `Task` 或 `Task<T>` 实例，由调用方 `await`。

---

## 3. 表达式树编译器（CommandInvokerCompiler）

### 3.1 设计目标

[CommandInvokerCompiler.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs) 处理未由源生成器覆盖的场景（如运行时动态注册的命令、第三方程序集方法）。借鉴 ASP.NET Core MVC 的表达式树编译模式：

```csharp
public delegate object? CompiledCommandInvoker(
    object instance,
    JsonElement parameters,
    ICommandContext? ctx);
```

### 3.2 编译流程

将 `MethodInfo` 编译为 `CompiledCommandInvoker` 委托：

```csharp
private static CompiledCommandInvoker? CompileCore(MethodInfo method, JsonSerializerOptions jsonOptions)
{
    var instanceParam = Expression.Parameter(typeof(object), "instance");
    var parametersParam = Expression.Parameter(typeof(JsonElement), "parameters");
    var ctxParam = Expression.Parameter(typeof(ICommandContext), "ctx");

    var methodParams = method.GetParameters();
    var args = new Expression[methodParams.Length];

    for (var i = 0; i < methodParams.Length; i++)
    {
        var paramType = methodParams[i].ParameterType;
        if (paramType == typeof(ICommandContext))
            args[i] = ctxParam;
        else if (paramType == typeof(CancellationToken))
            args[i] = Expression.Property(ctxParam, ctxCancellationTokenProp);
        else if (paramType == typeof(IServiceProvider))
            args[i] = Expression.Property(ctxParam, ctxServicesProp);
        else
            args[i] = Expression.Convert(
                Expression.Call(deserializeMethod,
                    Expression.Call(parametersParam, getRawTextMethod),
                    Expression.Constant(paramType, typeof(Type)),
                    Expression.Constant(jsonOptions, typeof(JsonSerializerOptions))),
                paramType);
    }

    var call = Expression.Call(Expression.Convert(instanceParam, declaringType), method, args);
    var body = method.ReturnType == typeof(void)
        ? (Expression)Expression.Block(call, Expression.Constant(null, typeof(object)))
        : Expression.Convert(call, typeof(object));

    return Expression.Lambda<CompiledCommandInvoker>(body, instanceParam, parametersParam, ctxParam).Compile();
}
```

### 3.3 参数绑定策略

与 `CommandDispatcher.ExecuteCommandAsync` 原有逻辑一致：

| 参数类型 | 绑定来源 |
|---------|---------|
| `ICommandContext` | 注入 `ctx` 参数 |
| `CancellationToken` | 注入 `ctx.CancellationToken` |
| `IServiceProvider` | 注入 `ctx.Services` |
| 其他类型 | `JsonSerializer.Deserialize(parameters.GetRawText(), type, options)` |

### 3.4 编译缓存

使用 `ConcurrentDictionary` 缓存编译结果，避免重复编译同一 `MethodInfo`：

```csharp
private static readonly ConcurrentDictionary<MethodInfo, CompiledCommandInvoker?> _cache = new();

public static CompiledCommandInvoker? Compile(MethodInfo method)
{
    return _cache.GetOrAdd(method, static (m, options) => CompileCore(m, options), _jsonOptions);
}
```

编译仅在首次调用时发生，之后所有调用直接命中委托，零反射开销。

### 3.5 与源生成器的关系

| 维度 | 源生成器调用器 | 表达式树编译委托 |
|------|---------------|----------------|
| 触发时机 | 编译期 | 运行时（首次注册） |
| 输入 | `IMethodSymbol` | `MethodInfo` |
| 输出 | 静态方法 | `CompiledCommandInvoker` 委托 |
| 适用场景 | 编译时已知的 `[Binding]`/`[Command]` 方法 | 运行时动态注册的方法、第三方程序集 |
| 性能 | 最优（直接静态调用） | 接近最优（委托调用，无反射） |

两者互补：源生成器覆盖编译期可见方法，表达式树编译器覆盖运行时动态场景。

---

## 4. 运行时代码生成器（Wails.Net.Generator）

### 4.1 BindingAnalyzer — 绑定方法分析器

[BindingAnalyzer.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/BindingAnalyzer.cs) 从 `GeneratedBindingsMetadata` 读取编译期填充的元数据，不再使用 `System.Reflection`：

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

[BindingIdGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/BindingIdGenerator.cs) 提供与 Go 版本 `fnv.New32a()` 完全一致的哈希计算：

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

[TypeScriptGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/TypeScriptGenerator.cs) 生成三类文件：

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

[TypeScriptTypeMapper.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/TypeScriptTypeMapper.cs) 定义运行时类型映射，源生成器中也有等价实现以在编译期完成映射：

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

[EventGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/EventGenerator.cs) 从 C# 事件枚举生成 TypeScript 枚举与已知事件常量：

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

## 5. 运行时 JS 生成器（RuntimeGenerator）

### 5.1 Generate(options) 主入口

[RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 在应用启动时生成注入 Webview 的 JavaScript 运行时：

```csharp
public static string Generate(RuntimeOptions options)
{
    var flags = GenerateFlags(options);
    var api = GenerateApi(options);
    var transport = LoadTemplate(TransportTemplateFileName, options);
    var platformRuntime = options.IsServerMode
        ? ServerRuntime.Generate(options)
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
| `wails.window` | 窗口管理（40+ 方法） | Wails v3 + Tauri v2 |
| `wails.tray` | 系统托盘 | Tauri v2 |
| `wails.windows` | 多窗口管理 | Tauri v2 |
| `wails.screen` / `wails.clipboard` / `wails.dialog` / `wails.menu` | 系统功能 | Wails v3 |
| `wails.application` | 应用级控制 | Wails v3 |
| `wails.stronghold` | 加密存储 | Tauri v2 plugin-stronghold |
| `wails.scope` | 持久化作用域 | Tauri v2 plugin-persisted-scope |
| `wails.localhost` | 嵌入式 HTTP 服务器 | Tauri v2 plugin-localhost |
| `wails.fswatch` | 文件系统监听 | Tauri v2 plugin-fs-watch |
| `wails.system` / `wails.power` / `wails.process` | 系统信息 | Tauri v2 plugin-os |
| `wails.fs` / `wails.shell` | 文件系统/Shell | Tauri v2 |
| `wails.notification` / `wails.store` / `wails.log` | 通知/存储/日志 | Tauri v2 |

所有调用通过 `window._wailsInvoke(type, payload)` 转发到后端 `MessageProcessor`。

### 5.4 模板加载与占位符替换

`LoadTemplate` 从程序集嵌入资源加载模板：

```csharp
internal static string LoadTemplate(string templateFileName, RuntimeOptions options)
{
    var assembly = typeof(RuntimeGenerator).Assembly;
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

### 5.5 DesktopRuntime vs ServerRuntime

通过 `options.IsServerMode` 选择平台运行时：

```csharp
var platformRuntime = options.IsServerMode
    ? ServerRuntime.Generate(options)   // 容器化部署、无 GUI
    : DesktopRuntime.Generate(options); // 桌面应用（WebView2 / WebKitGTK）
```

- `DesktopRuntime`：通过 Webview IPC 通信（`window._wailsInvoke` 由原生拦截）
- `ServerRuntime`：通过 WebSocket / HTTP 与浏览器通信（`{WEBSOCKET_URL}` 占位符）

---

## 6. 三层调用性能优化

### 6.1 调用优先级

`BindingManager.Call` 按以下优先级尝试调用：

```
1. 源生成器调用器（GeneratedBindingRegistry.TryGetInvoker）
       ↓ 未命中
2. 表达式树编译委托（CommandInvokerCompiler.Compile）
       ↓ 未命中
3. 反射 MethodInfo.Invoke（最终回退）
```

### 6.2 零反射调用路径

#### 路径 1：源生成器调用器（最优）

```csharp
if (GeneratedBindingRegistry.TryGetInvoker(fullName, out var invoker))
{
    var instance = ResolveInstance(typeFullName);
    return invoker!(instance, args, cancellationToken);
}
```

- 无反射，直接委托调用
- 参数反序列化使用编译期已知的泛型 `JsonElement.Deserialize<T>`
- AOT 完全可裁剪

#### 路径 2：表达式树编译委托（次优）

```csharp
var compiled = CommandInvokerCompiler.Compile(methodInfo);
if (compiled is not null)
{
    return compiled(instance, parameters, ctx);
}
```

- 首次编译后缓存，后续调用零反射
- 表达式树编译的委托与源生成器调用器性能接近
- 适用于运行时动态注册的方法

#### 路径 3：反射 MethodInfo.Invoke（最终回退）

```csharp
return methodInfo.Invoke(instance, BindingFlags.Default, null, args, null);
```

- 仅当上述两条路径均不可用时使用
- 性能最差（每次调用都有反射开销）
- 不支持 AOT 裁剪

### 6.3 性能对比

| 调用路径 | 首次调用 | 后续调用 | AOT 兼容 |
|---------|---------|---------|---------|
| 源生成器调用器 | 极快（直接委托） | 极快 | ✅ |
| 表达式树编译委托 | 慢（编译表达式树） | 极快（委托调用） | ⚠️（仅生成代码可见） |
| 反射 `MethodInfo.Invoke` | 中等 | 中等（每次有反射开销） | ❌ |

### 6.4 元数据查询路径

绑定元数据查询同样遵循编译期优先：

```
1. GeneratedBindingsMetadata.Methods（编译期填充）
       ↓ 未找到
2. 运行时反射分析（Assembly.GetTypes）
```

`BindingAnalyzer` 优先从 `GeneratedBindingsMetadata` 读取，避免运行时反射分析。`TypeScriptGenerator` 通过 `BindingAnalyzer` 间接使用编译期元数据，确保生成的 TypeScript 类型定义与后端实际签名完全一致。

---

## 7. 相关源文件索引

### 编译期源生成器

| 文件 | 职责 |
|------|------|
| [BindingSourceGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) | 增量生成器主入口，扫描特性并生成调用器 |
| [IsExternalInitPolyfill.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/IsExternalInitPolyfill.cs) | record 类型 polyfill |
| [Wails.Net.SourceGenerators.csproj](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/Wails.Net.SourceGenerators.csproj) | 项目配置（Roslyn 组件） |
| [GeneratedBindingRegistry.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs) | 调用器委托与注册表 |
| [GeneratedBindingsMetadata.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs) | 编译期元数据容器 |
| [BindingAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/BindingAttribute.cs) | `[Binding]` 特性定义 |
| [CommandAttribute.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandAttribute.cs) | `[Command]` 特性定义 |

### 表达式树编译器

| 文件 | 职责 |
|------|------|
| [CommandInvokerCompiler.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs) | `MethodInfo` → `CompiledCommandInvoker` 编译 |

### 运行时代码生成器

| 文件 | 职责 |
|------|------|
| [BindingAnalyzer.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/BindingAnalyzer.cs) | 从编译期元数据读取绑定方法 |
| [BindingIdGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/BindingIdGenerator.cs) | FNV-1a 哈希 ID 生成 |
| [TypeScriptGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/TypeScriptGenerator.cs) | TypeScript 定义/调用封装生成 |
| [TypeScriptTypeMapper.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/TypeScriptTypeMapper.cs) | C# → TypeScript 类型映射 |
| [EventGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Generator/EventGenerator.cs) | 事件枚举/常量生成 |

### 运行时 JS 生成器

| 文件 | 职责 |
|------|------|
| [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) | `window.wails` API 与模板生成 |

---

## 8. 设计总结

Wails.Net 的代码生成系统通过**编译期与运行时分层**、**多级回退**的策略实现了性能与灵活性的平衡：

1. **编译期完成尽可能多的工作**：调用器生成、元数据填充、FNV-1a 哈希、TypeScript 类型映射
2. **运行时多层回退保证兼容性**：源生成器 → 表达式树 → 反射，覆盖从静态到动态的各种注册场景
3. **三端哈希一致性**：源生成器、`BindingIdGenerator`、`BindingManager.FNV1aHash` 使用相同的 FNV-1a 算法
4. **类型映射一致性**：`TypeScriptTypeMapper.MapType`（运行时）与 `BindingSourceGenerator.MapTypeToTypeScript`（编译期）保持映射规则同步
5. **AOT 友好设计**：消除运行时反射，为 Native AOT 裁剪扫清障碍

这一架构既保留了 Wails v3 的对象模型与 IPC 设计，又融入了 ASP.NET Core 的表达式树编译模式与 Tauri v2 的插件能力，是 .NET 10 桌面应用框架的有益探索。
