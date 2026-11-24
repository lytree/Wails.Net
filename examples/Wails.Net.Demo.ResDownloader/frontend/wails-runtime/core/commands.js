/**
 * L2 命令抽象层：消除插件封装样板。
 *
 * 现状问题：每个插件封装都要手写「命令名拼接 + 参数打包 + 返回泛型 + 线协议映射」。
 * 本模块用 `defineCommand` 一条声明收拢全部样板，通过泛型把类型"钉"在导出变量上，
 * 在「零样板」与「完整类型提示（参数约束 / 返回推导 / 可取消）」之间取得平衡。
 *
 * 线协议约定（与后端 MessageProcessor.TryDispatchCommandAsync 一致）：
 * - `args.length === 1` → 取 `args[0]` **整体**反序列化为唯一业务参数；
 * - `args.length > 1`   → 整个数组按位置逐个反序列化；
 * - `args.length === 0` → 传 `default`。
 */
import { call } from "./runtime.js";
/**
 * 按打包模式把调用参数映射为线协议 wire 数组。
 * 独立为纯函数，便于单测与复用。
 */
function packArgs(pack, args) {
    switch (pack) {
        case "none":
            return [];
        case "single":
            return [args[0]];
        case "spread":
            return args;
    }
}
/**
 * 定义一条类型化命令。
 *
 * @typeParam A 参数元组（`[arg1, arg2]`），决定调用函数的参数约束。
 * @typeParam R 返回类型，决定 `CancellablePromise<R>` 的推导。
 * @param name 完整命令名（`ns.method`，与后端 MapCommand 注册名一致）。
 * @param pack 参数打包模式（none / single / spread）。
 * @returns 类型化调用函数：`(...args: A) => CancellablePromise<R>`。
 *
 * @example
 * ```ts
 * // 单参数对象：自动包装为 [{ channel }]
 * export const checkForUpdate = defineCommand<[UpdateChannel], UpdateManifest>(
 *   "updater.checkForUpdate", "single");
 * await checkForUpdate("stable");   // ^? CancellablePromise<UpdateManifest>
 *
 * // 多位置参数：原样展开
 * export const setSize = defineCommand<[number, number], void>("window.setSize", "spread");
 * setSize(800, 600);
 *
 * // 无参数
 * export const getCurrentVersion = defineCommand<[], string>("updater.getCurrentVersion", "none");
 * ```
 */
export function defineCommand(name, pack) {
    return (...args) => call(name, packArgs(pack, args));
}
//# sourceMappingURL=commands.js.map