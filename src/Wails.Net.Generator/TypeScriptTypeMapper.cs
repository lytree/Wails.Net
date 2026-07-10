namespace Wails.Net.Generator;

/// <summary>
/// C# 类型到 TypeScript 类型的映射器。
/// 对应 Wails v3 Go 版本 internal/generator/types.go 中的类型映射逻辑。
/// </summary>
public static class TypeScriptTypeMapper
{
    /// <summary>
    /// C# 基元类型到 TypeScript 类型的直接映射表。
    /// </summary>
    private static readonly Dictionary<Type, string> PrimitiveMappings = new()
    {
        [typeof(void)] = "void",
        [typeof(bool)] = "boolean",
        [typeof(byte)] = "number",
        [typeof(sbyte)] = "number",
        [typeof(short)] = "number",
        [typeof(ushort)] = "number",
        [typeof(int)] = "number",
        [typeof(uint)] = "number",
        [typeof(long)] = "number",
        [typeof(ulong)] = "number",
        [typeof(float)] = "number",
        [typeof(double)] = "number",
        [typeof(decimal)] = "number",
        [typeof(char)] = "string",
        [typeof(string)] = "string",
        [typeof(object)] = "unknown",
        [typeof(DateTime)] = "string",
        [typeof(DateTimeOffset)] = "string",
        [typeof(TimeSpan)] = "string",
        [typeof(Guid)] = "string",
        [typeof(Uri)] = "string",
        [typeof(byte[])] = "number[]",
        [typeof(System.Text.Json.JsonElement)] = "unknown",
        [typeof(System.Text.Json.JsonDocument)] = "unknown",
    };

    /// <summary>
    /// 将 C# 类型映射为 TypeScript 类型字符串。
    /// </summary>
    /// <param name="type">C# 类型。</param>
    /// <returns>TypeScript 类型字符串。</returns>
    public static string MapType(Type type)
    {
        if (type is null)
        {
            return "unknown";
        }

        // 处理 Nullable<T>
        var nullableUnderlyingType = Nullable.GetUnderlyingType(type);
        if (nullableUnderlyingType is not null)
        {
            return MapType(nullableUnderlyingType) + " | null";
        }

        // 处理基元类型和常见值类型
        if (PrimitiveMappings.TryGetValue(type, out var primitive))
        {
            return primitive;
        }

        // 处理数组
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return MapType(elementType!) + "[]";
        }

        // 处理泛型 List<T>
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();

            // List<T> / IList<T> / IEnumerable<T>
            if (genericDef == typeof(List<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(IReadOnlyList<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IReadOnlyCollection<>))
            {
                var elementType = type.GetGenericArguments()[0];
                return MapType(elementType) + "[]";
            }

            // Dictionary<K, V> / IDictionary<K, V>
            if (genericDef == typeof(Dictionary<,>) ||
                genericDef == typeof(IDictionary<,>) ||
                genericDef == typeof(IReadOnlyDictionary<,>))
            {
                var keyType = type.GetGenericArguments()[0];
                var valueType = type.GetGenericArguments()[1];
                return $"Record<{MapType(keyType)}, {MapType(valueType)}>";
            }

            // Task<T>
            if (genericDef == typeof(Task<>))
            {
                var resultType = type.GetGenericArguments()[0];
                return MapType(resultType);
            }

            // ValueTask<T>
            if (genericDef == typeof(ValueTask<>))
            {
                var resultType = type.GetGenericArguments()[0];
                return MapType(resultType);
            }

            // Tuple<T1, T2, ...>
            if (genericDef == typeof(Tuple<>) ||
                genericDef == typeof(Tuple<,>) ||
                genericDef == typeof(Tuple<,,>) ||
                genericDef == typeof(Tuple<,,,>) ||
                genericDef == typeof(Tuple<,,,,>) ||
                genericDef == typeof(Tuple<,,,,,>) ||
                genericDef == typeof(Tuple<,,,,,,>) ||
                genericDef == typeof(Tuple<,,,,,,,>))
            {
                var args = type.GetGenericArguments();
                return $"[{string.Join(", ", args.Select(MapType))}]";
            }
        }

        // 处理 Task（无泛型参数）
        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return "void";
        }

        // 处理 CancellationToken（不暴露给前端）
        if (type == typeof(CancellationToken))
        {
            return "void";
        }

        // 处理枚举
        if (type.IsEnum)
        {
            return type.Name;
        }

        // 处理自定义类型（类、结构体、接口）
        // 简化策略：使用类型名作为 TypeScript 类型
        var typeName = type.Name;

        // 移除泛型后缀（如 `1）
        var backtickIndex = typeName.IndexOf('`');
        if (backtickIndex > 0)
        {
            typeName = typeName[..backtickIndex];
        }

        return typeName;
    }

    /// <summary>
    /// 判断指定 C# 类型是否为异步方法返回类型（Task 或 Task&lt;T&gt;）。
    /// </summary>
    /// <param name="type">C# 类型。</param>
    /// <returns>如果是 Task 类型则返回 true。</returns>
    public static bool IsTaskType(Type type)
    {
        if (type is null)
        {
            return false;
        }

        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(Task<>) || genericDef == typeof(ValueTask<>);
        }

        return false;
    }
}
