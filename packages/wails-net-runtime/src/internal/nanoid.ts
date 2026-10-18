/**
 * `nanoid` 的等价实现（移植自 Wails v3
 * `@wailsio/runtime/src/nanoid.ts`，beta.28）。
 *
 * 上游从 `Math.random()` 取值（非密码学来源）。这里的 id 用作 stream session
 * 关联标识（非鉴权能力），但更强的来源能让跨多份运行时副本的碰撞概率可忽略。
 * 字母表恰为 64 个字符，因此用 `byte & 63` 取值是均匀的 —— 无需拒绝采样。
 *
 * 与仓库既有约定一致：不引入任何运行时第三方依赖，随机源直接使用宿主
 * `crypto.getRandomValues`。
 */

/** URL 安全字母表（`A-Za-z0-9_-`），字符顺序针对更好的 gzip/brotli 压缩优化。 */
const urlAlphabet =
  "useandom-26T198340PX75pxJACKVERYMINDBUSHWOLF_GQZbfghjklqvwyzrict";

/**
 * 生成一个 URL 安全的随机 id。
 * @param size id 长度，默认 21（与上游一致）。
 * @returns 由 {@link urlAlphabet} 组成的随机字符串。
 */
export function nanoid(size: number = 21): string {
  const source =
    typeof crypto !== "undefined" && typeof crypto.getRandomValues === "function"
      ? crypto
      : null;

  if (source) {
    const bytes = new Uint8Array(size);
    source.getRandomValues(bytes);
    let id = "";
    for (let i = 0; i < size; i++) {
      id += urlAlphabet[bytes[i] & 63];
    }
    return id;
  }

  // 无 Web Crypto 时的回退（极旧引擎 / 某些 SSR 场景）。id 仍唯一但不可预测性较弱。
  let id = "";
  let i = size | 0;
  while (i--) {
    id += urlAlphabet[(Math.random() * 64) | 0];
  }
  return id;
}
