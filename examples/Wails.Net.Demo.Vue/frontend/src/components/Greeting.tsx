import { defineComponent, ref } from 'vue'

export default defineComponent({
  name: 'Greeting',
  setup() {
    const name = ref('世界')
    const greetResult = ref('')
    const timeResult = ref('')
    const numA = ref<number>(6)
    const numB = ref<number>(8)
    const addResult = ref('')

    const onGreet = async () => {
      try {
        const result = await window.wails.call('GreetingService.Greet', [name.value])
        greetResult.value = String(result)
      } catch (e) {
        console.error('调用 GreetingService.Greet 失败:', e)
      }
    }

    const onGetTime = async () => {
      try {
        const result = await window.wails.call('GreetingService.GetCurrentTimeAsync', [])
        timeResult.value = `当前时间: ${result}`
      } catch (e) {
        console.error('调用 GreetingService.GetCurrentTimeAsync 失败:', e)
      }
    }

    const onAdd = async () => {
      try {
        const result = await window.wails.call('GreetingService.Add', [numA.value, numB.value])
        addResult.value = `结果: ${numA.value} + ${numB.value} = ${result}`
      } catch (e) {
        console.error('调用 GreetingService.Add 失败:', e)
      }
    }

    return () => (
      <div class="panel">
        <h2>问候服务</h2>
        <p class="description">演示 C# 后端方法绑定到前端调用（Vue JSX Vapor）</p>

        <div class="form-group">
          <label>输入你的名字：</label>
          <input
            type="text"
            value={name.value}
            onInput={(e) => (name.value = (e.target as HTMLInputElement).value)}
          />
          <button class="btn" onClick={onGreet}>
            打招呼
          </button>
        </div>
        {greetResult.value && <div class="result">{greetResult.value}</div>}

        <h3>获取当前时间</h3>
        <div class="form-group">
          <button class="btn" onClick={onGetTime}>
            获取时间
          </button>
        </div>
        {timeResult.value && <div class="result">{timeResult.value}</div>}

        <h3>加法计算</h3>
        <div class="form-group">
          <label>数字 A：</label>
          <input
            type="number"
            value={numA.value}
            onInput={(e) => (numA.value = parseInt((e.target as HTMLInputElement).value) || 0)}
          />
          <label>数字 B：</label>
          <input
            type="number"
            value={numB.value}
            onInput={(e) => (numB.value = parseInt((e.target as HTMLInputElement).value) || 0)}
          />
          <button class="btn" onClick={onAdd}>
            计算
          </button>
        </div>
        {addResult.value && <div class="result">{addResult.value}</div>}
      </div>
    )
  },
})
