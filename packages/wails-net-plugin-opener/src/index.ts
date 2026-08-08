/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const opener = {
  /**
   * 用外部程序打开 URL。
   * @param target 可选的目标程序；省略则用系统默认处理程序。
   */
  openUrl: (url: string, target?: string) => call<boolean>("opener.openUrl", [url, target ?? null]),
  /** 用外部程序打开本地路径。 */
  openPath: (path: string, target?: string) =>
    call<boolean>("opener.openPath", [path, target ?? null]),
  /** 在文件管理器中定位并选中文件。 */
  revealInFolder: (path: string) => call<void>("opener.revealInFolder", [path]),
  isUrlAllowed: (url: string) => call<boolean>("opener.isUrlAllowed", [url]),
  /** 校验 URL，返回规范化后的 URL；不合法时返回 `null`。 */
  verifyUrl: (url: string) => call<string | null>("opener.verifyUrl", [url]),
};
