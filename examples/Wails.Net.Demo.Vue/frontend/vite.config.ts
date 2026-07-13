import { defineConfig } from 'vite'
import vueJsxVapor from 'vue-jsx-vapor/vite'

export default defineConfig({
  base: './',
  plugins: [
    vueJsxVapor({
      interop: true,
    }),
  ],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
