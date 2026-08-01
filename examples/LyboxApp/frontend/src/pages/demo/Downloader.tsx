import { defineComponent, ref } from 'vue'
import { Lybox } from '../../api'
import { t } from '../../i18n'

export default defineComponent({
  name: 'Downloader',
  setup() {
    const url = ref('https://www.w3.org/2008/site/images/favicon.ico')
    const name = ref('')
    const status = ref('')

    const onDownload = async () => {
      status.value = t('common.loading')
      try {
        const r: any = await Lybox.download(url.value, name.value || undefined)
        status.value = '✅ ' + (r.path ?? '')
      } catch (e: any) {
        status.value = '❌ ' + (e?.message ?? e)
      }
    }

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('demo.downloader.title')}</h1>
        <div class="space-y-2">
          <input
            class="w-full rounded border border-slate-300 px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
            placeholder={t('demo.downloader.url')}
            value={url.value}
            onInput={(e: any) => (url.value = e.target.value)}
          />
          <input
            class="w-full rounded border border-slate-300 px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
            placeholder={t('demo.downloader.name')}
            value={name.value}
            onInput={(e: any) => (name.value = e.target.value)}
          />
          <button
            class="rounded bg-indigo-600 px-3 py-2 text-white hover:bg-indigo-700"
            onClick={onDownload}
          >
            {t('demo.downloader.start')}
          </button>
        </div>
        {status.value && <div class="font-mono text-sm break-all">{status.value}</div>}
        <p class="text-sm text-slate-500">{t('demo.downloader.progress')}</p>
      </div>
    )
  },
})
