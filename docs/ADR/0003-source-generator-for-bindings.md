# ADR-0003: 源代码生成器替代反射

- 状态：已接受
- 日期：2026-07-13
- 决策者：Wails.Net 团队

## 背景

Wails v3 (Go) 的绑定系统在运行时通过反射（`reflect` 包）查找方法并调用。直接将此模式移植到 .NET 会带来以下问题：

1. **AOT 与裁剪不兼容**：`MethodInfo.Invoke` 依赖运行时反射元数据，在 Native AOT 与 trimming 场景下会被裁剪器警告（IL2070/IL2026），可能导致发布后调用失败。AGENTS.md 3.4 节明确要求"禁止使用反射获取对应方法"。
2. **性能损耗**：每次调用都经过 `MethodInfo.Invoke` → 参数装箱 → 解包 `TargetInvocationException`，相比直接方法调用有 10-100 倍性能差距。
3. **参数反序列化重复**：反射调用前需要将 JSON 参数逐个转换为 `object[]`，再交给 `MethodInfo.Invoke`；这与 `System.Text.Json` 的强类型反序列化路径重复。
4. **元数据收集困难**：TypeScript 类型生成、FNV-1a 哈希 ID 计算等任务，在运行时反射下需要扫描整个程序集，启动时间长。
5. **类型安全缺失**：反射调用无法在编译期检查参数类型匹配，错误只能到运行时暴露。

约束：

- 必须保持与 Go 版本 `fnv.New32a()` 一致的 32 位 FNV-1a 哈希（AGENTS.md 6.3 节）。
- 必须同时支持 `[Binding]`（业务方法绑定）与 `[Command("name")]`（插件命令）两种特性。
- 必须支持 AOT 发布，不依赖运行时反射。
- 必须有降级路径：无法被源生成器覆盖的场景（如动态注册的命令）仍需可调用。

## 决策

引入 `IIncrementalGenerator` 源生成器，在编译期生成强类型调用器，替代运行时反射 `MethodInfo.Invoke`。

### 1. 源生成器扫描特性

参见 [BindingSourceGenerator.cs](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs)：

- 通过 `context.SyntaxProvider.ForAttributeWithMetadataName` 增量扫描 `[Binding]` 与 `[Command]` 特性。
- 使用增量管道（`Collect` + `Combine` + `RegisterSourceOutput`），仅在受影响时重新生成，避免全量重建。
- 跳过抽象方法、泛型方法、静态方法（这些无法生成强类型调用器）。

### 2. GeneratedBindingRegistry + [ModuleInitializer] 自动注册

参见 [GeneratedBindingRegistry.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)：

- 源生成器生成 `GeneratedBindingsRegistration` 静态类，标记 `[ModuleInitializer]`，在模块加载时自动调用 `GeneratedBindingRegistry.Register(fullName, invoker, typeFullName)`。
- `BindingManager` 在注册服务时优先查询 `GeneratedBindingRegistry.TryGetInvoker`，命中则使用强类型调用器，未命中再回退到反射路径（用于动态注册场景）。
- 注册表维护三个字典：`_invokers`（方法全名 → 调用器委托）、`_methodToTypeName`（方法全名 → 类型全名）、`_typeMethods`（类型全名 → 方法名列表，供 `BindingManager` 快速判断某类型是否有生成调用器）。

### 3. 编译期 FNV-1a 哈希计算

源生成器在生成代码时同步计算 FNV-1a 哈希：

```csharp
const uint offsetBasis = 2166136261u;
const uint prime = 16777619u;
```

与 `Wails.Net.Application.Bindings.BindingManager.FNV1aHash` 和 `Wails.Net.Generator.BindingIdGenerator.Generate` 完全一致，确保前后端绑定 ID 跨实现一致。

### 4. 表达式树编译器作为补充

参见 [CommandInvokerCompiler.cs](../../src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs)：

- 对于无法在编译期被源生成器覆盖的场景（如运行时通过 `CommandRegistry.Register` 动态注册的命令），使用 `System.Linq.Expressions` 将 `MethodInfo` 编译为 `CompiledCommandInvoker` 委托。
- 编译结果缓存在 `ConcurrentDictionary<MethodInfo, CompiledCommandInvoker?>`，仅编译一次，后续调用零反射。
- 参数绑定策略：`ICommandContext` → 注入 ctx；`CancellationToken` → 注入 `ctx.CancellationToken`；`IServiceProvider` → 注入 `ctx.Services`；其他类型 → `JsonSerializer.Deserialize` 反序列化。

### 5. 元数据同步生成

