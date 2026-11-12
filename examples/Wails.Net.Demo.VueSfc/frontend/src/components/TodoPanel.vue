<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useTodo } from '../composables/useTodo'
import ResultBanner from './ResultBanner.vue'

const {
  todos,
  stats,
  newTitle,
  loading,
  error,
  hasCompleted,
  isEmpty,
  refresh,
  add,
  toggle,
  remove,
  clearCompleted,
} = useTodo()

const clearedText = ref('')

/** 演示 watch：统计变化时更新提示文本 */
watch(
  () => stats.value.completed,
  (done) => {
    if (done > 0) clearedText.value = `已完成 ${done} 项，可一键清除`
  },
)

function formatDate(iso: string): string {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}

async function onClear(): Promise<void> {
  const removed = await clearCompleted()
  clearedText.value = `已清除 ${removed} 项已完成待办`
}

onMounted(() => {
  void refresh()
})
</script>

<template>
  <section class="panel">
    <h2>待办事项</h2>
    <p class="description">演示组合式函数封装 CRUD：状态、计算属性与后端绑定的配合（TodoService）</p>

    <div class="todo-stats">
      <span>总数：<strong>{{ stats.total }}</strong></span>
      <span>已完成：<strong>{{ stats.completed }}</strong></span>
      <span>未完成：<strong>{{ stats.pending }}</strong></span>
      <button class="btn btn-small" :disabled="!hasCompleted" @click="onClear">清除已完成</button>
      <button class="btn btn-small" :disabled="loading" @click="refresh">刷新</button>
    </div>

    <div class="form-group">
      <input
        v-model="newTitle"
        type="text"
        placeholder="输入待办事项标题，回车即可添加…"
        @keydown.enter="add"
      />
      <button class="btn" :disabled="loading || !newTitle.trim()" @click="add">添加</button>
    </div>

    <ul class="todo-list">
      <li v-if="isEmpty" class="todo-item">
        <span class="todo-title">暂无待办事项</span>
      </li>
      <li
        v-for="todo in todos"
        :key="todo.id"
        class="todo-item"
        :class="{ completed: todo.completed }"
      >
        <input type="checkbox" :checked="todo.completed" @change="toggle(todo.id)" />
        <span class="todo-title">{{ todo.title }}</span>
        <span class="todo-date">{{ formatDate(todo.createdAt) }}</span>
        <button class="btn btn-small btn-danger" @click="remove(todo.id)">删除</button>
      </li>
    </ul>

    <ResultBanner :text="clearedText" />
    <ResultBanner :text="error" kind="error" />
  </section>
</template>
