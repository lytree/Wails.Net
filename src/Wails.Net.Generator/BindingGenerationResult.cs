namespace Wails.Net.Generator;

/// <summary>
/// 绑定代码生成结果，包含生成的文件列表和统计信息。
/// </summary>
public sealed class BindingGenerationResult
{
    /// <summary>
    /// 生成的文件列表（相对路径 → 内容）。
    /// </summary>
    public Dictionary<string, string> GeneratedFiles { get; set; } = new();

    /// <summary>
    /// 分析到的绑定方法数量。
    /// </summary>
    public int MethodCount { get; set; }

    /// <summary>
    /// 分析到的绑定类数量。
    /// </summary>
    public int ClassCount { get; set; }

    /// <summary>
    /// 生成是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误消息（若生成失败）。
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 创建表示成功的结果。
    /// </summary>
    /// <param name="methods">方法数。</param>
    /// <param name="classes">类数。</param>
    /// <param name="files">生成的文件。</param>
    /// <returns>成功结果。</returns>
    public static BindingGenerationResult SuccessResult(int methods, int classes, Dictionary<string, string> files)
    {
        return new BindingGenerationResult
        {
            Success = true,
            MethodCount = methods,
            ClassCount = classes,
            GeneratedFiles = files
        };
    }

    /// <summary>
    /// 创建表示失败的结果。
    /// </summary>
    /// <param name="error">错误消息。</param>
    /// <returns>失败结果。</returns>
    public static BindingGenerationResult FailureResult(string error)
    {
        return new BindingGenerationResult
        {
            Success = false,
            ErrorMessage = error
        };
    }
}
