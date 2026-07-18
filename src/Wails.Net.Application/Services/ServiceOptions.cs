using Wails.Net.AssetServer;

namespace Wails.Net.Application.Services;

/// <summary>
/// 服务选项，对应 Wails v3 Go 版本 <c>services.go</c> 中的 <c>ServiceOptions</c> 结构。
/// <para>
/// 通过 <see cref="Application.RegisterService(object, ServiceOptions)"/> 在注册服务时附带选项。
/// 当服务实例实现 <see cref="IHttpServiceHandler"/> 且 <see cref="Route"/> 非空时，
/// 将被挂载到 <see cref="Wails.Net.AssetServer.AssetServer"/> 此前缀下。
/// </para>
/// </summary>
public class ServiceOptions
{
    /// <summary>
    /// 服务名称，用于日志和调试。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>ServiceOptions.Name</c> 字段。
    /// 若为空，回退到 <see cref="IService.ServiceName"/> 返回值或运行时类型名。
    /// </para>
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 路由前缀。若服务实例实现 <see cref="IHttpServiceHandler"/>，将挂载到 AssetServer 此前缀下。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>ServiceOptions.Route</c> 字段：
    /// <c>"If the service instance implements [http.Handler], it will be mounted
    /// on the internal asset server at the prefix specified by Route."</c>
    /// </para>
    /// <para>
    /// 约定：
    /// <list type="bullet">
    /// <item>以 <c>/</c> 开头（若未提供将自动补全）。</item>
    /// <item>不以 <c>/</c> 结尾（若提供将自动去除尾部斜杠）。</item>
    /// <item>匹配规则：路径等于 Route，或以 <c>Route + "/"</c> 开头。</item>
    /// </list>
    /// </para>
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// 获取默认的 <see cref="ServiceOptions"/> 实例（所有字段为默认值）。
    /// 对应 Wails v3 Go 版本 <c>DefaultServiceOptions</c> 变量。
    /// </summary>
    public static ServiceOptions Default { get; } = new();
}
