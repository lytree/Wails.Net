/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";
import type { ShellResult } from "@wails-net/runtime";

export const shell = {
  execute: (command: string, args?: string[]) => call<ShellResult>("shell.execute", [command, args]),
  executeAsync: (command: string, args?: string[]) => call<ShellResult>("shell.executeAsync", [command, args]),
  open: (target: string) => call<void>("shell.open", [target]),
  openUrl: (url: string) => call<void>("shell.openUrl", [url]),
};
