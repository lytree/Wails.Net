namespace Wails.Net.Application.Bindings;

/// <summary>
/// 绑定注册表，提供全局静态访问点。
/// 对应 Wails v3 中全局的 Bindings 实例。
/// </summary>
public static class BindingRegistry
{
    /// <summary>
    /// 全局绑定实例。
    /// </summary>
    private static BindingManager? _global;

    /// <summary>
    /// 获取全局绑定实例，若未设置则抛出异常。
    /// </summary>
    /// <exception cref="InvalidOperationException">当全局绑定未初始化时抛出。</exception>
    public static BindingManager Global => _global
        ?? throw new InvalidOperationException("绑定注册表尚未初始化，请先调用 Initialize()。");

    /// <summary>
    /// 获取全局绑定实例，若未设置则返回 null。
    /// </summary>
    public static BindingManager? GlobalOrNull => _global;

    /// <summary>
    /// 初始化全局绑定实例。
    /// </summary>
    /// <returns>新创建的绑定实例。</returns>
    public static BindingManager Initialize()
    {
        _global = new BindingManager();
        return _global;
    }

    /// <summary>
    /// 使用指定的绑定实例设置全局绑定。
    /// </summary>
    /// <param name="bindings">要设置为全局的绑定实例。</param>
    public static void SetGlobal(BindingManager bindings)
    {
        _global = bindings;
    }

    /// <summary>
    /// 重置全局绑定实例为 null（主要用于测试）。
    /// </summary>
    public static void Reset()
    {
        _global = null;
    }
}
