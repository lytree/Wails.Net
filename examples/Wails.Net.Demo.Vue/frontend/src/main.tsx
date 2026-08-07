import { createApp } from 'vue'
import App from './App'
import { wails } from '@wails-net/runtime'
import './styles.css'

// 挂载全局 wails：组件通过 window.wails.* 访问后端（与 wails.d.ts 全局类型声明一致）
window.wails = wails

createApp(App).mount('#app')
