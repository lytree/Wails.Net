import { defineComponent, ref } from 'vue'

interface ServerInfoEntry {
  key: string
  value: string
}

export default defineComponent({
  name: 'SystemPanel',
  setup() {
    const serverInfo = ref<ServerInfoEntry[]>([])
    const clipboardInput = ref('Hello, Wails.Net!')
    const clipboardResult = ref('')
    const notificationInput = ref('这是一条来自 Vue JSX Vapor 前端的通知')
    const windowMessage = ref('')

    const onGetServerInfo = async () => {
      try {
        const info = await window.wails.call('GreetingService.GetServerInfo', [])
        if (info && typeof info === 'object') {
          serverInfo.value = Object.entries(info as Record<string, string>).map(
            ([key, value]) => ({ key, value: String(value) }),
          )
        } else {
          serverInfo.value = [{ key: '结果', value: String(info) }]
        }
      } catch (e) {
        console.error('获取服务器信息失败:', e)
        serverInfo.value = [{ key: '错误', value: String(e) }]
      }
    }

    const onCopy = async () => {
      const text = clipboardInput.value
      if (!text) return
      try {
        await window.wails.call('clipboard.setText', [text])
        clipboardResult.value = '已复制到剪贴板'
      } catch (e) {
        console.error('复制失败:', e)
        clipboardResult.value = '复制失败'
      }
    }

    const onPaste = async () => {
      try {
        const text = await window.wails.call('clipboard.getText', [])
        clipboardResult.value = `剪贴板内容: ${text}`
      } catch (e) {
        console.error('粘贴失败:', e)
        clipboardResult.value = '粘贴失败'
      }
    }

    const onNotify = async () => {
      const text = notificationInput.value
      if (!text) return
      try {
        await window.wails.call('notification.show', [
          { title: 'Wails.Net Vue Demo', body: text },
        ])
      } catch (e) {
        console.error('发送通知失败:', e)
      }
    }

    const onSetTitle = async () => {
      try {
        await window.wails.window.setTitle('新标题 - 来自 Vue JSX Vapor')
        windowMessage.value = '窗口标题已更新'
      } catch (e) {
        console.error('设置窗口标题失败:', e)
        windowMessage.value = '设置窗口标题失败'
      }
    }

    const onMinimize = async () => {
      try {
        await window.wails.window.minimize()
        windowMessage.value = '窗口已最小化'
      } catch (e) {
        console.error('最小化窗口失败:', e)
        windowMessage.value = '最小化窗口失败'
      }
    }

    const onMaximize = async () => {
      try {
        await window.wails.window.maximize()
        windowMessage.value = '窗口已最大化'
      } catch (e) {
        console.error('最大化窗口失败:', e)
        windowMessage.value = '最大化窗口失败'
      }
    }

    const onCentre = async () => {
      try {
        await window.wails.window.centre()
        windowMessage.value = '窗口已居中'
      } catch (e) {
        console.error('居中窗口失败:', e)
        windowMessage.value = '居中窗口失败'
      }
    }

    return () => (
      <div class="panel">
        <h2>系统信息</h2>
        <p class="description">演示服务器信息、剪贴板、通知与窗口操作的绑定调用</p>

        <h3>服务器信息</h3>
        <div class="form-group">
          <button class="btn" onClick={onGetServerInfo}>
            获取服务器信息
          </button>
        </div>
        {serverInfo.value.length > 0 && (
          <div class="result">
            {serverInfo.value.map((entry) => (
              <div class="info-row" key={entry.key}>
                <strong>{entry.key}:</strong> {entry.value}
              </div>
            ))}
          </div>
        )}

        <h3>剪贴板</h3>
        <div class="form-group">
          <input
            type="text"
            value={clipboardInput.value}
            onInput={(e) =>
              (clipboardInput.value = (e.target as HTMLInputElement).value)
            }
          />
          <button class="btn" onClick={onCopy}>
            复制
          </button>
          <button class="btn" onClick={onPaste}>
            粘贴
          </button>
        </div>
        {clipboardResult.value && <div class="result">{clipboardResult.value}</div>}

        <h3>系统通知</h3>
        <div class="form-group">
          <input
            type="text"
            value={notificationInput.value}
            onInput={(e) =>
              (notificationInput.value = (e.target as HTMLInputElement).value)
            }
          />
          <button class="btn" onClick={onNotify}>
            发送通知
          </button>
        </div>

        <h3>窗口操作</h3>
        <div class="form-group">
          <button class="btn" onClick={onSetTitle}>
            设置标题
          </button>
          <button class="btn" onClick={onMinimize}>
            最小化
          </button>
          <button class="btn" onClick={onMaximize}>
            最大化
          </button>
          <button class="btn" onClick={onCentre}>
            居中
          </button>
        </div>
        {windowMessage.value && <div class="result">{windowMessage.value}</div>}
      </div>
    )
  },
})
