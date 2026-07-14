namespace Wails.Net.Application.Security;

/// <summary>
/// 权限作用域抽象接口。
/// 对应 Tauri v2 的 Scope 概念：每个权限可携带作用域参数（如允许的文件路径、URL 模式等），
/// 在 IPC 调用时校验实际参数是否在作用域内。
/// </summary>
public interface IScope
{
    /// <summary>
    /// 校验指定参数值是否在当前作用域允许的范围内。
    /// </summary>
    /// <param name="value">要校验的参数值（如文件路径、URL 等）。</param>
    /// <returns>在允许范围内返回 true；超出范围返回 false。</returns>
    bool Allows(string value);
}
