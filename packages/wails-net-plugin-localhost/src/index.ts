/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const localhost = {
  /**
   * 启动本地服务器。
   * @param port 监听端口；传 `0` 由后端自动分配空闲端口。
   * @param rootDir 静态文件根目录。
   * @returns 服务器根 URL。
   */
  start: (port = 0, rootDir?: string) => call<string>("localhost.start", [port, rootDir ?? null]),
  stop: (port: number) => call<void>("localhost.stop", [port]),
  getUrl: (port: number) => call<string | null>("localhost.getUrl", [port]),
  isRunning: (port: number) => call<boolean>("localhost.isRunning", [port]),
  setRoot: (port: number, rootDir: string) => call<void>("localhost.setRoot", [port, rootDir]),
  /**
   * 注册路由。
   * @param method HTTP 方法（如 `"GET"`），后端会转为大写。
   */
  addRoute: (port: number, route: string, method: string) =>
    call<void>("localhost.addRoute", [port, route, method]),
  removeRoute: (port: number, route: string) =>
    call<void>("localhost.removeRoute", [port, route]),
  listRoutes: (port: number) => call<string[] | null>("localhost.listRoutes", [port]),
};
