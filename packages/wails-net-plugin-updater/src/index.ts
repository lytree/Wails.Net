/**
 * @wails-net/plugin-updater — Updater 插件前端封装（前后端一体双包的前端薄壳）。
 * 命令前缀 `updater.*`，后端经 L2 抽象层 `defineCommand` 强类型转发。
 *
 * 注意：后端命令返回 JSON 字符串（版本/可用性/下载路径），本封装在此处解析为对象，
 * 调用方无需关心线协议细节。
 *
 * @platform windows,linux,macos  桌面通用插件
 */
import { defineCommand } from "@wails-net/runtime";

/** 检查更新结果（对应后端 updater.check / updater.checkAndDownload 的 JSON payload）。 */
export interface UpdateCheckResult {
  /** 最新版本号；无可用更新时为空字符串。 */
  version: string;
  /** 是否存在可用更新。 */
  available: boolean;
  /** 已下载的更新包本地路径（仅 checkAndDownload 返回，可空）。 */
  path?: string | null;
}

const checkRaw = defineCommand<[], string>("updater.check", "none");
const checkAndDownloadRaw = defineCommand<[], string>("updater.checkAndDownload", "none");
const downloadRaw = defineCommand<[], string>("updater.download", "none");
const installRaw = defineCommand<[string], void>("updater.install", "spread");

/** 解析后端 JSON 字符串 payload，容错返回空结果。 */
function parseResult(raw: string): UpdateCheckResult {
  try {
    const obj = JSON.parse(raw || "{}") as Partial<UpdateCheckResult>;
    return {
      version: obj.version ?? "",
      available: Boolean(obj.available),
      path: obj.path ?? null,
    };
  } catch {
    return { version: "", available: false, path: null };
  }
}

/**
 * 检查更新，返回解析后的结果对象。
 * 服务未注册或检查失败时返回 `{ version: "", available: false }`。
 */
export async function check(): Promise<UpdateCheckResult> {
  return parseResult(await checkRaw());
}

/**
 * 检查并下载更新，返回解析后的结果对象（含本地下载路径）。
 * 服务未注册或操作失败时返回 `{ version: "", available: false, path: null }`。
 */
export async function checkAndDownload(): Promise<UpdateCheckResult> {
  return parseResult(await checkAndDownloadRaw());
}

/**
 * 下载更新包，返回已下载归档的本地路径；无可用下载地址时返回空字符串。
 */
export function download(): ReturnType<typeof downloadRaw> {
  return downloadRaw();
}

/**
 * 安装指定路径的更新包（路径由 download / checkAndDownload 返回）。
 * 服务未注册或安装失败时静默忽略。
 */
export function install(archivePath: string): ReturnType<typeof installRaw> {
  return installRaw(archivePath);
}
