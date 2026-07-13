import { defineComponent, ref, type Component } from 'vue'
import Greeting from './components/Greeting'
import Todo from './components/Todo'
import Counter from './components/Counter'
import SystemPanel from './components/SystemPanel'

interface TabItem {
  key: string
  label: string
  component: Component
}

export default defineComponent({
  name: 'App',
  setup() {
    const tabs: TabItem[] = [
      { key: 'greeting', label: '问候服务', component: Greeting },
      { key: 'todo', label: '待办事项', component: Todo },
      { key: 'counter', label: '计数器插件', component: Counter },
      { key: 'system', label: '系统信息', component: SystemPanel },
    ]

    const activeTab = ref<string>('greeting')

    const currentComponent = (): Component => {
      const found = tabs.find((t) => t.key === activeTab.value)
      return found ? found.component : Greeting
    }

    return () => (
      <div class="app">
        <div class="header">
          <h1>Wails.Net Vue Demo</h1>
          <p class="subtitle">Vue 3.5 + vue-jsx-vapor + TypeScript + TSX（无 SFC 模板）</p>
        </div>

        <div class="tabs">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              class={'tab' + (activeTab.value === tab.key ? ' active' : '')}
              onClick={() => (activeTab.value = tab.key)}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {(() => {
          const Comp = currentComponent()
          return <Comp />
        })()}
      </div>
    )
  },
})
