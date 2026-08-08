/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";
import type { HttpRequestOptions, HttpResponse } from "@wails-net/runtime";

export const http = {
  /** 通用请求，支持自定义方法与请求头。 */
  fetch: (options: HttpRequestOptions) => call<HttpResponse>("http.fetch", [options]),
  get: (url: string) => call<HttpResponse>("http.get", [url]),
  post: (url: string, body?: string) => call<HttpResponse>("http.post", [url, body ?? null]),
  put: (url: string, body?: string) => call<HttpResponse>("http.put", [url, body ?? null]),
  delete: (url: string) => call<HttpResponse>("http.delete", [url]),
};
