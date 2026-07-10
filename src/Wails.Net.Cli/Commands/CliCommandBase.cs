using System.CommandLine;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// CLI 命令基类，提供通用辅助方法。
/// </summary>
internal abstract class CliCommandBase
{
    /// <summary>
    /// 向标准输出写入信息行。
    /// </summary>
    /// <param name="message">消息内容。</param>
    protected static void Info(string message) => Console.WriteLine(message);

    /// <summary>
    /// 向标准错误写入错误信息。
    /// </summary>
    /// <param name="message">错误消息。</param>
    protected static void Error(string message) => Console.Error.WriteLine($"[错误] {message}");

    /// <summary>
    /// 向标准输出写入成功信息（绿色）。
    /// </summary>
    /// <param name="message">成功消息。</param>
    protected static void Success(string message)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    /// <summary>
    /// 向标准输出写入警告信息（黄色）。
    /// </summary>
    /// <param name="message">警告消息。</param>
    protected static void Warn(string message)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[警告] {message}");
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
