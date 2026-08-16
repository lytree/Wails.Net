/**
 * @wails-net/plugin-localization — localization 插件前端封装。
 * 命令前缀 `localization.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * localization 插件前端 API（命令前缀 `localization.*`）。
 * 对应后端 Wails.Net.Plugins.Localization.LocalizationPlugin 注册的命令。
 */
export const localization = {
  /** 设置当前 locale（对应 `localization.setLocale`）。 */
  setLocale: (locale: string) => call<void>("localization.setLocale", [locale]),
  /** 获取当前 locale（对应 `localization.getLocale`）。 */
  getLocale: () => call<string>("localization.getLocale", []),
  /**
   * 翻译指定 key 到当前 locale，支持 `{param}` 参数插值（对应 `localization.t`）。
   * @param key 翻译键。
   * @param paramsJson 插值参数 JSON 对象字符串（如 `'{"name":"wails"}'`），可为 null。
   * @returns 翻译后的字符串；未找到时返回 key 本身。
   */
  t: (key: string, paramsJson?: string | null) =>
    call<string>("localization.t", [key, paramsJson ?? null]),
  /**
   * 注册翻译资源（对应 `localization.registerTranslations`）。
   * @param locale locale 字符串（如 "en-US"、"zh-CN"）。
   * @param translationsJson key→value 翻译字典的 JSON 字符串。
   */
  registerTranslations: (locale: string, translationsJson: string) =>
    call<void>("localization.registerTranslations", [locale, translationsJson]),
  /** 获取已注册的所有 locale（对应 `localization.getAvailableLocales`）。 */
  getAvailableLocales: () => call<string[]>("localization.getAvailableLocales", []),
};