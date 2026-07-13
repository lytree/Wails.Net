import { useEffect, useState } from 'react'

interface ServerInfo {
  [key: string]: string | number | boolean
}

export default function SystemPanel() {
  const [info, setInfo] = useState<ServerInfo | null>(null)
  const [error, setError] = useState('')
  const [clipText, setClipText] = useState('')
  const [notifyResult, setNotifyResult] = useState('')
  const [windowResult, setWindowResult] = useState('')

  const loadServerInfo = async () => {
    try {
      const result = await window.wails.call('GreetingService.GetServerInfo', [])
      setInfo(result as ServerInfo)
      setError('')
    } catch (e) {
      console.error(e)
      setError('获取服务器信息失败：' + String(e))
    }
  }

  useEffect(() => {
    loadServerInfo()
  }, [])

  const onCopy = async () => {
    try {
      const text = info ? JSON.stringify(info, null, 2) : ''
      await window.wails.call('clipboard.setText', [text])
      setClipText(text)
      setError('')
    } catch (e) {
      console.error(e)
      setError('复制到剪贴板失败：' + String(e))
    }
  }

  const onPaste = async () => {
    try {
      const text = await window.wails.call('clipboard.getText', [])
      setClipText(String(text))
      setError('')
    } catch (e) {
      console.error(e)
      setError('从剪贴板粘贴失败：' + String(e))
    }
  }

  const onNotify = async () => {
    try {
      const body = info ? `服务运行中，共 ${Object.keys(info).length} 项信息` : '服务器信息未加载'
      await window.wails.call('notification.show', [
        { title: 'Wails.Net React Demo', body },
      ])
      setNotifyResult('通知已发送')
      setError('')
    } catch (e) {
      console.error(e)
      setError('发送通知失败：' + String(e))
    }
  }

  const onSetTitle = async () => {
    try {
      await window.wails.window.setTitle('Wails.Net React Demo - 新标题')
      setWindowResult('窗口标题已更新')
      setError('')
    } catch (e) {
      console.error(e)
      setError('设置标题失败：' + String(e))
    }
  }

  const onMinimize = async () => {
    try {
      await window.wails.window.minimize()
      setWindowResult('窗口已最小化')
      setError('')
    } catch (e) {
      console.error(e)
      setError('最小化失败：' + String(e))
    }
  }

  const onMaximize = async () => {
    try {
      await window.wails.window.maximize()
      setWindowResult('窗口已最大化')
      setError('')
    } catch (e) {
      console.error(e)
      setError('最大化失败：' + String(e))
    }
  }

  const onCentre = async () => {
    try {
      await window.wails.window.centre()
      setWindowResult('窗口已居中')
      setError('')
    } catch (e) {
      console.error(e)
      setError('居中失败：' + String(e))
    }
  }

  const onAlwaysOnTop = async () => {
    try {
      await window.wails.window.setAlwaysOnTop(true)
      setWindowResult('窗口已置顶')
      setError('')
    } catch (e) {
      console.error(e)
      setError('置顶失败：' + String(e))
    }
  }

  const onOpenDevTools = async () => {
    try {
      await window.wails.window.openDevTools()
      setWindowResult('开发者工具已打开')
      setError('')
    } catch (e) {
      console.error(e)
      setError('打开开发者工具失败：' + String(e))
    }
  }

  return (
    <div className="panel">
      <h2>系统信息</h2>
      <p className="description">演示服务器信息、剪贴板、通知和窗口操作</p>

      <h3>服务器信息</h3>
      <div className="form-group">
        <button className="btn btn-small" onClick={loadServerInfo}>刷新信息</button>
        <button className="btn btn-small" onClick={onCopy}>复制到剪贴板</button>
        <button className="btn btn-small" onClick={onPaste}>从剪贴板粘贴</button>
      </div>
      {info && (
        <div className="result">
          {Object.entries(info).map(([key, val]) => (
            <div className="info-row" key={key}>
              <strong>{key}</strong>: {String(val)}
            </div>
          ))}
        </div>
      )}

      <h3>剪贴板内容</h3>
      <div className="result">{clipText || '(空)'}</div>

      <h3>通知</h3>
      <div className="form-group">
        <button className="btn" onClick={onNotify}>发送通知</button>
      </div>
      {notifyResult && <div className="result">{notifyResult}</div>}

      <h3>窗口操作</h3>
      <div className="form-group">
        <button className="btn btn-small" onClick={onSetTitle}>设置标题</button>
        <button className="btn btn-small" onClick={onMinimize}>最小化</button>
        <button className="btn btn-small" onClick={onMaximize}>最大化</button>
        <button className="btn btn-small" onClick={onCentre}>居中</button>
        <button className="btn btn-small" onClick={onAlwaysOnTop}>置顶</button>
        <button className="btn btn-small" onClick={onOpenDevTools}>开发者工具</button>
      </div>
      {windowResult && <div className="result">{windowResult}</div>}

      {error && <div className="result">{error}</div>}
    </div>
  )
}
