import { reactive, ref } from 'vue'
import { Lybox } from './api'
import { setLang } from './i18n'

/// <summary>导航项列表（来自后端）。</summary>
export const nav = ref<any[]>([])

/// <summary>插件清单列表。</summary>
export const plugins = ref<any[]>([])

/// <summary>任务列表。</summary>
export const tasks = ref<any[]>([])

/// <summary>当前活动对话框列表。</summary>
export const dialogs = ref<any[]>([])

/// <summary>设置（语言 / 主题 / 插件开关）。</summary>
export const settings = reactive({
  language: 'zh-CN',
  theme: 'light',
  pluginEnabled: {} as Record<string, boolean>,
})

/// <summary>当前主题。</summary>
export const theme = ref('light')

/// <summary>当前路由（侧边栏选中项）。</summary>
export const currentRoute = ref('dashboard')

/// <summary>应用主题到 <html> 的 dark 类。</summary>
export function applyTheme(t: string) {
  theme.value = t
  if (t === 'dark') {
    document.documentElement.classList.add('dark')
  } else {
    document.documentElement.classList.remove('dark')
  }
}

/// <summary>加载导航树。</summary>
export async function loadNav() {
  nav.value = await Lybox.getNavigation()
}

/// <summary>加载插件清单。</summary>
export async function loadPlugins() {
  plugins.value = await Lybox.getPlugins()
}

/// <summary>加载任务列表。</summary>
export async function loadTasks() {
  tasks.value = await Lybox.getTasks()
}

/// <summary>加载并应用设置。</summary>
export async function loadSettings() {
  const s = await Lybox.getSettings()
  settings.language = s.language ?? 'zh-CN'
  settings.theme = s.theme ?? 'light'
  settings.pluginEnabled = s.pluginEnabled ?? {}
  setLang(settings.language)
  applyTheme(settings.theme)
}

/// <summary>启用/禁用插件并刷新。</summary>
export async function togglePlugin(id: string, enabled: boolean) {
  await Lybox.setPluginEnabled(id, enabled)
  settings.pluginEnabled[id] = enabled
  await loadPlugins()
  await loadNav()
}

/// <summary>保存设置。</summary>
export async function saveSettings() {
  await Lybox.saveSettings(settings.language, settings.theme, settings.pluginEnabled)
}

function upsertTask(d: any) {
  const i = tasks.value.findIndex((t) => t.id === d.id)
  if (i >= 0) {
    tasks.value[i] = d
  } else {
    tasks.value = [d, ...tasks.value]
  }
}

/// <summary>订阅后端事件（语言/对话框/任务/插件变更）。</summary>
export function setupSubscriptions() {
  const w: any = window
  w.wails.events.on('lybox:language-changed', (d: any) => {
    settings.language = d.code
    setLang(d.code)
  })
  w.wails.events.on('lybox:dialog', (d: any) => {
    dialogs.value = [...dialogs.value, d]
  })
  w.wails.events.on('lybox:task-update', (d: any) => {
    upsertTask(d)
  })
  w.wails.events.on('lybox:plugins-changed', (d: any) => {
    settings.pluginEnabled[d.id] = d.enabled
    loadPlugins()
    loadNav()
  })
}
