import { defineComponent } from 'vue'
import { dialogs } from '../store'
import { t } from '../i18n'

const typeColor: Record<string, string> = {
  info: 'bg-blue-500',
  success: 'bg-green-500',
  warning: 'bg-amber-500',
  error: 'bg-red-500',
  confirm: 'bg-indigo-600',
}

export default defineComponent({
  name: 'DialogHost',
  setup() {
    const close = (d: any, result: string) => {
      const w: any = window
      w.wails.events.emit('lybox:dialog-result', { id: d.id, result })
      dialogs.value = dialogs.value.filter((x) => x.id !== d.id)
    }

    return () => (
      <>
        {dialogs.value.map((d) => (
          <div
            key={d.id}
            class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
          >
            <div class="w-96 rounded-xl bg-white p-5 shadow-xl dark:bg-slate-800">
              <div
                class={`mb-3 inline-flex rounded-full px-3 py-1 text-xs font-semibold text-white ${
                  typeColor[d.type] ?? 'bg-slate-500'
                }`}
              >
                {t('dialog.' + d.type)}
              </div>
              <h3 class="mb-2 text-lg font-semibold">{d.title}</h3>
              <p class="mb-4 text-sm text-slate-600 dark:text-slate-300">{d.message}</p>
              <div class="flex justify-end gap-2">
                {d.cancelText && (
                  <button
                    class="rounded border border-slate-300 px-3 py-1 text-sm hover:bg-slate-100 dark:border-slate-600 dark:hover:bg-slate-700"
                    onClick={() => close(d, 'cancel')}
                  >
                    {d.cancelText}
                  </button>
                )}
                <button
                  class="rounded bg-indigo-600 px-3 py-1 text-sm text-white hover:bg-indigo-700"
                  onClick={() => close(d, 'confirm')}
                >
                  {d.confirmText ?? t('common.ok')}
                </button>
              </div>
            </div>
          </div>
        ))}
      </>
    )
  },
})
