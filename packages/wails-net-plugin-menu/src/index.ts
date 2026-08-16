/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";
import type { AboutMetadata, Menu, MenuRole } from "@wails-net/runtime";

/** menu.setApplicationMenu 命令参数。 */
export interface MenuApplicationMenuOptions {
  /** 要设置的菜单实例；省略表示清除应用菜单。 */
  menu?: Menu;
}

/** menu.setContextMenu 命令参数。 */
export interface MenuContextMenuOptions {
  /** 上下文菜单 ID，前端 CSS 变量 --custom-contextmenu 引用此 ID。 */
  id: string;
  /** 上下文菜单实例；省略表示移除该 ID 的注册。 */
  menu?: Menu;
}

/** menu.popup 命令参数。 */
export interface MenuPopupOptions {
  /** 已注册的上下文菜单 ID；为空时平台可能弹出空菜单或忽略。 */
  id?: string;
  /** X 坐标（视口坐标，由平台转换为屏幕坐标）。 */
  x: number;
  /** Y 坐标（视口坐标，由平台转换为屏幕坐标）。 */
  y: number;
  /** 透传到菜单项点击回调的额外数据。 */
  data?: string;
}

/** menu.updateMenuItem 命令参数。 */
export interface MenuUpdateItemOptions {
  /** 菜单项 ID（字符串形式的 uint）。 */
  id: string;
  /** 要更新的属性字典（label/text/enabled/disabled/checked/accelerator 等）。 */
  properties: Record<string, unknown>;
}

/** menu.addRoleItem 命令参数。 */
export interface MenuAddRoleItemOptions {
  /** 父菜单项 ID（字符串形式的 uint）。 */
  parentId: string;
  /** 要添加的角色。 */
  role: MenuRole;
  /** 标签文本，留空时由平台实现提供默认本地化文本。 */
  label?: string;
}

/** menu.addStandardEditMenu / menu.addStandardWindowMenu 命令参数。 */
export interface MenuParentOptions {
  /** 父菜单项 ID（字符串形式的 uint）。 */
  parentId: string;
}

/** menu.addStandardHelpMenu 命令参数。 */
export interface MenuAddStandardHelpOptions {
  /** 父菜单项 ID（字符串形式的 uint）。 */
  parentId: string;
  /** 关于对话框元数据。 */
  metadata?: AboutMetadata;
  /** 标签文本，留空时使用默认文本。 */
  label?: string;
}

export const menu = {
  /** 设置应用菜单。 */
  setApplicationMenu: (opts: MenuApplicationMenuOptions) =>
    call<void>("menu.setApplicationMenu", [opts]),
  /** 获取当前应用菜单；未设置时返回 null。 */
  getApplicationMenu: () => call<Menu | null>("menu.getApplicationMenu", []),
  /** 按 ID 注册上下文菜单到全局注册表；省略 Menu 视为移除该 ID 的注册。 */
  setContextMenu: (opts: MenuContextMenuOptions) =>
    call<void>("menu.setContextMenu", [opts]),
  /** 在指定窗口的 (X, Y) 坐标弹出已注册的上下文菜单。 */
  popup: (opts: MenuPopupOptions) => call<void>("menu.popup", [opts]),
  /** 按菜单项 ID 更新属性（label、enabled、disabled、checked、accelerator 等）。 */
  updateMenuItem: (opts: MenuUpdateItemOptions) =>
    call<void>("menu.updateMenuItem", [opts]),
  /** 向指定父菜单追加一个角色菜单项，返回新菜单项 ID。 */
  addRoleItem: (opts: MenuAddRoleItemOptions) =>
    call<string>("menu.addRoleItem", [opts]),
  /** 向指定父菜单追加标准编辑菜单项（Undo/Redo/Sep/Cut/Copy/Paste/SelectAll）。 */
  addStandardEditMenu: (opts: MenuParentOptions) =>
    call<void>("menu.addStandardEditMenu", [opts]),
  /** 向指定父菜单追加标准窗口菜单项（Minimize/Maximize/Sep/CloseWindow）。 */
  addStandardWindowMenu: (opts: MenuParentOptions) =>
    call<void>("menu.addStandardWindowMenu", [opts]),
  /** 向指定父菜单追加标准帮助菜单项（About）。 */
  addStandardHelpMenu: (opts: MenuAddStandardHelpOptions) =>
    call<void>("menu.addStandardHelpMenu", [opts]),
};