/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const positioner = {
  /** 将窗口移动到指定位置（相对于屏幕原点）。 */
  move: (windowName: string, x: number, y: number) =>
    call<void>("positioner.move", [windowName, x, y]),
  /** 将窗口居中显示。 */
  center: (windowName: string) => call<void>("positioner.center", [windowName]),
  /** 将窗口移动到指定方向（topLeft/topRight/bottomLeft/bottomRight/center/top/bottom/left/right）。 */
  moveRelativeTo: (windowName: string, position: string) =>
    call<void>("positioner.moveRelativeTo", [windowName, position]),
  /** 将窗口移动到鼠标光标位置。 */
  moveToCursor: (windowName: string) =>
    call<void>("positioner.moveToCursor", [windowName]),
  /** 获取窗口当前位置，返回 JSON 字符串（形如 {"x":..,"y":..}）。 */
  getPosition: (windowName: string) =>
    call<string>("positioner.getPosition", [windowName]),
};