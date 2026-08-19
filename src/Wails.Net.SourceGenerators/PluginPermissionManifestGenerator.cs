using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Wails.Net.SourceGenerators;

/// <summary>
/// 插件命令权限清单生成器（OPT-PLUGIN-003）。
/// <para>
/// 对应 Tauri v2 插件的 <c>permissions/&lt;plugin&gt;/default.toml</c> 声明机制：
/// 编译期扫描所有实现 <see cref="IPlugin"/> 的类，收集 <c>Configure</c> 方法体内
/// 对 <c>IPermissionRegistrar.RegisterPermissionSet</c> 与
/// <c>IPermissionRegistrar.DeclarePermission</c> 的字符串字面量调用，
/// 生成 Wails.Net.Generated.WailsGeneratedPermissionManifest.g.cs，
/// 内嵌对应每个插件的 JSON 清单。
/// </para>
/// <para>
/// 生成代码通过 <c>[ModuleInitializer]</c> 在模块加载时调用
/// <c>PluginPermissionManifestRegistry.Register</c>，将清单写入运行时注册表，
/// 供 <c>wails-net check</c> 编译期校验与权限审计查询复用。
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PluginPermissionManifestGenerator : IIncrementalGenerator
{
    private const string IPluginTypeName = "Wails.Net.Application.Plugins.IPlugin";
    private const string IPermissionRegistrarTypeName = "Wails.Net.Application.Plugins.IPermissionRegistrar";
    private const string RegisterPermissionSetMethodName = "RegisterPermissionSet";
    private const string DeclarePermissionMethodName = "DeclarePermission";

    /// <summary>
    /// 初始化生成器。
    /// </summary>
    /// <param name="context">增量生成器上下文。</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pluginManifests = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ExtractPluginManifest(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var aggregatedManifests = pluginManifests.Collect()
            .Select(static (manifests, _) => AggregateManifests(manifests));

        context.RegisterSourceOutput(aggregatedManifests, static (spc, manifests) =>
        {
            if (manifests.Length == 0)
            {
                return;
            }

            var code = GenerateCode(manifests);
            if (!string.IsNullOrEmpty(code))
            {
                spc.AddSource("WailsGeneratedPermissionManifest.g.cs", code);
            }
        });
    }

    private sealed record PluginManifestInfo(
        string PluginName,
        string ContainingNamespace,
        IReadOnlyList<PermissionSetInfo> PermissionSets,
        IReadOnlyList<string> Permissions);

    private sealed record PermissionSetInfo(
        string Identifier,
        string Description,
        IReadOnlyList<string> Permissions);

    private static PluginManifestInfo? ExtractPluginManifest(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (classSymbol is null)
        {
            return null;
        }

        if (!ImplementsInterface(classSymbol, IPluginTypeName))
        {
            return null;
        }

        var pluginName = ExtractPluginName(classSymbol);
        if (string.IsNullOrEmpty(pluginName))
        {
            return null;
        }

        var permissionSets = new List<PermissionSetInfo>();
        var permissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in classDecl.Members)
        {
            if (member is not MethodDeclarationSyntax methodDecl)
            {
                continue;
            }

            if (methodDecl.ParameterList.Parameters.Count != 1)
            {
                continue;
            }

            var firstParamType = ctx.SemanticModel.GetTypeInfo(methodDecl.ParameterList.Parameters[0].Type!, ct).Type;
            if (firstParamType is null ||
                firstParamType.ToDisplayString() != "Wails.Net.Application.Plugins.IPluginContext")
            {
                continue;
            }

            SyntaxNode? body = methodDecl.Body ?? (SyntaxNode?)methodDecl.ExpressionBody;
            if (body is not null)
            {
                ScanMethodBody(ctx.SemanticModel, body, ct, permissionSets, permissions);
            }
        }

        return new PluginManifestInfo(
            PluginName: pluginName,
            ContainingNamespace: classSymbol.ContainingNamespace.ToDisplayString(),
            PermissionSets: permissionSets,
            Permissions: permissions.ToArray());
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceFullName)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == interfaceFullName)
            {
                return true;
            }
        }
        return false;
    }

    private static string? ExtractPluginName(INamedTypeSymbol classSymbol)
    {
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IPropertySymbol property &&
                property.Name == "Name" &&
                property.DeclaredAccessibility == Accessibility.Public &&
                !property.IsStatic)
            {
                var getter = property.GetMethod;
                if (getter is null)
                {
                    continue;
                }

                foreach (var syntaxRef in getter.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxRef.GetSyntax();
                    var literal = TryExtractStringLiteral(syntax);
                    if (literal is not null)
                    {
                        return literal;
                    }
                }
            }
        }
        return null;
    }

    private static string? TryExtractStringLiteral(SyntaxNode syntax)
    {
        if (syntax is ArrowExpressionClauseSyntax arrow)
        {
            return TryStringLiteral(arrow.Expression);
        }
        if (syntax is AccessorDeclarationSyntax accessor && accessor.Body is not null)
        {
            foreach (var stmt in accessor.Body.Statements)
            {
                if (stmt is ReturnStatementSyntax ret)
                {
                    var lit = TryStringLiteral(ret.Expression);
                    if (lit is not null)
                    {
                        return lit;
                    }
                }
            }
        }
        return null;
    }

    private static string? TryStringLiteral(ExpressionSyntax? expr)
    {
        if (expr is LiteralExpressionSyntax literal &&
            (literal.Token.Value is string s))
        {
            return s;
        }
        return null;
    }

    private static void ScanMethodBody(
        SemanticModel semanticModel,
        SyntaxNode body,
        CancellationToken ct,
        List<PermissionSetInfo> permissionSets,
        HashSet<string> permissions)
    {
        foreach (var node in body.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
            if (symbolInfo.Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (method.ContainingType.ToDisplayString() != IPermissionRegistrarTypeName)
            {
                continue;
            }

            switch (method.Name)
            {
                case RegisterPermissionSetMethodName:
                    CollectPermissionSet(semanticModel, invocation, ct, permissionSets, permissions);
                    break;
                case DeclarePermissionMethodName:
                    CollectDeclaredPermission(semanticModel, invocation, ct, permissions);
                    break;
            }
        }
    }

    private static void CollectPermissionSet(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken ct,
        List<PermissionSetInfo> permissionSets,
        HashSet<string> permissions)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
        {
            return;
        }

        var identifier = TryStringLiteralArg(semanticModel, args[0].Expression, ct);
        var description = TryStringLiteralArg(semanticModel, args[1].Expression, ct);
        if (identifier is null || description is null)
        {
            return;
        }

        var permList = new List<string>();
        for (var i = 2; i < args.Count; i++)
        {
            var lit = TryStringLiteralArg(semanticModel, args[i].Expression, ct);
            if (lit is not null)
            {
                permList.Add(lit);
                permissions.Add(lit);
            }
        }

        permissionSets.Add(new PermissionSetInfo(identifier, description, permList));
    }

    private static void CollectDeclaredPermission(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken ct,
        HashSet<string> permissions)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
        {
            return;
        }

        var identifier = TryStringLiteralArg(semanticModel, args[0].Expression, ct);
        if (identifier is null)
        {
            return;
        }

        permissions.Add(identifier);
    }

    private static string? TryStringLiteralArg(SemanticModel semanticModel, ExpressionSyntax expr, CancellationToken ct)
    {
        var constant = semanticModel.GetConstantValue(expr, ct);
        if (constant.HasValue && constant.Value is string s)
        {
            return s;
        }
        return null;
    }

    private static ImmutableArray<PluginManifestInfo> AggregateManifests(
        IEnumerable<PluginManifestInfo> manifests)
    {
        var grouped = new Dictionary<string, PluginManifestInfo>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            if (grouped.TryGetValue(manifest.PluginName, out var existing))
            {
                var mergedSets = existing.PermissionSets.Concat(manifest.PermissionSets).ToArray();
                var mergedPerms = new HashSet<string>(existing.Permissions, StringComparer.Ordinal);
                foreach (var p in manifest.Permissions)
                {
                    mergedPerms.Add(p);
                }
                grouped[manifest.PluginName] = existing with
                {
                    PermissionSets = mergedSets,
                    Permissions = mergedPerms.ToArray()
                };
            }
            else
            {
                grouped[manifest.PluginName] = manifest;
            }
        }
        return grouped.Values.OrderBy(m => m.PluginName, StringComparer.Ordinal).ToImmutableArray();
    }

    private static string GenerateCode(ImmutableArray<PluginManifestInfo> manifests)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// 由 PluginPermissionManifestGenerator（OPT-PLUGIN-003）自动生成。");
        sb.AppendLine("// 编译期扫描 IPermissionRegistrar 调用，产出插件权限清单 JSON。");
        sb.AppendLine("// 禁止手工修改；如需调整清单请修改插件 Configure 方法中的声明。");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using Wails.Net.Application.Security;");
        sb.AppendLine();
        sb.AppendLine("namespace Wails.Net.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// 由源代码生成器自动生成的插件权限清单注册入口（OPT-PLUGIN-003）。");
        sb.AppendLine("/// 通过 [ModuleInitializer] 在模块加载时注册到 PluginPermissionManifestRegistry，");
        sb.AppendLine("/// 供 wails-net check 编译期校验与权限审计查询复用。");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class WailsGeneratedPermissionManifest");
        sb.AppendLine("{");

        foreach (var manifest in manifests)
        {
            var json = SerializeManifest(manifest);
            var jsonLiteral = ToVerbatimString(json);
            var identifier = SafeIdentifier(manifest.PluginName);
            sb.AppendLine("    /// <summary>插件 <c>" + EscapeXml(manifest.PluginName) + "</c> 的权限清单。</summary>");
            sb.AppendLine("    public static class " + identifier);
            sb.AppendLine("    {");
            sb.AppendLine("        public const string PluginName = \"" + EscapeCSharp(manifest.PluginName) + "\";");
            sb.AppendLine("        public const string ManifestJson = @\"" + jsonLiteral + "\";");
            sb.AppendLine();
            sb.AppendLine("        [ModuleInitializer]");
            sb.AppendLine("        public static void Register()");
            sb.AppendLine("        {");
            sb.AppendLine("            PluginPermissionManifestRegistry.Register(PluginName, ManifestJson);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string SerializeManifest(PluginManifestInfo manifest)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        AppendJsonField(sb, "plugin", false);
        AppendJsonString(sb, manifest.PluginName);
        sb.Append(',');
        AppendJsonField(sb, "permissionSets", false);
        sb.Append('[');
        var firstSet = true;
        foreach (var ps in manifest.PermissionSets)
        {
            if (!firstSet) sb.Append(',');
            firstSet = false;
            sb.Append('{');
            AppendJsonField(sb, "identifier", false);
            AppendJsonString(sb, ps.Identifier);
            sb.Append(',');
            AppendJsonField(sb, "description", false);
            AppendJsonString(sb, ps.Description);
            sb.Append(',');
            AppendJsonField(sb, "permissions", false);
            sb.Append('[');
            var firstPerm = true;
            foreach (var p in ps.Permissions)
            {
                if (!firstPerm) sb.Append(',');
                firstPerm = false;
                AppendJsonString(sb, p);
            }
            sb.Append(']');
            sb.Append('}');
        }
        sb.Append(']');
        sb.Append(',');
        AppendJsonField(sb, "permissions", false);
        sb.Append('[');
        var first = true;
        foreach (var p in manifest.Permissions.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            AppendJsonString(sb, p);
        }
        sb.Append(']');
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendJsonField(StringBuilder sb, string name, bool trailingComma)
    {
        sb.Append('"').Append(name).Append('"').Append(':');
    }

    private static void AppendJsonString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    private static string EscapeXml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeCSharp(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string ToVerbatimString(string s) => s.Replace("\"", "\"\"");

    private static string SafeIdentifier(string name)
    {
        var sb = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (c == '-' || c == '_' || c == ' ')
            {
                upper = true;
                continue;
            }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        if (sb.Length == 0 || !char.IsLetter(sb[0]))
        {
            sb.Insert(0, 'P');
        }
        return sb.ToString();
    }
}