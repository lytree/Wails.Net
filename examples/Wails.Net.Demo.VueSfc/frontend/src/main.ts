import { createApp } from 'vue'
import App from './App.vue'
import './styles.css'

// 标准 Vue 3 SFC 入口：组件内通过 `@wails-net/runtime` 的 wails 对象与后端通信，
// 不再依赖 window 全局变量，便于类型推导与单元测试。
createApp(App).mount('#app')
