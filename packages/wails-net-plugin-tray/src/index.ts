/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";
import type { Menu } from "@wails-net/runtime";

/** tray.setIcon 命令参数。 */
export interface TrayIconOptions {
  /** 图标二进制数据（Base64 字符串，对应后端 byte[]）。 */
  iconData?: string;
}

/** tray.setLabel 命令参数。 */
export interface TrayLabelOptions {
  /** 标签文本。 */
  label: string;
}

/** tray.setMenu 命令参数。 */
export interface TrayMenuOptions {
  /** 要关联的菜单实例；省略表示清除已关联的菜单。 */
  menu?: Menu;
}

/** tray.setTooltip 命令参数。 */
export interface TrayTooltipOptions {
  /** 提示文本。 */
  tooltip: string;
}

export const tray = {
  /** 设置当前活动托盘实例的图标。 */
  setIcon: (opts: TrayIconOptions) => call<void>("tray.setIcon", [opts]),
  /** 设置当前活动托盘实例的标签文本。 */
  setLabel: (opts: TrayLabelOptions) => call<void>("tray.setLabel", [opts]),
  /** 设置当前活动托盘实例关联的菜单。 */
  setMenu: (opts: TrayMenuOptions) => call<void>("tray.setMenu", [opts]),
  /** 设置当前活动托盘实例的提示文本。 */
  setTooltip: (opts: TrayTooltipOptions) => call<void>("tray.setTooltip", [opts]),
  /** 销毁当前活动托盘实例。 */
  destroy: () => call<void>("tray.destroy", []),
  /** 显示托盘图标。 */
  show: () => call<void>("tray.show", []),
  /** 隐藏托盘图标。 */
  hide: () => call<void>("tray.hide", []),
  /** 查询当前活动托盘实例是否可见。 */
  isVisible: () => call<boolean>("tray.isVisible", []),
};