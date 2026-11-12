import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 标准 Vue 3 SFC 工程：@vitejs/plugin-vue 负责编译 .vue 单文件组件。
// base 使用相对路径，便于 WebView 以自定义协议（而非 http 根路径）加载 dist 资源。
export default defineConfig({
  base: './',
  plugins: [vue()],
  server: {
    port: 5174,
    strictPort: true,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
