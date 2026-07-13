import { defineComponent, onMounted, ref } from 'vue'

export default defineComponent({
  name: 'Counter',
  setup() {
    const value = ref<number>(0)
    const message = ref('')

    const refresh = async () => {
      try {
        const v = await window.wails.call('counter.getValue', [])
        value.value = Number(v) || 0
      } catch (e) {
        console.error('获取计数器值失败:', e)
        value.value = 0
      }
    }

    const onIncrement = async () => {
      try {
        const result = await window.wails.call('counter.increment', [])
        value.value = Number(result) || 0
        message.value = ''
      } catch (e) {
        console.error('增加失败:', e)
        message.value = '调用失败，插件可能未加载'
      }
    }

    const onDecrement = async () => {
      try {
        const result = await window.wails.call('counter.decrement', [])
        value.value = Number(result) || 0
        message.value = ''
      } catch (e) {
        console.error('减少失败:', e)
        message.value = '调用失败，插件可能未加载'
      }
    }

    const onReset = async () => {
      try {
        await window.wails.call('counter.reset', [])
        value.value = 0
        message.value = ''
      } catch (e) {
        console.error('重置失败:', e)
        message.value = '调用失败，插件可能未加载'
      }
    }

    onMounted(() => {
      void refresh()
    })

    return () => (
      <div class="panel">
        <h2>计数器插件</h2>
        <p class="description">演示自定义插件注册的命令调用（MyCustomPlugin / counter.*）</p>

        <div class="counter-display">
          <span>{value.value}</span>
        </div>

        <div class="counter-buttons">
          <button class="btn" onClick={onIncrement}>
            增加 (+1)
          </button>
          <button class="btn" onClick={onDecrement}>
            减少 (-1)
          </button>
          <button class="btn btn-danger" onClick={onReset}>
            重置
          </button>
        </div>

        {message.value && <div class="result">{message.value}</div>}
      </div>
    )
  },
})
