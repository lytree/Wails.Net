<script setup lang="ts">
import { ref } from 'vue'
import { useBackendCall } from '../composables/useBackend'
import ResultBanner from './ResultBanner.vue'
import type { InfoEntry } from '../types'

const { loading, error, call } = useBackendCall()

const serverInfo = ref<InfoEntry[]>([])
const clipboardInput = ref('Hello, Wails.Net!')
const clipboardResult = ref('')
const notificationText = ref('这是一条来自 Vue 3 SFC 前端的通知')
const dialogResult = ref('')
const windowMessage = ref('')

async function loadServerInfo(): Promise<void> {
  const info = await call<Record<string, string>>('GreetingService.GetServerInfo', [])
  if (info && typeof info === 'object') {
    serverInfo.value = Object.entries(info).map(([key, value]) => ({ key, value: String(value) }))
  }
}

async function copyText(): Promise<void> {
  if (!clipboardInput.value) return
  const ok = await call<void>('clipboard.setText', [clipboardInput.value])
  clipboardResult.value = ok === null ? '复制失败' : '已复制到剪贴板'
}

async function pasteText(): Promise<void> {
  const text = await call<string>('clipboard.getText', [])
  if (text !== null) clipboardResult.value = `剪贴板内容：${text}`
}

async function notify(): Promise<void> {
  if (!notificationText.value) return
  const ok = await call<void>('notification.show', [
    { title: 'Wails.Net Vue SFC Demo', body: notificationText.value },
  ])
  if (ok !== null) windowMessage.value = '通知已发送'
}

async function showMessageDialog(): Promise<void> {
  const index = await call<number>('dialog.message', ['Wails.Net', '来自 Vue 3 SFC 组件的消息对话框'])
  if (index !== null) dialogResult.value = `对话框返回索引：${index}`
}

async function pickFile(): Promise<void> {
  const path = await call<string | null>('dialog.openFile', ['选择一个文件', null, null])
  dialogResult.value = path ? `已选择：${path}` : '已取消选择'
}

async function setTitle(): Promise<void> {
  // 窗口控制走 window.* 命令（对应 @wails-net/plugin-window 的类型化封装）
  const ok = await call<void>('window.setTitle', [{ title: '新标题 - 来自 Vue 3 SFC' }])
  if (ok !== null) windowMessage.value = '窗口标题已更新'
}

async function minimize(): Promise<void> {
  const ok = await call<void>('window.minimize', [])
  if (ok !== null) windowMessage.value = '窗口已最小化'
}

async function maximize(): Promise<void> {
  const ok = await call<void>('window.maximize', [])
  if (ok !== null) windowMessage.value = '窗口已最大化'
}

async function centre(): Promise<void> {
  const ok = await call<void>('window.centre', [])
  if (ok !== null) windowMessage.value = '窗口已居中'
}
</script>

<template>
  <section class="panel">
    <h2>系统能力</h2>
    <p class="description">演示内置插件命令：clipboard / notification / dialog / window，以及绑定方法返回的字典</p>

    <h3>服务器信息</h3>
    <div class="form-group">
      <button class="btn" :disabled="loading" @click="loadServerInfo">获取服务器信息</button>
    </div>
    <div v-if="serverInfo.length" class="result">
      <div v-for="entry in serverInfo" :key="entry.key" class="info-row">
        <strong>{{ entry.key }}:</strong> {{ entry.value }}
      </div>
    </div>

    <h3>剪贴板</h3>
    <div class="form-group">
      <input v-model="clipboardInput" type="text" placeholder="输入要复制的内容" />
      <button class="btn" :disabled="loading" @click="copyText">复制</button>
      <button class="btn" :disabled="loading" @click="pasteText">粘贴</button>
    </div>
    <ResultBanner :text="clipboardResult" />

    <h3>系统通知</h3>
    <div class="form-group">
      <input v-model="notificationText" type="text" placeholder="通知内容" />
      <button class="btn" :disabled="loading" @click="notify">发送通知</button>
    </div>

    <h3>原生对话框</h3>
    <div class="form-group">
      <button class="btn" :disabled="loading" @click="showMessageDialog">消息对话框</button>
      <button class="btn" :disabled="loading" @click="pickFile">选择文件</button>
    </div>
    <ResultBanner :text="dialogResult" />

    <h3>窗口操作</h3>
    <div class="form-group">
      <button class="btn" :disabled="loading" @click="setTitle">设置标题</button>
      <button class="btn" :disabled="loading" @click="minimize">最小化</button>
      <button class="btn" :disabled="loading" @click="maximize">最大化</button>
      <button class="btn" :disabled="loading" @click="centre">居中</button>
    </div>
    <ResultBanner :text="windowMessage" />

    <ResultBanner :text="error" kind="error" />
  </section>
</template>
