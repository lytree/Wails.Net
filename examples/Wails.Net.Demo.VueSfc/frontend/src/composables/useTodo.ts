import { computed, ref } from 'vue'
import { wails } from '@wails-net/runtime'
import { describeError } from './useBackend'
import type { TodoItem, TodoStats } from '../types'

/**
 * 待办事项组合式函数：把 TodoService 的 CRUD 包装成响应式状态，
 * 组件只关心模板，不需要直接和 wails.call 打交道。
 */
export function useTodo() {
  const todos = ref<TodoItem[]>([])
  const stats = ref<TodoStats>({ total: 0, completed: 0, pending: 0 })
  const newTitle = ref('')
  const loading = ref(false)
  const error = ref<string | null>(null)

  const hasCompleted = computed(() => stats.value.completed > 0)
  const isEmpty = computed(() => todos.value.length === 0)

  async function refresh(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const all = await wails.call<TodoItem[]>('TodoService.GetAll', [])
      todos.value = Array.isArray(all) ? all : []

      const s = await wails.call<TodoStats>('TodoService.GetStats', [])
      stats.value = {
        total: s?.total ?? 0,
        completed: s?.completed ?? 0,
        pending: s?.pending ?? 0,
      }
    } catch (e) {
      error.value = describeError(e)
      console.error('[wails] 获取待办事项失败:', e)
    } finally {
      loading.value = false
    }
  }

  async function add(): Promise<boolean> {
    const title = newTitle.value.trim()
    if (!title) return false

    loading.value = true
    error.value = null
    try {
      await wails.call<TodoItem>('TodoService.Add', [title])
      newTitle.value = ''
      await refresh()
      return true
    } catch (e) {
      error.value = describeError(e)
      console.error('[wails] 添加待办失败:', e)
      return false
    } finally {
      loading.value = false
    }
  }

  async function toggle(id: number): Promise<void> {
    await mutate('TodoService.Toggle', [id])
  }

  async function remove(id: number): Promise<void> {
    await mutate('TodoService.Delete', [id])
  }

  async function clearCompleted(): Promise<number> {
    error.value = null
    try {
      const removed = await wails.call<number>('TodoService.ClearCompleted', [])
      await refresh()
      return removed ?? 0
    } catch (e) {
      error.value = describeError(e)
      console.error('[wails] 清除已完成失败:', e)
      return 0
    }
  }

  /** 轻量写操作：调用后统一刷新列表与统计。 */
  async function mutate(name: string, args: unknown[]): Promise<void> {
    error.value = null
    try {
      await wails.call(name, args)
      await refresh()
    } catch (e) {
      error.value = describeError(e)
      console.error(`[wails] ${name} 失败:`, e)
    }
  }

  return {
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
  }
}
