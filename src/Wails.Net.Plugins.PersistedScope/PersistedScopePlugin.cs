using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Plugins.PersistedScope;

/// <summary>
/// 文件系统范围持久化插件（P2-2 审计项落地）。
/// <para>
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-persisted-scope</c>。
/// 允许在运行时动态添加/移除允许访问的文件系统路径，并将范围变更持久化到 JSON 文件，
/// 以便应用重启后恢复之前的 scope 配置。
/// </para>
/// <para>
/// <b>与权限模型的深度整合（P2-2）</b>：插件通过 <see cref="IPermissionRegistrar.BindScope"/>
/// 将 <see cref="PersistedScopeManager"/>（实现 <see cref="Wails.Net.Application.Security.IScope"/>）
/// 绑定到受管权限（默认 <c>fs:allow-read</c> / <c>fs:allow-write</c>），使运行时动态授权的路径
/// 立即参与 <see cref="Wails.Net.Application.Security.PermissionManager.ValidateScopes"/>
/// 的参数级 Scope 校验（如 fs 插件的 <c>[ScopeParameter("fs:allow-read")]</c> 路径参数）。
/// </para>
/// </summary>
public class PersistedScopePlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "persisted-scope";

    /// <summary>作用域管理器实例（注册为 DI 单例，供其他服务注入）。</summary>
    private readonly PersistedScopeManager _manager = new();

    /// <summary>
    /// 受管权限列表：绑定 <see cref="PersistedScopeManager"/> 作为其参数级 Scope 的权限标识。
    /// 默认覆盖 fs 插件的读写权限；可通过构造函数自定义。
    /// </summary>
    public IReadOnlyList<string> ManagedPermissions { get; }

    /// <summary>
    /// 使用默认受管权限构造插件（<c>fs:allow-read</c>、<c>fs:allow-write</c>）。
    /// </summary>
    public PersistedScopePlugin()
        : this(["fs:allow-read", "fs:allow-write"])
    {
    }

    /// <summary>
    /// 使用自定义受管权限列表构造插件。
    /// </summary>
    /// <param name="managedPermissions">应绑定持久化作用域的权限标识列表（如 <c>fs:allow-read</c>）。</param>
    public PersistedScopePlugin(IEnumerable<string> managedPermissions)
    {
        ArgumentNullException.ThrowIfNull(managedPermissions);
        ManagedPermissions = managedPermissions.ToArray();
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器：注册 <see cref="IPersistedScope"/> 单例。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPersistedScope>(_manager);
    }

    /// <summary>
    /// 配置插件：将持久化作用域绑定到受管权限，并注册范围管理命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // P2-2：将持久化作用域管理器绑定为受管权限的 IScope。
        // 绑定后，scope.addPath 等命令添加的路径会立即参与 CommandDispatcher 的
        // 参数级 Scope 校验（fs 插件的 [ScopeParameter] 路径参数）。
        foreach (var permissionId in ManagedPermissions)
        {
            context.Permissions.BindScope(permissionId, _manager);
        }

        // 添加允许的路径
        context.Commands.MapCommand("scope.addPath", (Func<string, string?, bool>)((path, scopeName) =>
            _manager.AddPath(path, scopeName)));

        // 移除允许的路径
        context.Commands.MapCommand("scope.removePath", (Func<string, string?, bool>)((path, scopeName) =>
            _manager.RemovePath(path, scopeName)));

        // 列出所有允许的路径
        context.Commands.MapCommand("scope.listPaths", (Func<string?, string[]?>)((scopeName) =>
            _manager.ListPaths(scopeName)));

        // 清除所有允许的路径
        context.Commands.MapCommand("scope.clear", (Action<string?>)((scopeName) =>
            _manager.Clear(scopeName)));

        // 检查路径是否在允许范围内
        context.Commands.MapCommand("scope.isAllowed", (Func<string, string?, bool>)((path, scopeName) =>
        {
            // 兼容旧签名（scopeName 指定单作用域）：指定时仅查该作用域，未指定时合并所有作用域
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                return _manager.IsAllowed(path);
            }
            return _manager.ListPaths(scopeName).Any(allowed =>
                string.Equals(allowed, path, StringComparison.OrdinalIgnoreCase));
        }));

        // 手动保存范围到磁盘
        context.Commands.MapCommand("scope.save", (Action<string?>)((scopeName) =>
            _manager.Save(scopeName)));

        // 从磁盘加载范围
        context.Commands.MapCommand("scope.load", (Func<string?, bool>)((scopeName) =>
            _manager.Load(scopeName)));
    }
}
