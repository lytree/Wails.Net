import { defineComponent } from 'vue'
import { tasks } from '../store'
import { t } from '../i18n'

const statusColor: Record<string, string> = {
  running: 'text-blue-500',
  done: 'text-green-500',
  failed: 'text-red-500',
  canceled: 'text-slate-400',
}

export default defineComponent({
  name: 'TaskTray',
  setup() {
    const cancel = (id: string) => {
      const w: any = window
      w.wails.events.emit('lybox:cancel-task', { taskId: id })
    }

    return () => (
      <div class="fixed bottom-4 right-4 z-40 w-80">
        {tasks.value.length === 0 ? null : (
          <div class="space-y-2 rounded-xl bg-white p-3 shadow-lg dark:bg-slate-800">
            <div class="mb-1 text-xs font-semibold text-slate-500">{t('tasks.title')}</div>
            {tasks.value.map((tsk) => (
              <div
                key={tsk.id}
                class="rounded-lg border border-slate-200 p-2 dark:border-slate-700"
              >
                <div class="flex items-center justify-between text-sm">
                  <span class="truncate">{tsk.name}</span>
                  <span class={statusColor[tsk.status] ?? 'text-slate-500'}>{tsk.status}</span>
                </div>
                <div class="mt-1 h-1.5 w-full overflow-hidden rounded bg-slate-200 dark:bg-slate-700">
                  <div class="h-full bg-indigo-500" style={{ width: `${tsk.progress}%` }} />
                </div>
                {tsk.detail && (
                  <div class="mt-1 truncate text-xs text-slate-400">{tsk.detail}</div>
                )}
                {tsk.status === 'running' && (
                  <button
                    class="mt-1 text-xs text-red-500 hover:underline"
                    onClick={() => cancel(tsk.id)}
                  >
                    {t('tasks.cancel')}
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    )
  },
})
