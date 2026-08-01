import { defineComponent } from 'vue'
import { nav, currentRoute } from '../store'
import { t } from '../i18n'

const iconMap: Record<string, string> = {
  home: '🏠',
  puzzle: '🧩',
  tasks: '📋',
  settings: '⚙️',
  document: '📄',
  button: '🔘',
  clock: '🕒',
  chat: '💬',
  download: '⬇️',
  menu: '☰',
  circle: '•',
}

export default defineComponent({
  name: 'Sidebar',
  setup() {
    return () => (
      <aside class="w-60 shrink-0 border-r border-slate-200 bg-white p-3 dark:border-slate-700 dark:bg-slate-800">
        <div class="mb-4 px-2 text-lg font-bold">LYBox</div>
        <nav class="flex flex-col gap-1">
          {nav.value.map((item) => (
            <button
              key={item.key}
              class={`flex items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition ${
                currentRoute.value === item.key
                  ? 'bg-indigo-600 text-white'
                  : 'hover:bg-slate-100 dark:hover:bg-slate-700'
              }`}
              onClick={() => {
                currentRoute.value = item.key
              }}
            >
              <span class="w-5 text-center">{iconMap[item.icon] ?? '•'}</span>
              <span class="truncate">{t(item.titleKey)}</span>
            </button>
          ))}
        </nav>
      </aside>
    )
  },
})
