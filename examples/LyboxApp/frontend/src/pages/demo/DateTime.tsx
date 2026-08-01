import { defineComponent, ref, onMounted } from 'vue'
import { Lybox } from '../../api'
import { t } from '../../i18n'

export default defineComponent({
  name: 'DateTime',
  setup() {
    const now = ref('')
    const utc = ref('')
    const format = ref('yyyy-MM-dd HH:mm:ss')
    const formatted = ref('')
    const parts = ref<Record<string, number>>({})

    onMounted(refresh)

    async function refresh() {
      now.value = await Lybox.dateNow('yyyy-MM-dd HH:mm:ss')
      utc.value = await Lybox.dateUtc()
      parts.value = await Lybox.dateParts()
    }

    const onFormat = async () => {
      formatted.value = await Lybox.dateNow(format.value)
    }

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('demo.datetime.title')}</h1>
        <div class="grid grid-cols-2 gap-3">
          <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
            <div class="text-sm text-slate-500">{t('demo.datetime.now')}</div>
            <div class="font-mono">{now.value}</div>
          </div>
          <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
            <div class="text-sm text-slate-500">{t('demo.datetime.utc')}</div>
            <div class="font-mono">{utc.value}</div>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <input
            class="rounded border border-slate-300 px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
            value={format.value}
            onInput={(e: any) => (format.value = e.target.value)}
          />
          <button
            class="rounded bg-indigo-600 px-3 py-2 text-white hover:bg-indigo-700"
            onClick={onFormat}
          >
            {t('demo.datetime.format')}
          </button>
        </div>
        {formatted.value && <div class="font-mono">{formatted.value}</div>}
        <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
          <div class="mb-1 text-sm text-slate-500">{t('demo.datetime.parts')}</div>
          <div class="grid grid-cols-4 gap-2 text-sm">
            {Object.entries(parts.value).map(([k, v]) => (
              <div class="rounded bg-slate-100 p-2 dark:bg-slate-700">
                <div class="text-xs text-slate-400">{k}</div>
                <div class="font-mono">{v}</div>
              </div>
            ))}
          </div>
        </div>
      </div>
    )
  },
})
