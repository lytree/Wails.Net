<script setup lang="ts">
import { ref } from 'vue'
import { useBackendCall } from '../composables/useBackend'
import ResultBanner from './ResultBanner.vue'

const { loading, error, call } = useBackendCall()

const name = ref('世界')
const greeting = ref('')
const timeText = ref('')
const numA = ref(6)
const numB = ref(8)
const sumText = ref('')

/** 调用绑定方法 GreetingService.Greet */
async function onGreet(): Promise<void> {
  const result = await call<string>('GreetingService.Greet', [name.value])
  if (result !== null) greeting.value = result
}

/** 调用绑定方法 GreetingService.GetCurrentTimeAsync（后端异步方法，前端同样 await） */
async function onGetTime(): Promise<void> {
  const result = await call<string>('GreetingService.GetCurrentTimeAsync', [])
  if (result !== null) timeText.value = `当前时间：${result}`
}

/** 调用绑定方法 GreetingService.Add（多参数按顺序传入） */
async function onAdd(): Promise<void> {
  const result = await call<number>('GreetingService.Add', [numA.value, numB.value])
  if (result !== null) sumText.value = `${numA.value} + ${numB.value} = ${result}`
}
</script>

<template>
  <section class="panel">
    <h2>问候服务</h2>
    <p class="description">
      演示 Vue 3 <code>&lt;script setup&gt;</code> 组件调用 C# 绑定方法（GreetingService）
    </p>

    <h3>打招呼</h3>
    <div class="form-group">
      <label for="greet-name">你的名字</label>
      <input id="greet-name" v-model="name" type="text" placeholder="请输入名字" />
      <button class="btn" :disabled="loading" @click="onGreet">
        {{ loading ? '调用中…' : '打招呼' }}
      </button>
    </div>
    <ResultBanner :text="greeting" />

    <h3>异步方法</h3>
    <div class="form-group">
      <button class="btn" :disabled="loading" @click="onGetTime">获取当前时间</button>
    </div>
    <ResultBanner :text="timeText" />

    <h3>加法计算</h3>
    <div class="form-group">
      <label for="num-a">数字 A</label>
      <input id="num-a" v-model.number="numA" type="number" />
      <label for="num-b">数字 B</label>
      <input id="num-b" v-model.number="numB" type="number" />
      <button class="btn" :disabled="loading" @click="onAdd">计算</button>
    </div>
    <ResultBanner :text="sumText" />

    <ResultBanner :text="error" kind="error" />
  </section>
</template>
