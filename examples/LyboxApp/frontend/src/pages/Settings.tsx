import { defineComponent, onMounted, ref } from 'vue'
import { settings, theme, applyTheme, saveSettings } from '../store'
import { t, lang, setLang } from '../i18n'
import { Lybox } from '../api'

export default defineComponent({
  name: 'Settings',
  setup() {
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
      <div class="max-w-2xl space-y-6">
        <h1 class="text-2xl font-bold">{t('settings.title')}</h1>
        <section class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
          <label class="mb-1 block text-sm font-medium">{t('settings.language')}</label>
          <select
            value={lang.value}
            onChange={onLang}
            class="w-full rounded border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
          >
            {languages.value.map((l) => (
              <option key={l.code} value={l.code}>
                {l.name}
              </option>
            ))}
          </select>
        </section>
        <section class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
          <label class="mb-1 block text-sm font-medium">{t('settings.theme')}</label>
          <button
            onClick={onTheme}
            class="rounded bg-indigo-600 px-3 py-2 text-sm text-white hover:bg-indigo-700"
          >
            {theme.value === 'dark' ? t('common.light') : t('common.dark')}
          </button>
        </section>
      </div>
    )
  },
})
