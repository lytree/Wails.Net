<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useBackendCall } from '../composables/useBackend'
import ResultBanner from './ResultBanner.vue'

const { loading, error, call } = useBackendCall()

const value = ref(0)
const hint = ref('')

const display = computed(() => (value.value > 0 ? `+${value.value}` : String(value.value)))

async function refresh(): Promise<void> {
  const current = await call<number>('counter.getValue', [])
  if (current !== null) value.value = current
}

async function increment(): Promise<void> {
  const next = await call<number>('counter.increment', [])
  if (next !== null) value.value = next
}

async function decrement(): Promise<void> {
  const next = await call<number>('counter.decrement', [])
  if (next !== null) value.value = next
}

async function reset(): Promise<void> {
  await call<void>('counter.reset', [])
  value.value = 0
  hint.value = '计数器已重置'
}

onMounted(() => {
  void refresh()
})
</script>

<template>
  <section class="panel">
    <h2>计数器插件</h2>
    <p class="description">
      演示自定义插件注册的命令（MyCustomPlugin → <code>counter.*</code>），与绑定方法同样经源生成器派发
    </p>

    <div class="counter-display">
      <span>{{ display }}</span>
    </div>

    <div class="counter-buttons">
      <button class="btn" :disabled="loading" @click="increment">增加 (+1)</button>
      <button class="btn" :disabled="loading" @click="decrement">减少 (-1)</button>
      <button class="btn btn-danger" :disabled="loading" @click="reset">重置</button>
    </div>

    <ResultBanner :text="hint" />
    <ResultBanner :text="error" kind="error" />
  </section>
</template>
