function wails(): any {
  return (window as any).wails
}

/// <summary>调用后端 [Binding]/[Command] 方法。</summary>
export async function call<T = any>(name: string, args: any[] = []): Promise<T> {
  return (await wails().call(name, args)) as T
}

/// <summary>后端绑定方法的类型化封装。</summary>
export const Lybox = {
  getPlugins: () => call<any[]>('LyboxCoreService.GetPlugins'),
  setPluginEnabled: (id: string, enabled: boolean) =>
    call('LyboxCoreService.SetPluginEnabled', [id, enabled]),
  getNavigation: () => call<any[]>('LyboxCoreService.GetNavigation'),
  getTasks: () => call<any[]>('LyboxCoreService.GetTasks'),
  getAppInfo: () => call<Record<string, string>>('LyboxCoreService.GetAppInfo'),

  getSettings: () => call<any>('SettingsService.GetSettings'),
  saveSettings: (language: string, theme: string, pluginEnabled: Record<string, boolean>) =>
    call('SettingsService.SaveSettings', [language, theme, pluginEnabled]),

  getLanguages: () => call<any[]>('LocalizationService.GetLanguages'),
  getCurrentLanguage: () => call('LocalizationService.GetCurrentLanguage'),
  setLanguage: (code: string) => call('LocalizationService.SetLanguage', [code]),

  templateEcho: (text: string) => call('TemplateService.Echo', [text]),
  templateInfo: () => call('TemplateService.GetInfo'),
  buttonsEcho: (text: string) => call('ButtonsInputsService.Echo', [text]),
  buttonsCombine: (text: string, number: number, toggle: boolean) =>
    call('ButtonsInputsService.Combine', [text, number, toggle]),
  dateNow: (format: string) => call('DateTimeService.Now', [format]),
  dateUtc: () => call('DateTimeService.UtcNow'),
  dateParts: () => call('DateTimeService.Parts'),
  showDialog: (type: string, title: string, message: string, confirmText?: string, cancelText?: string) =>
    call('DialogFeedbacksService.ShowDialog', [type, title, message, confirmText, cancelText]),
  download: (url: string, name?: string) => call('DownloaderService.Download', [url, name]),
  getMenuTree: () => call<any[]>('NavigationMenusService.GetMenuTree'),
}
