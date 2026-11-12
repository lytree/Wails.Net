/// <reference types="vite/client" />

// 让 tsc / IDE 识别 .vue 单文件组件（vue-tsc 会走原生解析，此处为兜底声明）
declare module '*.vue' {
  import type { DefineComponent } from 'vue'

  const component: DefineComponent
  export default component
}
