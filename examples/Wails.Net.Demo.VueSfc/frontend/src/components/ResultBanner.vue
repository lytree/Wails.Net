<script setup lang="ts">
/**
 * 结果提示条：演示 props（类型化 defineProps）与条件渲染。
 */
withDefaults(
  defineProps<{
    text?: string | null
    kind?: 'info' | 'error'
  }>(),
  { text: '', kind: 'info' },
)
</script>

<template>
  <div v-if="text" class="result" :class="{ 'result-error': kind === 'error' }">
    {{ text }}
  </div>
</template>
