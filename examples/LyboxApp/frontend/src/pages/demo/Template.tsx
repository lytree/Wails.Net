import { defineComponent, ref } from 'vue'
import { Lybox } from '../../api'
import { t } from '../../i18n'

export default defineComponent({
  name: 'Template',
  setup() {
    const text = ref('Hello')
    const result = ref('')

    const onEcho = async () => {
      result.value = await Lybox.templateEcho(text.value)
    }

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('demo.template.title')}</h1>
        <p class="text-slate-500">{t('demo.template.desc')}</p>
        <div class="flex items-center gap-2">
          <input
            class="rounded border border-slate-300 px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
            value={text.value}
            onInput={(e: any) => (text.value = e.target.value)}
          />
          <button
            class="rounded bg-indigo-600 px-3 py-2 text-white hover:bg-indigo-700"
            onClick={onEcho}
          >
            {t('demo.template.echo')}
          </button>
        </div>
        {result.value && (
          <div class="rounded bg-slate-100 p-3 font-mono dark:bg-slate-800">{result.value}</div>
        )}
      </div>
    )
  },
})
