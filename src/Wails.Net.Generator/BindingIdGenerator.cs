using System.Text;

namespace Wails.Net.Generator;

/// <summary>
/// 绑定 ID 生成器，使用 FNV-1a 32 位哈希算法生成稳定的方法 ID。
/// 与运行时 BindingManager.FNV1aHash 完全一致，确保跨语言兼容。
/// 对应 Wails v3 Go 版本 internal/generator/bindingid.go。
/// </summary>
public static class BindingIdGenerator
{
    /// <summary>
    /// FNV-1a 32 位偏移基数（标准值）。
    /// </summary>
    public const uint OffsetBasis = 2166136261u;

    /// <summary>
    /// FNV-1a 32 位质数（标准值）。
    /// </summary>
    public const uint Prime = 16777619u;

    /// <summary>
    /// 计算指定文本的 FNV-1a 32 位哈希值。
    /// 与 Go 版本 fnv.New32a() 和运行时 BindingManager.FNV1aHash 完全一致。
    /// </summary>
    /// <param name="text">要哈希的文本（通常为方法全限定名）。</param>
    /// <returns>32 位无符号哈希值。</returns>
    public static uint Generate(string text)
    {
        var hash = OffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= Prime;
        }
        return hash;
    }

    /// <summary>
    /// 根据命名空间、类名和方法名生成方法全限定名。
    /// </summary>
    /// <param name="namespace">命名空间。</param>
    /// <param name="className">类名。</param>
    /// <param name="methodName">方法名。</param>
    /// <returns>全限定名（Namespace.ClassName.MethodName）。</returns>
    public static string GetFullName(string @namespace, string className, string methodName)
    {
        return $"{@namespace}.{className}.{methodName}";
    }

    /// <summary>
    /// 根据命名空间、类名和方法名生成绑定 ID。
    /// </summary>
    /// <param name="namespace">命名空间。</param>
    /// <param name="className">类名。</param>
    /// <param name="methodName">方法名。</param>
    /// <returns>FNV-1a 哈希 ID。</returns>
    public static uint GenerateFromParts(string @namespace, string className, string methodName)
    {
        return Generate(GetFullName(@namespace, className, methodName));
    }
}
