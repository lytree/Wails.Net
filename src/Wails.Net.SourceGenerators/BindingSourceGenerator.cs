using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Wails.Net.SourceGenerators;

/// <summary>
/// 源代码生成器，扫描标记了 [Binding] 或 [Command] 特性的方法，
/// 生成强类型调用代码，替代运行时反射 MethodInfo.Invoke。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BindingSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// 标记方法的信息，用于在生成器管道中传递。
    /// </summary>
    private sealed record MethodMarker(
        IMethodSymbol MethodSymbol,
        string AttributeName,
        string? CustomName);

    /// <summary>
    /// 初始化生成器，注册语法提供者。
    /// </summary>
    /// <param name="context">增量生成器上下文。</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 扫描标记了 [Binding] 特性的方法
        var bindingMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Wails.Net.Application.Bindings.BindingAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var methodSymbol = (IMethodSymbol)ctx.TargetSymbol;
                    var customName = ExtractNamedArgument(ctx.Attributes.FirstOrDefault(), "Name");
                    return new MethodMarker(methodSymbol, "Binding", customName);
                })
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // 扫描标记了 [Command] 特性的方法
        var commandMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Wails.Net.Application.Commands.CommandAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var methodSymbol = (IMethodSymbol)ctx.TargetSymbol;
                    // CommandAttribute 构造函数第一个参数为命令名
                    var customName = ExtractConstructorArgument(ctx.Attributes.FirstOrDefault(), 0);
                    return new MethodMarker(methodSymbol, "Command", customName);
                })
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // 合并两个来源
        var allMarkers = bindingMethods.Collect().Combine(commandMethods.Collect())
            .SelectMany(static (pair, _) => pair.Left.Concat(pair.Right));

        // 收集并去重
        var collected = allMarkers.Collect();

        // 与 CompilationProvider 结合以获取完整类型符号信息（用于生成完全限定类型名）
        var withCompilation = collected.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(withCompilation, static (spc, pair) =>
        {
            var (markers, compilation) = pair;
            if (markers.IsDefaultOrEmpty)
            {
                return;
            }

            // 按方法符号唯一性去重（同一方法可能因部分类型被多次访问而出现重复）
            var distinctMarkers = markers
                .Distinct(MethodMarkerEqualityComparer.Instance)
                .ToList();

            if (distinctMarkers.Count == 0)
            {
                return;
            }

            var generatedCode = GenerateCode(distinctMarkers);
            if (!string.IsNullOrEmpty(generatedCode))
            {
                spc.AddSource("WailsGeneratedBindings.g.cs", generatedCode);
            }
        });
    }

    /// <summary>
    /// 从特性中提取指定名称的命名参数值。
    /// </summary>
    private static string? ExtractNamedArgument(AttributeData? attr, string name)
    {
        if (attr is null)
        {
            return null;
        }

        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is string s)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// 从特性中提取指定位置的构造函数参数值。
    /// </summary>
    private static string? ExtractConstructorArgument(AttributeData? attr, int index)
    {
        if (attr is null || attr.ConstructorArguments.Length <= index)
        {
            return null;
        }

        return attr.ConstructorArguments[index].Value as string;
    }

    /// <summary>
    /// 生成调用器类代码。
    /// </summary>
    private static string GenerateCode(List<MethodMarker> markers)
    {
        var methodInfos = new List<MethodSymbolInfo>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var marker in markers)
        {
            var methodSymbol = marker.MethodSymbol;

            // 跳过抽象方法、泛型方法、静态方法
            if (methodSymbol.IsAbstract || methodSymbol.IsGenericMethod || methodSymbol.IsStatic)
            {
                continue;
            }

            var containingType = methodSymbol.ContainingType;
            if (containingType is null)
            {
                continue;
            }

            var fullTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var typeName = containingType.Name;
            var methodName = methodSymbol.Name;
            var isCommand = marker.AttributeName == "Command";

            // 确定绑定名
            string bindingName;
            if (isCommand)
            {
                // [Command("name")]：必须有名字
                bindingName = marker.CustomName ?? $"{typeName}.{methodName}";
            }
            else
            {
                // [Binding(Name = "alias")] 或 [Binding]
                bindingName = marker.CustomName ?? methodName;
            }

            // 全限定名（Namespace.ClassName.MethodName）
            var namespaceName = containingType.ContainingNamespace?.ToDisplayString() ?? "";
            var fullName = isCommand
                ? bindingName
                : $"{namespaceName}.{typeName}.{methodName}";

            // 短名称（ClassName.MethodName）
            var shortName = $"{typeName}.{methodName}";

            // 生成唯一的调用器方法名
            var invokerMethodName = MakeUniqueName($"{typeName}_{methodName}", usedNames);

            methodInfos.Add(new MethodSymbolInfo(
                methodSymbol, fullTypeName, typeName, methodName, namespaceName,
                fullName, shortName, bindingName, isCommand, invokerMethodName));
        }

        if (methodInfos.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// 此代码由 Wails.Net.SourceGenerators 自动生成，请勿手动修改。");
        sb.AppendLine("// 扫描 [Binding] 和 [Command] 特性，生成强类型调用器，替代运行时反射。");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Wails.Net.Application.Bindings;");
        sb.AppendLine();
        sb.AppendLine("namespace Wails.Net.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 由源代码生成器自动生成的强类型调用器注册入口。");
        sb.AppendLine("    /// 通过 [ModuleInitializer] 在模块加载时自动注册到 GeneratedBindingRegistry，");
        sb.AppendLine("    /// 替代运行时反射 MethodInfo.Invoke，提升性能并支持 AOT 裁剪。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class GeneratedBindingsRegistration");
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        public static void Register()");
        sb.AppendLine("        {");

        foreach (var info in methodInfos)
        {
            // 类型全名（Namespace.ClassName），用于运行时查找实例
            var typeFullNameStr = info.NamespaceName.Length > 0
                ? $"{info.NamespaceName}.{info.TypeName}"
                : info.TypeName;

            // 全限定名注册
            sb.AppendLine($"            GeneratedBindingRegistry.Register(");
            sb.AppendLine($"                \"{EscapeString(info.FullName)}\",");
            sb.AppendLine($"                {info.InvokerMethodName},");
            sb.AppendLine($"                \"{EscapeString(typeFullNameStr)}\");");

            // 对于 [Binding] 特性的方法，同时注册短名称作为别名
            if (!info.IsCommand && info.ShortName != info.FullName)
            {
                sb.AppendLine($"            GeneratedBindingRegistry.Register(");
                sb.AppendLine($"                \"{EscapeString(info.ShortName)}\",");
                sb.AppendLine($"                {info.InvokerMethodName},");
                sb.AppendLine($"                \"{EscapeString(typeFullNameStr)}\");");
            }

            // 对于 [Command] 特性的方法，命令名已与 fullName 相同（直接使用自定义名）
            // 但如果指定了自定义名，同时注册 "ClassName.MethodName" 短名称作为别名
            if (info.IsCommand && info.CustomName != info.ShortName)
            {
                sb.AppendLine($"            GeneratedBindingRegistry.Register(");
                sb.AppendLine($"                \"{EscapeString(info.ShortName)}\",");
                sb.AppendLine($"                {info.InvokerMethodName},");
                sb.AppendLine($"                \"{EscapeString(typeFullNameStr)}\");");
            }

            // 同步注册元数据（供 BindingAnalyzer / TypeScriptGenerator 使用，替代运行时反射分析）
            AppendMetadataRegistration(sb, info, typeFullNameStr);
        }

        sb.AppendLine("        }");

        // 生成每个调用器方法
        foreach (var info in methodInfos)
        {
            GenerateInvokerMethod(sb, info);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 转义字符串中的特殊字符，用于生成的字符串字面量。
    /// </summary>
    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// 为指定方法生成 <see cref="GeneratedBindingsMetadata.Register(BoundMethodInfo)"/> 调用。
    /// 此元数据替代运行时反射分析，供 <c>BindingAnalyzer</c> 和 <c>TypeScriptGenerator</c> 使用。
    /// </summary>
    private static void AppendMetadataRegistration(StringBuilder sb, MethodSymbolInfo info, string typeFullNameStr)
    {
        var methodSymbol = info.MethodSymbol;
        var returnType = methodSymbol.ReturnType;
        var isVoid = returnType.SpecialType == SpecialType.System_Void;
        var isTask = returnType.IsTaskReturnType();

        // 计算返回类型的 TypeScript 表示
        // 对于异步方法（Task<T>），返回类型为 T 的 TypeScript 表示
        var returnTypeName = isVoid
            ? "void"
            : (isTask ? MapReturnTypeForTask(returnType) : MapTypeToTypeScript(returnType));
        var isAsync = isTask;

        // 计算 FNV-1a 哈希 ID（与运行时 BindingManager.FNV1aHash 一致）
        var id = ComputeFnv1aHash(info.FullName);

        // 生成参数元数据数组
        var paramBuilder = new StringBuilder();
        paramBuilder.Append("            new BoundParameterInfo[]");
        paramBuilder.AppendLine();
        paramBuilder.Append("            {");
        paramBuilder.AppendLine();

        var parameters = methodSymbol.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramTypeStr = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isCancellationToken = paramTypeStr == "global::System.Threading.CancellationToken";
            // params 关键字：参数有 ParamArrayAttribute
            var isVariadic = param.IsParams && !isCancellationToken;
            var tsParamType = isCancellationToken ? "void" : MapTypeToTypeScript(param.Type);
            var paramName = string.IsNullOrEmpty(param.Name) ? "arg" + i : param.Name;

            paramBuilder.Append($"                new BoundParameterInfo(\"{EscapeString(paramName)}\", \"{EscapeString(tsParamType)}\", {(isVariadic ? "true" : "false")}, {(isCancellationToken ? "true" : "false")})");
            if (i < parameters.Length - 1)
            {
                paramBuilder.Append(",");
            }
            paramBuilder.AppendLine();
        }

        paramBuilder.Append("            }");

        sb.AppendLine($"            GeneratedBindingsMetadata.Register(");
        sb.AppendLine($"                new BoundMethodInfo(");
        sb.AppendLine($"                    FullName: \"{EscapeString(info.FullName)}\",");
        sb.AppendLine($"                    Id: {id}u,");
        sb.AppendLine($"                    Namespace: \"{EscapeString(info.NamespaceName)}\",");
        sb.AppendLine($"                    ClassName: \"{EscapeString(info.TypeName)}\",");
        sb.AppendLine($"                    MethodName: \"{EscapeString(info.MethodName)}\",");
        sb.AppendLine($"                    Parameters: {paramBuilder},");
        sb.AppendLine($"                    ReturnTypeName: \"{EscapeString(returnTypeName)}\",");
        sb.AppendLine($"                    IsAsync: {(isAsync ? "true" : "false")},");
        sb.AppendLine($"                    IsCommand: {(info.IsCommand ? "true" : "false")}));");
    }

    /// <summary>
    /// 计算 FNV-1a 32 位哈希值。
    /// 与 <c>Wails.Net.Generator.BindingIdGenerator.Generate</c> 和
    /// <c>Wails.Net.Application.Bindings.BindingManager.FNV1aHash</c> 完全一致。
    /// </summary>
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

    /// <summary>
    /// 将 <see cref="ITypeSymbol"/> 映射为 TypeScript 类型字符串。
    /// 移植自 <c>Wails.Net.Generator.TypeScriptTypeMapper.MapType</c>，
    /// 适配 Roslyn 符号模型而非 <see cref="System.Type"/>。
    /// </summary>
    private static string MapTypeToTypeScript(ITypeSymbol type)
    {
        if (type is null)
        {
            return "unknown";
        }

        // 处理 Nullable<T> 或可空引用类型
        var (underlying, isNullableAnnotation) = UnwrapNullable(type);
        var nullableSuffix = isNullableAnnotation ? " | null" : string.Empty;

        if (underlying is not null)
        {
            // Nullable<T> 解包后递归
            return MapTypeToTypeScript(underlying) + nullableSuffix;
        }

        // 处理基元类型
        switch (type.SpecialType)
        {
            case SpecialType.System_Void: return "void";
            case SpecialType.System_Boolean: return "boolean";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return "number";
            case SpecialType.System_Char:
            case SpecialType.System_String: return "string";
            case SpecialType.System_Object: return "unknown";
        }

        // 处理枚举
        if (type.TypeKind == TypeKind.Enum)
        {
            return type.Name;
        }

        // 处理数组
        if (type is IArrayTypeSymbol arrayType)
        {
            return MapTypeToTypeScript(arrayType.ElementType) + "[]";
        }

        // 处理泛型类型
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.ConstructedFrom;
            var defFullName = (def.ContainingNamespace?.ToDisplayString() ?? "") + "." + def.Name;

            // List<T> / IList<T> / IEnumerable<T> 等
            if (defFullName == "System.Collections.Generic.List" ||
                defFullName == "System.Collections.Generic.IList" ||
                defFullName == "System.Collections.Generic.IEnumerable" ||
                defFullName == "System.Collections.Generic.IReadOnlyList" ||
                defFullName == "System.Collections.Generic.ICollection" ||
                defFullName == "System.Collections.Generic.IReadOnlyCollection" ||
                defFullName == "System.Collections.Immutable.ImmutableArray" ||
                defFullName == "System.Collections.Immutable.ImmutableList")
            {
                if (named.TypeArguments.Length > 0)
                {
                    return MapTypeToTypeScript(named.TypeArguments[0]) + "[]";
                }
            }

            // Dictionary<K,V> / IDictionary<K,V> / IReadOnlyDictionary<K,V>
            if (defFullName == "System.Collections.Generic.Dictionary" ||
                defFullName == "System.Collections.Generic.IDictionary" ||
                defFullName == "System.Collections.Generic.IReadOnlyDictionary" ||
                defFullName == "System.Collections.Immutable.ImmutableDictionary")
            {
                if (named.TypeArguments.Length == 2)
                {
                    return $"Record<{MapTypeToTypeScript(named.TypeArguments[0])}, {MapTypeToTypeScript(named.TypeArguments[1])}>";
                }
            }

            // Task<T> / ValueTask<T> — 返回 T 的类型
            if (defFullName == "System.Threading.Tasks.Task" ||
                defFullName == "System.Threading.Tasks.ValueTask")
            {
                if (named.TypeArguments.Length > 0)
                {
                    return MapTypeToTypeScript(named.TypeArguments[0]);
                }
            }

            // Tuple<...>
            if (defFullName == "System.Tuple")
            {
                var args = named.TypeArguments.Select(MapTypeToTypeScript);
                return $"[{string.Join(", ", args)}]";
            }

            // KeyValuePair<K, V> → [K, V]
            if (defFullName == "System.Collections.Generic.KeyValuePair" && named.TypeArguments.Length == 2)
            {
                return $"[{MapTypeToTypeScript(named.TypeArguments[0])}, {MapTypeToTypeScript(named.TypeArguments[1])}]";
            }
        }

        // 处理 Task / ValueTask（无泛型参数）
        var fullName2 = (type.ContainingNamespace?.ToDisplayString() ?? "") + "." + type.Name;
        if (fullName2 == "System.Threading.Tasks.Task" || fullName2 == "System.Threading.Tasks.ValueTask")
        {
            return "void";
        }

        // 处理 CancellationToken
        if (fullName2 == "System.Threading.CancellationToken")
        {
            return "void";
        }

        // 处理 DateTime / DateTimeOffset / TimeSpan / Guid / Uri
        if (fullName2 == "System.DateTime" ||
            fullName2 == "System.DateTimeOffset" ||
            fullName2 == "System.TimeSpan" ||
            fullName2 == "System.Guid" ||
            fullName2 == "System.Uri")
        {
            return "string";
        }

        // 处理 JsonElement / JsonDocument
        if (fullName2 == "System.Text.Json.JsonElement" ||
            fullName2 == "System.Text.Json.JsonDocument")
        {
            return "unknown";
        }

        // 默认：使用类型名（去掉泛型后缀）
        var typeName = type.Name;
        var backtickIndex = typeName.IndexOf('`');
        if (backtickIndex > 0)
        {
            typeName = typeName[..backtickIndex];
        }
        return typeName;
    }

    /// <summary>
    /// 解包 Nullable&lt;T&gt; 或可空引用类型，返回底层类型与是否可空标记。
    /// </summary>
    private static (ITypeSymbol? underlying, bool isNullable) UnwrapNullable(ITypeSymbol type)
    {
        // Nullable<T> 是泛型结构体
        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            return (named.TypeArguments[0], true);
        }

        // 可空引用类型（如 string?）：通过 NullableAnnotation 标记
        // 同一类型的可空与非可空版本共享 OriginalDefinition，仅 NullableAnnotation 不同
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var nonNullable = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            return (nonNullable, true);
        }

        return (null, false);
    }

    /// <summary>
    /// 对异步方法的返回类型应用 TypeScript 映射。
    /// 输入为 Task 或 Task&lt;T&gt;，输出为 T 的 TypeScript 表示（Task 无 T 时为 "void"）。
    /// </summary>
    private static string MapReturnTypeForTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { IsGenericType: true } named)
        {
            // Task<T>
            return MapTypeToTypeScript(named.TypeArguments[0]);
        }
        // Task（无泛型参数）
        return "void";
    }

    /// <summary>
    /// 生成唯一的调用器方法名。
    /// </summary>
    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        var name = baseName;
        var suffix = 1;
        while (!usedNames.Add(name))
        {
            name = $"{baseName}_{suffix}";
            suffix++;
        }
        return name;
    }

    /// <summary>
    /// 生成单个调用器方法。
    /// </summary>
    private static void GenerateInvokerMethod(StringBuilder sb, MethodSymbolInfo info)
    {
        var methodSymbol = info.MethodSymbol;
        var returnType = methodSymbol.ReturnType;
        var isVoid = returnType.SpecialType == SpecialType.System_Void;
        var isTask = returnType.IsTaskReturnType();

        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 强类型调用器：{(info.IsCommand ? "命令" : "绑定")} {info.TypeName}.{info.MethodName}");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static object? {info.InvokerMethodName}(");
        sb.AppendLine($"            object instance,");
        sb.AppendLine($"            JsonElement[] args,");
        sb.AppendLine($"            CancellationToken cancellationToken)");
        sb.AppendLine("        {");

        // 处理 void 实例方法（非异步）
        if (isVoid && !isTask)
        {
            sb.AppendLine($"            var svc = ({info.FullTypeName})instance;");
            GenerateArgumentsAndCall(sb, info, isVoid: true, isTask: false);
        }
        else if (isTask)
        {
            // 异步方法返回 Task 或 Task<T>
            sb.AppendLine($"            var svc = ({info.FullTypeName})instance;");
            GenerateArgumentsAndCall(sb, info, isVoid: false, isTask: true);
        }
        else
        {
            // 同步方法返回值类型
            sb.AppendLine($"            var svc = ({info.FullTypeName})instance;");
            GenerateArgumentsAndCall(sb, info, isVoid: false, isTask: false);
        }

        sb.AppendLine("        }");
    }

    /// <summary>
    /// 生成参数反序列化和方法调用代码。
    /// </summary>
    private static void GenerateArgumentsAndCall(StringBuilder sb, MethodSymbolInfo info, bool isVoid, bool isTask)
    {
        var methodSymbol = info.MethodSymbol;
        var parameters = methodSymbol.Parameters;
        var callArgs = new List<string>();
        var argIndex = 0;

        foreach (var param in parameters)
        {
            if (param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken")
            {
                callArgs.Add("cancellationToken");
            }
            else
            {
                var paramTypeStr = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isNullable = param.Type.IsNullable();
                var defaultValue = isNullable ? "null" : $"default({paramTypeStr})!";
                // 非可空类型在反序列化后追加 null-forgiving 运算符 "!"，
                // 避免编译器对 Deserialize<T>? 返回值赋给非可空参数发出 CS8604 警告。
                var nullForgiving = isNullable ? "" : "!";

                sb.AppendLine($"            var p{argIndex} = args.Length > {argIndex} && !args[{argIndex}].ValueKind.Equals(JsonValueKind.Null)");
                sb.AppendLine($"                ? args[{argIndex}].Deserialize<{paramTypeStr}>(JsonOptions.DefaultSerializerOptions){nullForgiving}");
                sb.AppendLine($"                : {defaultValue};");
                callArgs.Add($"p{argIndex}");
                argIndex++;
            }
        }

        var callExpr = $"svc.{info.MethodName}({string.Join(", ", callArgs)})";

        if (isVoid)
        {
            sb.AppendLine($"            {callExpr};");
            sb.AppendLine("            return null;");
        }
        else if (isTask)
        {
            // 异步方法返回 Task 或 Task<T>，返回 Task 实例由调用方 await
            sb.AppendLine($"            return {callExpr};");
        }
        else
        {
            sb.AppendLine($"            return {callExpr};");
        }
    }

    /// <summary>
    /// 方法符号信息包装。
    /// </summary>
    private sealed record MethodSymbolInfo(
        IMethodSymbol MethodSymbol,
        string FullTypeName,
        string TypeName,
        string MethodName,
        string NamespaceName,
        string FullName,
        string ShortName,
        string CustomName,
        bool IsCommand,
        string InvokerMethodName);

    /// <summary>
    /// MethodMarker 的去重比较器。
    /// </summary>
    private sealed class MethodMarkerEqualityComparer : IEqualityComparer<MethodMarker>
    {
        public static readonly MethodMarkerEqualityComparer Instance = new();

        public bool Equals(MethodMarker? x, MethodMarker? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(x.MethodSymbol, y.MethodSymbol)
                && x.AttributeName == y.AttributeName
                && x.CustomName == y.CustomName;
        }

        public int GetHashCode(MethodMarker obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj.MethodSymbol);
        }
    }
}

/// <summary>
/// ITypeSymbol 扩展方法。
/// </summary>
internal static class TypeSymbolExtensions
{
    /// <summary>
    /// 判断类型是否为 Task 或 Task&lt;T&gt;。
    /// </summary>
    public static bool IsTaskReturnType(this ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var fullName = namedType.ContainingNamespace?.ToDisplayString() + "." + namedType.Name;
        return fullName == "System.Threading.Tasks.Task" || fullName == "System.Threading.Tasks.ValueTask";
    }

    /// <summary>
    /// 判断类型是否为可空引用类型或 Nullable&lt;T&gt;。
    /// </summary>
    public static bool IsNullable(this ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        return type is INamedTypeSymbol { IsGenericType: true } namedType
            && namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T;
    }
}