源生成器同时生成 `GeneratedBindingsMetadata.Register(BoundMethodInfo)` 调用，包含方法全名、FNV-1a ID、命名空间、类名、方法名、参数列表（含 TypeScript 类型）、返回类型、是否异步、是否命令。供 `BindingAnalyzer` 和 `TypeScriptGenerator` 使用，替代运行时反射分析。

## 后果

**正面影响**：

- **AOT 兼容**：强类型调用器是直接方法调用，无 `MethodInfo.Invoke`，通过 AOT 裁剪分析。`IsAotCompatible` 可启用。
- **零反射调用**：`[ModuleInitializer]` 在模块加载时一次性注册调用器，运行时调用为委托直接调用，性能接近手写代码。
- **编译期类型检查**：源生成器在编译时即可发现参数类型不匹配、方法不存在等问题（通过生成的强类型调用器代码）。
- **元数据免反射**：`TypeScriptGenerator` 不再需要运行时反射分析程序集，启动时间缩短。
- **降级路径完整**：动态注册的命令通过 `CommandInvokerCompiler` 表达式树编译，仍能享受零反射调用的性能优势。

**负面影响**：

- **编译时间增加**：源生成器需要在每次编译时扫描所有 `[Binding]` 与 `[Command]` 特性，大型项目可能增加 1-3 秒编译时间。
- **调试复杂度**：生成的代码在 `WailsGeneratedBindings.g.cs` 中，调试时需要找到对应生成的调用器方法；IDE 中可通过 "Go to Source Generator Output" 查看。
- **`[ModuleInitializer]` 时机约束**：模块初始化器在类型首次被访问时执行，必须保证 `GeneratedBindingRegistry` 静态字典线程安全（当前使用普通 `Dictionary`，依赖 CLR 模块加载的线程安全保证；若并行加载模块需重新评估）。
- **生成器与运行时哈希必须严格同步**：`ComputeFnv1aHash` 在源生成器中重复实现，必须与运行时版本保持一致，否则绑定 ID 不匹配；当前通过单元测试 `FNV1aHash_ReturnsConsistentValue` 保证。
- **动态命令仍需反射元数据**：`CommandInvokerCompiler.Compile` 仍接收 `MethodInfo`，无法完全消除运行时反射（仅在降级路径）。

## 考虑过的替代方案

1. **动态代码生成（`System.Reflection.Emit`）**：放弃。`Emit` 不支持 AOT，且生成的动态程序集难以调试；表达式树（`System.Linq.Expressions`）是 `Emit` 的高级封装，已通过 `CommandInvokerCompiler` 用于降级路径。
2. **表达式树-only（不引入源生成器）**：放弃。表达式树仍需要运行时 `MethodInfo` 作为输入，无法消除启动时反射扫描；且无法在编译期生成 TypeScript 元数据。源生成器在编译期完成所有工作，运行时零反射。
3. **保持运行时反射**：放弃。AGENTS.md 3.4 节明确禁止"使用反射获取对应方法"；且反射路径无法通过 AOT 裁剪分析，发布后可能运行时失败。
4. **Castle DynamicProxy / LinFu.DynamicProxy 等动态代理**：放弃。这些库依赖 `Emit` 或反射，同样不支持 AOT；且引入第三方依赖增加攻击面。
5. **Source Generator + Roslyn 编译期 IL Weaving（如 Fody）**：放弃。Fody 通过 MSBuild 任务修改编译后的 IL，与源生成器路线重复；且 Fody 社区活跃度下降，部分插件未适配 .NET 10。源生成器是官方推荐的编译期代码生成方案。
6. **`System.Text.Json` 源生成器（`JsonSerializerContext`）**：部分采纳。生成的调用器内部使用 `args[i].Deserialize<T>(JsonOptions.DefaultSerializerOptions)`，未来可进一步引入 `JsonSerializerContext` 替换运行时反射式反序列化，进一步优化 AOT 兼容性。

## 相关文件

- [BindingSourceGenerator.cs](../../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs)
- [GeneratedBindingRegistry.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingRegistry.cs)
- [GeneratedBindingsMetadata.cs](../../src/Wails.Net.Application/Bindings/GeneratedBindingsMetadata.cs)
- [CommandInvokerCompiler.cs](../../src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs)
- [BindingAttribute.cs](../../src/Wails.Net.Application/Bindings/BindingAttribute.cs)
- [CommandAttribute.cs](../../src/Wails.Net.Application/Commands/CommandAttribute.cs)
- [docs/implementation/source-generators.md](../implementation/source-generators.md)
- [docs/architecture/binding-and-command-system.md](../architecture/binding-and-command-system.md)
