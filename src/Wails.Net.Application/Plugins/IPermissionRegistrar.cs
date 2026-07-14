using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件权限声明接口，允许插件在 <see cref="IPlugin.Configure"/> 中声明自己的权限集和细粒度权限。
/// 对应 Tauri v2 插件的 <c>permissions/&lt;plugin&gt;/default.toml</c> 声明机制：
/// 每个插件通过代码声明自身提供的权限集（如 <c>fs:default</c>）和细粒度权限（如 <c>fs:allow-read</c>），
/// 以便能力声明（<see cref="Security.Capability"/>）中引用这些标识后能被 <see cref="PermissionManager"/> 识别并授权。
/// </summary>
public interface IPermissionRegistrar
{
    /// <summary>
    /// 注册插件权限集（如 <c>fs:default</c>）。
    /// 权限集是命名权限组合，能力声明中引用权限集标识后，授权时自动展开为集内所有细粒度权限。
    /// </summary>
    /// <param name="identifier">权限集标识符，约定格式 <c>&lt;plugin&gt;:&lt;setName&gt;</c>（如 <c>fs:default</c>）。</param>
    /// <param name="description">权限集描述。</param>
    /// <param name="permissions">权限集包含的细粒度权限标识列表（如 <c>fs:allow-read</c>）。</param>
    void RegisterPermissionSet(string identifier, string description, params string[] permissions);

    /// <summary>
    /// 声明细粒度权限（如 <c>fs:allow-read</c>）。
    /// 声明的权限将记录到权限管理器，便于运行时权限校验和文档生成。
    /// </summary>
    /// <param name="identifier">权限标识，约定格式 <c>&lt;plugin&gt;:allow-&lt;action&gt;</c>（如 <c>fs:allow-read</c>）。</param>
    /// <param name="description">权限描述。</param>
    void DeclarePermission(string identifier, string description);

    /// <summary>
    /// 为权限绑定作用域（Scope）。
    /// 绑定后，命令调度时会自动校验参数值是否在作用域允许范围内（如文件路径必须在指定目录下）。
    /// </summary>
    /// <param name="permissionId">权限标识（如 <c>fs:allow-read</c>）。</param>
    /// <param name="scope">作用域实例（如 <see cref="FileSystemScope"/>、<see cref="UrlScope"/>）。</param>
    void BindScope(string permissionId, IScope scope);
}
