import { defineComponent } from 'vue'
import { plugins, togglePlugin } from '../store'
import { t } from '../i18n'

export default defineComponent({
  name: 'Plugins',
  setup() {
    const onToggle = async (p: any) => {
      await togglePlugin(p.id, !p.enabled)
    }

    return () => (
      <div class="space-y-4">
        <div>
          <h1 class="text-2xl font-bold">{t('plugins.title')}</h1>
          <p class="mt-1 text-slate-500">{t('plugins.desc')}</p>
        </div>
        <div class="grid grid-cols-1 gap-3 md:grid-cols-2">
          {plugins.value.map((p) => (
            <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
              <div class="flex items-start justify-between">
                <div>
                  <div class="font-semibold">{p.name}</div>
                  <div class="text-xs text-slate-400">{p.id}</div>
                </div>
                <label class="relative inline-flex cursor-pointer items-center">
                  <input
                    type="checkbox"
                    class="peer sr-only"
                    checked={p.enabled}
                    onChange={() => onToggle(p)}
                  />
                  <div class="h-6 w-11 rounded-full bg-slate-300 after:absolute after:left-0.5 after:top-0.5 after:h-5 after:w-5 after:rounded-full after:bg-white after:content-[''] after:transition peer-checked:bg-indigo-600 peer-checked:after:translate-x-5 dark:bg-slate-600" />
                </label>
              </div>
              <p class="mt-2 text-sm text-slate-600 dark:text-slate-300">{p.description}</p>
              <div class="mt-3 flex gap-3 text-xs text-slate-400">
                <span>
                  <b class="text-slate-500">{t('plugins.author')}:</b> {p.author}
                </span>
                <span>
                  <b class="text-slate-500">{t('plugins.version')}:</b> {p.version}
                </span>
                <span>
                  <b class="text-slate-500">{t('plugins.category')}:</b> {p.category}
                </span>
              </div>
            </div>
          ))}
        </div>
      </div>
    )
  },
})
