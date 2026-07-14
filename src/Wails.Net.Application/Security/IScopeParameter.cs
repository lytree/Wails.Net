namespace Wails.Net.Application.Security;

/// <summary>
/// 参数级作用域校验接口。
/// Options 类实现此接口后，CommandDispatcher 在调度时会自动提取待校验值并验证。
/// 对应 Tauri v2 的 Scope 机制：命令参数在执行前必须通过已绑定 Scope 的校验。
/// </summary>
public interface IScopeParameter
{
    /// <summary>
    /// 获取此参数对象中所有需要 Scope 校验的 (权限标识, 待校验值) 对。
    /// 例如 FileSystemReadOptions 返回 ("fs:allow-read", "/path/to/file")。
    /// </summary>
    /// <returns>需要校验的权限标识与对应值的集合。</returns>
    IEnumerable<(string PermissionId, string Value)> GetScopeValues();
}
