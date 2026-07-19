using System.IO;
using Wails.Net.Events;
using Wails.Net.Generator.Models;

namespace Wails.Net.Generator;

/// <summary>
/// 绑定代码生成管道，统一协调分析器、生成器和文件写入。
/// 提供从源生成器元数据到 TypeScript 文件的一站式生成入口。
/// </summary>
/// <remarks>
/// 此实现完全基于源代码生成器在编译期填充的元数据
/// （<see cref="GeneratedBindingsMetadata"/> 和 <see cref="GeneratedEventsMetadata"/>），
/// 不再使用 <see cref="System.Reflection.Assembly"/> 进行运行时分析。
/// </remarks>
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
    /// <param name="options">生成选项。</param>
    /// <returns>生成结果。</returns>
    public BindingGenerationResult Generate(BindingGenerationOptions options)
    {
        try
        {
            // 分析绑定方法（从 GeneratedBindingsMetadata 读取）
            var methods = _analyzer.AnalyzeMetadata();
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

            // 生成事件类型（从 GeneratedEventsMetadata.Enums 读取）
            if (options.GenerateEvents)
            {
                var content = _eventGenerator.GenerateFromMetadata();
                files[options.EventsFileName] = content;
            }

            // 生成已知事件名称（从 GeneratedEventsMetadata.KnownEvents 读取）
            if (options.GenerateKnownEvents)
            {
                var content = _eventGenerator.GenerateKnownEventsFromMetadata();
                files[options.KnownEventsFileName] = content;
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
    /// <param name="options">生成选项。</param>
    /// <returns>生成结果。</returns>
    public BindingGenerationResult GenerateToDisk(BindingGenerationOptions options)
    {
        var result = Generate(options);
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
            foreach (var instance in instances)
            {
                methods.AddRange(_analyzer.AnalyzeInstance(instance));
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

            // 生成事件类型和已知事件常量（从源生成器元数据中读取）
            if (options.GenerateEvents)
            {
                files[options.EventsFileName] = _eventGenerator.GenerateFromMetadata();
            }

            if (options.GenerateKnownEvents)
            {
                files[options.KnownEventsFileName] = _eventGenerator.GenerateKnownEventsFromMetadata();
            }

            return BindingGenerationResult.SuccessResult(methods.Count, classCount, files);
        }
        catch (Exception ex)
        {
            return BindingGenerationResult.FailureResult(ex.Message);
        }
    }
}
