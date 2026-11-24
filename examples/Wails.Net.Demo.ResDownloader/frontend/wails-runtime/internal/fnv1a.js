/**
 * FNV-1a 32 位哈希。
 *
 * 必须与 Wails.Net 后端 `Bindings.FNV1aHash` 以及 Go 版 `fnv.New32a()` 完全一致，
 * 因为前端 `bindings.call(id, args)` 用的 id 即由 `FullName` 经此哈希得到。
 *
 * 常量：offsetBasis = 2166136261，prime = 16777619。
 */
/**
 * 计算字符串的 FNV-1a 32 位无符号哈希。
 * @param text 输入字符串（按 UTF-8 编码）。
 * @returns 32 位无符号整数。
 */
export function fnv1a(text) {
    let hash = 2166136261 >>> 0;
    const bytes = new TextEncoder().encode(text);
    for (let i = 0; i < bytes.length; i++) {
        hash = (hash ^ bytes[i]) >>> 0;
        hash = Math.imul(hash, 16777619) >>> 0;
    }
    return hash >>> 0;
}
//# sourceMappingURL=fnv1a.js.map