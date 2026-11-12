<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { wails } from '@wails-net/runtime'
import { useEventStream } from '../composables/useEventStream'
import ResultBanner from './ResultBanner.vue'
import type { PongPayload, TickPayload } from '../types'

// 后端每 2 秒广播的心跳事件
const {
  items: ticks,
  active: tickActive,
  start: startTick,
  stop: stopTick,
  clear: clearTicks,
} = useEventStream<TickPayload>('vuesfc:tick', 6)

// 后端对前端 ping 的回显事件
const { items: echoes } = useEventStream<PongPayload>('vuesfc:pong', 4)

const pingText = ref('ping from Vue 3 SFC')
const sentText = ref('')

/** 前端 → 后端：emit 事件，后端收到后回广播 vuesfc:pong */
function sendPing(): void {
  wails.events.emit('vuesfc:ping', {
    message: pingText.value,
    at: new Date().toLocaleTimeString(),
  })
  sentText.value = '已发送 vuesfc:ping，等待后端回显…'
}

function toggleTick(): void {
  if (tickActive.value) {
    stopTick()
  } else {
    startTick()
  }
}

function formatEcho(echo: unknown): string {
  if (echo && typeof echo === 'object' && 'message' in echo) {
    return String((echo as { message: unknown }).message)
  }
  return typeof echo === 'string' ? echo : JSON.stringify(echo)
}

onMounted(() => {
  startTick()
})
</script>

<template>
  <section class="panel">
    <h2>事件通信</h2>
    <p class="description">
      演示双向事件：后端 <code>app.Events.Emit</code> 广播 → 前端
      <code>wails.events.on</code> 订阅；前端 <code>wails.events.emit</code> → 后端回显
    </p>

    <div class="form-group">
      <button class="btn btn-small" @click="toggleTick">
        {{ tickActive ? '暂停心跳' : '恢复心跳' }}
      </button>
      <button class="btn btn-small" @click="clearTicks">清空心跳记录</button>
      <span class="badge">{{ tickActive ? '已订阅 vuesfc:tick' : '未订阅' }}</span>
    </div>

    <ul class="event-log">
      <li v-if="ticks.length === 0">暂无心跳事件（后端每 2 秒广播一次）</li>
      <li v-for="item in ticks" :key="item.seq">
        <span class="badge">#{{ item.seq }}</span> {{ item.time }}
      </li>
    </ul>

    <h3>发送事件到后端</h3>
    <div class="form-group">
      <input v-model="pingText" type="text" placeholder="要发送的内容" />
      <button class="btn" @click="sendPing">发送 ping</button>
    </div>
    <ResultBanner :text="sentText" />

    <ul class="event-log">
      <li v-if="echoes.length === 0">暂无回显事件</li>
      <li v-for="(item, index) in echoes" :key="`${item.receivedAt}-${index}`">
        <span class="badge">{{ item.receivedAt }}</span> {{ formatEcho(item.echo) }}
      </li>
    </ul>
  </section>
</template>
