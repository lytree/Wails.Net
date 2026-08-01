import { defineComponent, ref } from 'vue'
import { Lybox } from '../../api'
import { t } from '../../i18n'

export default defineComponent({
  name: 'ButtonsInputs',
  setup() {
    const text = ref('Wails.Net')
    const number = ref(42)
    const toggle = ref(true)
    const echo = ref('')
    const combined = ref('')

    const onEcho = async () => {
      echo.value = await Lybox.buttonsEcho(text.value)
    }
    const onCombine = async () => {
      combined.value = await Lybox.buttonsCombine(text.value, number.value, toggle.value)
    }

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('demo.buttons.title')}</h1>
        <div class="space-y-3 rounded-xl bg-white p-4 shadow dark:bg-slate-800">
          <label class="block text-sm">{t('demo.buttons.text')}</label>
          <input
            class="w-full rounded border border-slate-300 px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
            value={text.value}
            onInput={(e: any) => (text.value = e.target.value)}
          />
          <label class="block text-sm">{t('demo.buttons.number')}</label>
          <input
            type="number"
            class="w-full rounded border border-slate-300 px-3 py-2 dark:border-slate-600 dark:bg-slate-700"
            value={number.value}
            onInput={(e: any) => (number.value = Number(e.target.value))}
          />
          <label class="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={toggle.value}
              onChange={(e: any) => (toggle.value = e.target.checked)}
            />
            {t('demo.buttons.toggle')}
          </label>
          <div class="flex gap-2">
            <button
              class="rounded bg-indigo-600 px-3 py-2 text-white hover:bg-indigo-700"
              onClick={onEcho}
            >
              {t('demo.buttons.echo')}
            </button>
            <button
              class="rounded bg-emerald-600 px-3 py-2 text-white hover:bg-emerald-700"
              onClick={onCombine}
            >
              {t('demo.buttons.combine')}
            </button>
          </div>
          {echo.value && <div class="font-mono text-sm">{echo.value}</div>}
          {combined.value && <div class="font-mono text-sm">{combined.value}</div>}
        </div>
      </div>
    )
  },
})
