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
  name: 'Tasks',
  setup() {
    const cancel = (id: string) => {
      const w: any = window
      w.wails.events.emit('lybox:cancel-task', { taskId: id })
    }

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('tasks.title')}</h1>
        {tasks.value.length === 0 ? (
          <p class="text-slate-500">{t('tasks.empty')}</p>
        ) : (
          <div class="space-y-2">
            {tasks.value.map((tsk) => (
              <div class="rounded-xl bg-white p-4 shadow dark:bg-slate-800">
                <div class="flex items-center justify-between">
                  <span class="font-medium">{tsk.name}</span>
                  <span class={statusColor[tsk.status] ?? 'text-slate-500'}>{tsk.status}</span>
                </div>
                <div class="mt-2 h-2 w-full overflow-hidden rounded bg-slate-200 dark:bg-slate-700">
                  <div class="h-full bg-indigo-500" style={{ width: `${tsk.progress}%` }} />
                </div>
                {tsk.detail && (
                  <div class="mt-1 text-xs text-slate-400">{tsk.detail}</div>
                )}
                {tsk.status === 'running' && (
                  <button
                    class="mt-2 text-sm text-red-500 hover:underline"
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
