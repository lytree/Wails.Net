<script setup lang="ts">
import { computed, markRaw, ref, type Component } from 'vue'
import GreetingPanel from './components/GreetingPanel.vue'
import TodoPanel from './components/TodoPanel.vue'
import CounterPanel from './components/CounterPanel.vue'
import SystemPanel from './components/SystemPanel.vue'
import EventPanel from './components/EventPanel.vue'

interface TabItem {
  key: string
  label: string
  component: Component
}

// markRaw：组件定义不需要深度响应式，避免 Vue 把组件对象变成 proxy
const tabs: TabItem[] = [
  { key: 'greeting', label: '问候服务', component: markRaw(GreetingPanel) },
  { key: 'todo', label: '待办事项', component: markRaw(TodoPanel) },
  { key: 'counter', label: '计数器插件', component: markRaw(CounterPanel) },
  { key: 'system', label: '系统能力', component: markRaw(SystemPanel) },
  { key: 'events', label: '事件通信', component: markRaw(EventPanel) },
]

const activeTab = ref('greeting')

const activeComponent = computed<Component>(() => {
  const found = tabs.find((tab) => tab.key === activeTab.value)
  return found ? found.component : GreetingPanel
})
</script>

<template>
  <div class="app">
    <header class="header">
      <h1>Wails.Net Vue SFC Demo</h1>
      <p class="subtitle">Vue 3.5 单文件组件（&lt;script setup&gt; + 组合式 API + TypeScript + Vite）</p>
    </header>

    <nav class="tabs">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        type="button"
        class="tab"
        :class="{ active: activeTab === tab.key }"
        @click="activeTab = tab.key"
      >
        {{ tab.label }}
      </button>
    </nav>

    <!-- KeepAlive：切换页签时保留各面板状态（如待办列表输入、事件订阅） -->
    <KeepAlive>
      <component :is="activeComponent" />
    </KeepAlive>
  </div>
</template>
