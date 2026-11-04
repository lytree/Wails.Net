using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Wails.Net.SourceGenerators;

/// <summary>
/// 安全诊断分析器（SEC-H-002）。
/// 检测将带 <c>[RequireCapability]</c> / <c>[ScopeParameter]</c> 特性的方法通过
/// <c>MapCommand</c> / <c>MapCommandAsync</c> 注册为命令时，未传入 <see cref="CommandSecurity"/> 的场景，
/// 发射 WNET001 / WNET002 警告（在 <c>Directory.Build.props</c> 的 <c>TreatWarningsAsErrors=true</c> 下即构建失败）。
/// <para>
/// 背景：插件在方法参数上加 <c>[ScopeParameter]</c>、在方法上加 <c>[RequireCapability]</c> 声明安全约束，
/// 但若通过 <c>MapCommand</c> 注册时未传 <c>CommandSecurity</c>，这些声明在运行时会被静默丢弃
///（<see cref="CommandDispatcher"/> 只读取注册条目上的能力/Scope 声明），造成"声明与执行两张皮"的安全缺口。
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MapCommandSecurityDiagnosticAnalyzer : DiagnosticAnalyzer
{
    /// <summary>命令安全类型全名。</summary>
    private const string CommandSecurityType = "Wails.Net.Application.Commands.CommandSecurity";

    /// <summary>命令注册扩展类名。</summary>
    private const string MapCommandExtensionsType = "Wails.Net.Application.Commands.MapCommandExtensions";

    /// <summary>能力要求特性全名。</summary>
    private const string RequireCapabilityAttribute = "Wails.Net.Application.Security.RequireCapabilityAttribute";

    /// <summary>参数作用域特性全名。</summary>
    private const string ScopeParameterAttribute = "Wails.Net.Application.Security.ScopeParameterAttribute";

    /// <summary>
    /// WNET001：方法标记了 <c>[RequireCapability]</c>，但 MapCommand 调用未传 <c>CommandSecurity</c>。
    /// </summary>
    public static readonly DiagnosticDescriptor Wnet001 = new(
        id: "WNET001",
        title: "命令缺少能力声明",
        messageFormat: "方法 '{0}' 标记了 [RequireCapability]，但 MapCommand 调用未传入 CommandSecurity，能力声明将被运行时丢弃，请补充 CommandSecurity.Require(...)",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "带 [RequireCapability] 的方法经 MapCommand 注册时若未传 CommandSecurity，编译期声明的能力要求不会生效。");

    /// <summary>
    /// WNET002：方法参数标记了 <c>[ScopeParameter]</c>，但 MapCommand 调用未传 <c>CommandSecurity</c>。
    /// </summary>
    public static readonly DiagnosticDescriptor Wnet002 = new(
        id: "WNET002",
        title: "命令缺少作用域声明",
        messageFormat: "方法 '{0}' 的参数标记了 [ScopeParameter]，但 MapCommand 调用未传入 CommandSecurity，作用域声明将被运行时丢弃，请补充 CommandSecurity.WithScope(...)",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "带 [ScopeParameter] 参数的方法经 MapCommand 注册时若未传 CommandSecurity，编译期声明的参数级 Scope 校验不会生效。");

    /// <summary>
    /// WNET003：MapCommand 调用未传入 CommandSecurity（R3 落地）。
    /// <para>
    /// 仅当目标 MapCommand / MapCommandAsync 重载已声明 CommandSecurity? 形参但调用未提供该实参时报告。
    /// 不接受 security 形参的旧重载（迁移期尚未被废弃）不会触发本诊断，避免对存量注册路径产生噪声。
    /// </para>
    /// <para>
    /// 修复方向：在 MapCommand 调用末尾追加 CommandSecurity.Require("xxx:cap").WithScope(...)
    /// 或 CommandSecurity.Public()；或迁移到 R3 之后将被废弃的不带 security 形参的重载。
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor Wnet003 = new(
        id: "WNET003",
        title: "MapCommand 调用未声明 CommandSecurity",
        messageFormat: "命令 `{0}` 通过 MapCommand 调用时未传入 CommandSecurity；建议补充 CommandSecurity.Require(...) 声明所需 capability 或 CommandSecurity.Public() 标记为公开命令，以便 Strict + DenyByDefault 模式下命令仍可被调用",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "MapCommand 调用未传入 CommandSecurity 时，运行时不读取任何 capability/scope 声明；Strict + DenyByDefault 模式下未声明的命令将被拒绝。");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Wnet001, Wnet002, Wnet003);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    /// <summary>
    /// 分析 MapCommand / MapCommandAsync 调用，检查是否缺少 CommandSecurity。
    /// </summary>
    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method is null || !IsMapCommandMethod(method))
        {
            return;
        }

        // 目标重载是否声明了 CommandSecurity 形参：未声明则不能诊断（旧重载没有该参数）。
        var hasSecurityParameter = HasCommandSecurityParameter(method);

        // 已传入 CommandSecurity（任意参数为该类型）则不诊断
        var hasSecurityArgument = false;
        foreach (var arg in invocation.Arguments)
        {
            if (IsCommandSecurityType(arg.Parameter?.Type))
            {
                hasSecurityArgument = true;
                break;
            }
        }

        // WNET003：重载支持 security 但调用未传入。命令名取自第一个 string 实参。
        if (hasSecurityParameter && !hasSecurityArgument)
        {
            var commandName = TryGetCommandName(invocation);
            context.ReportDiagnostic(Diagnostic.Create(
                Wnet003, invocation.Syntax.GetLocation(), commandName ?? "<unknown>"));
        }

        // 遍历各参数，解析委托引用到的方法，检查其安全特性。
        // 已传入 CommandSecurity 时，方法上的 [RequireCapability] / 参数上的 [ScopeParameter]
        // 会在运行时被读取，不属于"声明被丢弃"场景，故不报告 WNET001 / WNET002。
        if (hasSecurityArgument)
        {
            return;
        }

        foreach (var arg in invocation.Arguments)
        {
            var target = ResolveTargetMethod(arg.Value);
            if (target is null)
            {
                continue;
            }

            var hasCapability = HasAttribute(target, RequireCapabilityAttribute);
            var hasScope = HasScopeParameter(target);

            if (hasCapability)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Wnet001, invocation.Syntax.GetLocation(), target.ToDisplayString()));
            }

            if (hasScope)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Wnet002, invocation.Syntax.GetLocation(), target.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// 判断方法是否为 <c>MapCommandExtensions</c> 上的 MapCommand / MapCommandAsync 扩展方法。
    /// </summary>
    private static bool IsMapCommandMethod(IMethodSymbol method)
    {
        if (method.Name != "MapCommand" && method.Name != "MapCommandAsync")
        {
            return false;
        }

        return method.ContainingType.ToDisplayString() == MapCommandExtensionsType;
    }

    /// <summary>
    /// 从操作中解析被引用的目标方法符号。
    /// 处理方法组转委托（<c>IConversionOperation</c>）、括号运算与方法引用。
    /// Lambda 表达式返回其符号（Lambda 参数无法声明特性，用于排除误报）。
    /// </summary>
    private static IMethodSymbol? ResolveTargetMethod(IOperation? operation)
    {
        while (operation is not null)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    break;
                case IMethodReferenceOperation methodReference:
                    return methodReference.Method;
                case IDelegateCreationOperation delegateCreation:
                    return TryGetDelegateTargetMethod(delegateCreation);
                case IAnonymousFunctionOperation anonymous:
                    return anonymous.Symbol;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// 从委托创建操作中提取目标方法。
    /// <para>
    /// 处理 <c>(Func&lt;T,R&gt;)Method</c> 方法组转委托：<c>IDelegateCreationOperation.Target</c> 为
    /// <c>IMethodReferenceOperation</c>；处理 <c>CommandSecurity.Require(...)</c> 等 <c>IInvocationOperation</c>
    /// 返回 null（非委托方法引用，避免误报）。
    /// </para>
    /// </summary>
    private static IMethodSymbol? TryGetDelegateTargetMethod(IDelegateCreationOperation delegateCreation)
    {
        // 方法组转委托：Target 为 IMethodReferenceOperation
        if (delegateCreation.Target is IMethodReferenceOperation methodRef)
        {
            return methodRef.Method;
        }

        // 匿名函数转委托：Symbol 为 lambda 方法
        if (delegateCreation.Target is IAnonymousFunctionOperation anonymous)
        {
            return anonymous.Symbol;
        }

        return null;
    }

    /// <summary>
    /// 判断类型是否为 <c>CommandSecurity</c>（解包可空 <c>CommandSecurity?</c> 后比较）。
    /// </summary>
    private static bool IsCommandSecurityType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        // 解包 Nullable<T>（值类型可空），以兼容 CommandSecurity? 的可空引用类型注解
        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        // 忽略可空引用类型的 '?' 注解标记
        var display = type.ToDisplayString();
        return display.TrimEnd('?') == CommandSecurityType;
    }

    /// <summary>
    /// 判断方法是否标记了指定全名特性。
    /// </summary>
    private static bool HasAttribute(IMethodSymbol method, string attributeFullName)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == attributeFullName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断方法的任一参数是否标记了 <c>[ScopeParameter]</c>。
    /// </summary>
    private static bool HasScopeParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == ScopeParameterAttribute)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 判断方法是否声明了 <c>CommandSecurity?</c> 形参。
    /// 静态助手，供 WNET003 在重载筛选时使用；不在重载上做反射，符号由 Roslyn 在编译期提供。
    /// </summary>
    private static bool HasCommandSecurityParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (IsCommandSecurityType(parameter.Type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从 MapCommand 调用的实参中提取命令名。
    /// 约定：第一个 string 实参为 name；找不到时返回 null，诊断用 <c>&lt;unknown&gt;</c> 占位。
    /// </summary>
    private static string? TryGetCommandName(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is null)
            {
                continue;
            }

            if (argument.Parameter.Type.SpecialType == SpecialType.System_String)
            {
                return ExtractConstantString(argument.Value);
            }
        }

        return null;
    }

    /// <summary>
    /// 从运算中提取常量字符串。仅支持字面量 / 隐式转换后的字符串字面量；
    /// 其余形式（如 const 字段、变量拼接）返回 null，分析器不杜撰内容。
    /// </summary>
    private static string? ExtractConstantString(IOperation? operation)
    {
        while (operation is not null)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    break;
                case ILiteralOperation literal when literal.ConstantValue.HasValue && literal.ConstantValue.Value is string s:
                    return s;
                default:
                    return null;
            }
        }

        return null;
    }
}


