import { ref } from 'vue'
import { wails } from '@wails-net/runtime'

/** 把任意抛出物归一化为可读文本（后端 CallError 也带 message）。 */
export function describeError(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === 'string') return error
  try {
    return JSON.stringify(error)
  } catch {
    return String(error)
  }
}

/**
 * 后端调用组合式函数：统一处理 loading 与错误，避免每个组件重复 try/catch。
 * 调用失败时返回 null 并由 `error` 暴露原因（示例里直接渲染到面板）。
 */
export function useBackendCall() {
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function call<T>(name: string, args: unknown[] = []): Promise<T | null> {
    loading.value = true
    error.value = null
    try {
      return await wails.call<T>(name, args)
    } catch (e) {
      error.value = describeError(e)
      console.error(`[wails] ${name} 调用失败:`, e)
      return null
    } finally {
      loading.value = false
    }
  }

  function reset(): void {
    error.value = null
  }

  return { loading, error, call, reset }
}
