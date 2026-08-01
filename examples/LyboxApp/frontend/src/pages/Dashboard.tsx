import { defineComponent, onMounted, ref } from 'vue'
import { plugins, tasks } from '../store'
import { Lybox } from '../api'
import { t } from '../i18n'

export default defineComponent({
  name: 'Dashboard',
  setup() {
    const info = ref<Record<string, string>>({})

    onMounted(async () => {
      info.value = await Lybox.getAppInfo()
    })

    return () => {
      const cards = [
        { title: t('dashboard.plugins'), value: String(plugins.value.length) },
        {
          title: t('dashboard.enabled'),
          value: String(plugins.value.filter((p) => p.enabled).length),
        },
        {
          title: t('dashboard.tasks'),
          value: String(tasks.value.filter((x) => x.status === 'running').length),
        },
      ]

      return (
        <div class="space-y-6">
          <div>
            <h1 class="text-2xl font-bold">{t('dashboard.welcome')}</h1>
            <p class="mt-1 text-slate-500">{t('dashboard.subtitle')}</p>
          </div>
          <div class="grid grid-cols-3 gap-4">
            {cards.map((c) => (
              <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
                <div class="text-sm text-slate-500">{c.title}</div>
                <div class="mt-1 text-3xl font-bold">{c.value}</div>
              </div>
            ))}
          </div>
          <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
            <div class="mb-2 text-sm font-semibold">{t('app.title')}</div>
            <dl class="grid grid-cols-2 gap-2 text-sm">
              {Object.entries(info.value).map(([k, v]) => (
                <div
                  class="flex justify-between border-b border-slate-100 py-1 dark:border-slate-700"
                >
                  <dt class="text-slate-500">{k}</dt>
                  <dd class="font-mono">{v}</dd>
                </div>
              ))}
            </dl>
          </div>
        </div>
      )
    }
  },
})
