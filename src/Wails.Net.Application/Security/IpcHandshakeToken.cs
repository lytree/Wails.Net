using System.Security.Cryptography;

namespace Wails.Net.Application.Security;

/// <summary>
/// IPC handshake token 生成器（OPT-IPC-002）。
/// <para>
/// 为 HTTP/WebSocket fallback 通道提供进程级随机 token，防止本机其他网页或进程
/// 误调用 IPC 接口。token 由框架在装配传输层时生成并注入前端 runtime
/// （<c>window._wails.ipcToken</c>），前端发起 IPC 请求时回传校验。
/// </para>
/// <para>
/// 安全约定：
/// <list type="bullet">
/// <item>token 使用 32 字节 CSPRNG 随机数，Base64Url 编码（无填充），不可预测。</item>
/// <item>token 进程内缓存（<see cref="Current"/>），同一进程所有 fallback 通道共享。</item>
/// <item>禁止写入日志：任何日志输出不得包含 token 值。</item>
/// </list>
/// </para>
/// </summary>
public static class IpcHandshakeToken
{
    /// <summary>
    /// 进程级 token 缓存（惰性生成）。
    /// </summary>
    private static readonly Lazy<string> LazyCurrent = new(Generate, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// 获取当前进程的 handshake token（首次访问时生成，进程内稳定）。
    /// </summary>
    public static string Current => LazyCurrent.Value;

    /// <summary>
    /// 生成新的随机 token（32 字节 CSPRNG，Base64Url 无填充）。
    /// </summary>
    /// <returns>Base64Url 编码的 token 字符串。</returns>
    public static string Generate()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
