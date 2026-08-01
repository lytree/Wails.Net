import { defineComponent, onMounted, ref } from 'vue'
import { settings, theme, applyTheme, saveSettings } from '../store'
import { t, lang, setLang } from '../i18n'
import { Lybox } from '../api'

export default defineComponent({
  name: 'TopBar',
  props: {
    title: { type: String, default: 'app.title' },
  },
  setup(props) {
    const languages = ref<any[]>([])

    onMounted(async () => {
      languages.value = await Lybox.getLanguages()
    })

    const onLang = async (e: any) => {
      const code = e.target.value
      await Lybox.setLanguage(code)
      settings.language = code
      setLang(code)
    }

    const onTheme = async () => {
      const next = theme.value === 'dark' ? 'light' : 'dark'
      applyTheme(next)
      settings.theme = next
      await saveSettings()
    }

    return () => (
      <header class="flex h-14 shrink-0 items-center justify-between border-b border-slate-200 bg-white px-6 dark:border-slate-700 dark:bg-slate-800">
        <div class="text-sm font-medium text-slate-600 dark:text-slate-300">{t(props.title)}</div>
        <div class="flex items-center gap-3">
          <select
            value={lang.value}
            onChange={onLang}
            class="rounded border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-600 dark:bg-slate-700"
          >
            {languages.value.map((l) => (
              <option key={l.code} value={l.code}>
                {l.name}
              </option>
            ))}
          </select>
          <button
            onClick={onTheme}
            class="rounded border border-slate-300 px-3 py-1 text-sm hover:bg-slate-100 dark:border-slate-600 dark:hover:bg-slate-700"
          >
            {theme.value === 'dark' ? t('common.light') : t('common.dark')}
          </button>
        </div>
      </header>
    )
  },
})
