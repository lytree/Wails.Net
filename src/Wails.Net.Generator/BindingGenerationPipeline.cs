using System.IO;
using System.Reflection;
using Wails.Net.Generator.Models;

namespace Wails.Net.Generator;

/// <summary>
/// 绑定代码生成管道，统一协调分析器、生成器和文件写入。
/// 提供从程序集到 TypeScript 文件的一站式生成入口。
/// </summary>
public class BindingGenerationPipeline
{
    /// <summary>
    /// 绑定分析器。
    /// </summary>
    private readonly BindingAnalyzer _analyzer = new();

    /// <summary>
    /// TypeScript 代码生成器。
    /// </summary>
    private readonly TypeScriptGenerator _typeScriptGenerator = new();

    /// <summary>
    /// 事件代码生成器。
    /// </summary>
    private readonly EventGenerator _eventGenerator = new();

    /// <summary>
    /// 生成绑定代码（仅返回内容，不写入文件）。
    /// </summary>
    /// <param name="assembly">要分析的主程序集。</param>
    /// <param name="eventsAssembly">包含事件枚举的程序集（可为同一个）。</param>
    /// <param name="options">生成选项。</param>
    /// <returns>生成结果。</returns>
    public BindingGenerationResult Generate(
        Assembly assembly,
        Assembly? eventsAssembly,
        BindingGenerationOptions options)
    {
        try
        {
            // 分析绑定方法
            var methods = _analyzer.AnalyzeAssembly(assembly);
            var classCount = methods.Select(m => (m.Namespace, m.ClassName)).Distinct().Count();

            var files = new Dictionary<string, string>();

            // 生成类型定义
            if (options.GenerateDefinitions)
            {
                var content = _typeScriptGenerator.GenerateDefinitions(methods);
                files[options.DefinitionsFileName] = content;
            }

            // 生成调用封装
            if (options.GenerateCaller)
            {
                var content = _typeScriptGenerator.GenerateCaller(methods);
                files[options.CallerFileName] = content;
            }

            // 生成绑定 ID 映射
            if (options.GenerateIdMap)
            {
                var content = _typeScriptGenerator.GenerateIdMap(methods);
                files[options.IdMapFileName] = content;
            }

            // 生成事件类型
            var effectiveEventsAssembly = eventsAssembly ?? assembly;
            if (options.GenerateEvents)
            {
                var content = _eventGenerator.GenerateFromAssembly(effectiveEventsAssembly);
                files[options.EventsFileName] = content;
            }

            // 生成已知事件名称
            if (options.GenerateKnownEvents)
            {
                var knownEventsType = effectiveEventsAssembly.GetType("Wails.Net.Events.KnownEvents");
                if (knownEventsType is not null)
                {
                    var content = _eventGenerator.GenerateKnownEvents(knownEventsType);
                    files[options.KnownEventsFileName] = content;
                }
            }

            return BindingGenerationResult.SuccessResult(methods.Count, classCount, files);
        }
        catch (Exception ex)
        {
            return BindingGenerationResult.FailureResult(ex.Message);
        }
    }

    /// <summary>
    /// 生成绑定代码并写入文件系统。
    /// </summary>
    /// <param name="assembly">要分析的主程序集。</param>
    /// <param name="eventsAssembly">包含事件枚举的程序集。</param>
    /// <param name="options">生成选项。</param>
    /// <returns>生成结果。</returns>
    public BindingGenerationResult GenerateToDisk(
        Assembly assembly,
        Assembly? eventsAssembly,
        BindingGenerationOptions options)
    {
        var result = Generate(assembly, eventsAssembly, options);
        if (!result.Success)
        {
            return result;
        }

        try
        {
            // 确保输出目录存在
            if (!Directory.Exists(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
            }

            // 写入所有文件
            foreach (var (fileName, content) in result.GeneratedFiles)
            {
                var fullPath = Path.Combine(options.OutputDirectory, fileName);
                File.WriteAllText(fullPath, content);
            }

            return result;
        }
        catch (Exception ex)
        {
            return BindingGenerationResult.FailureResult($"写入文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 分析指定实例列表并生成绑定代码。
    /// 适用于已知服务实例的场景。
    /// </summary>
    /// <param name="instances">服务实例列表。</param>
    /// <param name="options">生成选项。</param>
    /// <returns>生成结果。</returns>
    public BindingGenerationResult GenerateFromInstances(
        IEnumerable<object> instances,
        BindingGenerationOptions options)
    {
        try
        {
            var methods = new List<BoundMethodModel>();
            var assemblies = new HashSet<Assembly>();
            foreach (var instance in instances)
            {
                methods.AddRange(_analyzer.AnalyzeInstance(instance));
                assemblies.Add(instance.GetType().Assembly);
            }

            var classCount = methods.Select(m => (m.Namespace, m.ClassName)).Distinct().Count();
            var files = new Dictionary<string, string>();

            if (options.GenerateDefinitions)
            {
                files[options.DefinitionsFileName] = _typeScriptGenerator.GenerateDefinitions(methods);
            }

            if (options.GenerateCaller)
            {
                files[options.CallerFileName] = _typeScriptGenerator.GenerateCaller(methods);
            }

            if (options.GenerateIdMap)
            {
                files[options.IdMapFileName] = _typeScriptGenerator.GenerateIdMap(methods);
            }

            // 生成事件类型和已知事件常量（从实例所在程序集中提取）
            if (options.GenerateEvents || options.GenerateKnownEvents)
            {
                foreach (var assembly in assemblies)
                {
                    if (options.GenerateEvents)
                    {
                        var eventContent = _eventGenerator.GenerateFromAssembly(assembly);
                        // 使用程序集名作为文件名后缀避免冲突
                        var fileName = assemblies.Count > 1
                            ? $"{Path.GetFileNameWithoutExtension(assembly.Location)}-{options.EventsFileName}"
                            : options.EventsFileName;
                        files[fileName] = eventContent;
                    }

                    if (options.GenerateKnownEvents)
                    {
                        var knownEventsType = assembly.GetType("Wails.Net.Events.KnownEvents");
                        if (knownEventsType is not null)
                        {
                            var content = _eventGenerator.GenerateKnownEvents(knownEventsType);
                            files[options.KnownEventsFileName] = content;
                        }
                    }
                }
            }

            return BindingGenerationResult.SuccessResult(methods.Count, classCount, files);
        }
        catch (Exception ex)
        {
            return BindingGenerationResult.FailureResult(ex.Message);
        }
    }
}
