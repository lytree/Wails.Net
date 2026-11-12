import { onUnmounted, ref, shallowRef } from 'vue'
import { wails } from '@wails-net/runtime'

/**
 * 事件订阅组合式函数：订阅后端广播的事件并保留最近 `limit` 条记录。
 *
 * 关键点（Vue 3 生命周期）：
 * - 订阅在组件卸载时自动取消（`onUnmounted`），避免热更新/切页签后重复回调；
 * - `active` 用 ref 承载订阅状态，模板可据此切换「开始/停止」按钮。
 */
export function useEventStream<T>(name: string, limit = 8) {
  // shallowRef：事件载荷是后端直接 JSON 反序列化的普通对象，无需深度响应式，
  // 同时避免 ref<T[]> 对泛型元素的 UnwrapRef 改写导致类型不匹配。
  const items = shallowRef<T[]>([])
  const active = ref(false)

  let unsubscribe: (() => void) | null = null

  function start(): void {
    if (unsubscribe) return
    unsubscribe = wails.events.on<T>(name, (payload) => {
      // 最新的排在最前，只保留 limit 条
      items.value = [payload, ...items.value].slice(0, limit)
    })
    active.value = true
  }

  function stop(): void {
    unsubscribe?.()
    unsubscribe = null
    active.value = false
  }

  function clear(): void {
    items.value = []
  }

  // 组件卸载时兜底清理，防止泄漏
  onUnmounted(stop)

  return { name, items, active, start, stop, clear }
}
