namespace Wails.Net.Application.Services;

/// <summary>
/// 服务标记接口，对应 Wails v3 中的 Service。
/// </summary>
public interface IService
{
    /// <summary>
    /// 返回服务名称。
    /// </summary>
    /// <returns>服务名称字符串。</returns>
    string ServiceName();
}
