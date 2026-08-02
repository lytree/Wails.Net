using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TUnit.Core;

namespace Wails.Net.Application.Tests.Architecture;

/// <summary>
/// 阶段 B2：禁反射守卫。
/// 自动扫描 <c>src/</c> 生产代码，禁止 AGENTS.md §3.4.2 列出的运行时反射 API；
/// 仅放行 §3.4.3 已评审的 3 处例外（白名单）。
/// 注释与字符串字面量会被剥离后再匹配，避免文档注释中提及违禁 API 造成误报。
/// </summary>
public class ReflectionGuardTests
{
    // AGENTS.md §3.4.2 禁用的运行时反射 API（生产代码 src/ 严禁）
    private static readonly IReadOnlyList<Regex> BannedPatterns = new[]
    {
        @"MethodInfo\s*\.\s*Invoke",
        @"ConstructorInfo\s*\.\s*Invoke",
        @"Activator\s*\.\s*CreateInstance(?:From)?",
        @"Type\s*\.\s*Get(?:Method|Methods|Property|Properties|Field|Fields|Constructor|Constructors|Type|ExportedTypes|NestedType|NestedTypes)\b",
        @"Assembly\s*\.\s*Get(?:Types|ExportedTypes)\b",
        @"MakeGeneric(?:Method|Type)\b",
        @"Delegate\s*\.\s*CreateDelegate",
        @"AssemblyResolve",
    }.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToArray();

    // §3.4.3 已评审例外（相对 src/ 的路径片段）
    private static readonly IReadOnlyList<string> WhitelistSegments = new[]
    {
        "Wails.Net.Application/Platform/PlatformFactory.cs", // Assembly.Load + RunModuleConstructor
        "Wails.Net.Application/Bindings/Bindings.cs",         // 含 BindingManager 的 GetType().Name/Namespace
        "Wails.Net.SourceGenerators",                        // 源生成器 Roslyn 分析（非运行时反射）
    };

    [Test]
    public async Task Src_MustNotUseRuntimeReflection()
    {
        var srcRoot = FindSrcRoot();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (WhitelistSegments.Any(w => normalized.Contains(w)))
                continue;

            var lines = await File.ReadAllLinesAsync(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var cleaned = StripCommentsAndStrings(lines[i]);
                foreach (var pattern in BannedPatterns)
                {
                    if (pattern.IsMatch(cleaned))
                    {
                        violations.Add($"{Path.GetRelativePath(srcRoot, file)}:{i + 1}: {lines[i].Trim()}");
                        break;
                    }
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("AGENTS.md §3.4 禁止生产代码使用运行时反射：\n" + string.Join("\n", violations));
    }

    private static string StripCommentsAndStrings(string line)
    {
        // 去掉行内 // 注释（含 /// 文档注释）
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        if (idx >= 0)
            line = line[..idx];

        // 去掉字符串字面量（"..." / $"..." / @"..."），避免文档字符串误报
        line = Regex.Replace(line, @"""[^""\\]*(?:\\.[^""\\]*)*""", "\"\"");
        return line;
    }

    private static string FindSrcRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) &&
                File.Exists(Path.Combine(dir, "AGENTS.md")))
                return Path.Combine(dir, "src");
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("无法定位仓库 src 目录（向上未找到含 src/ 与 AGENTS.md 的目录）");
    }
}
