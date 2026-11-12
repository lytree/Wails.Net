import { useState } from 'react'
// 从独立插件包导入（workspace:* 解析，类型随包走）：包导出的是 updater 对象
import { updater, type UpdateCheckResult } from '@wails-net/plugin-updater'

interface LogEntry {
  time: string
  text: string
}

export default function UpdaterPanel() {
  // check 返回 { version, available }，checkAndDownload 额外带 path
  const [result, setResult] = useState<(UpdateCheckResult & { path?: string | null }) | null>(null)
  const [logs, setLogs] = useState<LogEntry[]>([])

  const appendLog = (text: string) => {
    setLogs((prev) => [...prev, { time: new Date().toLocaleTimeString(), text }])
  }

  const onCheck = async () => {
    appendLog('调用 updater.check（插件包封装）…')
    const res = await updater.check()
    setResult(res)
    appendLog(`check 结果: ${JSON.stringify(res)}`)
  }

  const onCheckAndDownload = async () => {
    appendLog('调用 updater.checkAndDownload …')
    const res = await updater.checkAndDownload()
    setResult(res)
    appendLog(`checkAndDownload 结果: ${JSON.stringify(res)}`)
  }

  const onDownload = async () => {
    appendLog('调用 updater.download …')
    const path = await updater.download()
    appendLog(`download 路径: ${path || '(空)'}`)
  }

  const onInstall = async () => {
    appendLog('调用 updater.install（无本地路径，演示调用链）…')
    await updater.install('')
    appendLog('install 已返回')
  }

  return (
    <div className="panel">
      <h2>更新检查插件（@wails-net/plugin-updater）</h2>
      <p className="hint">演示从独立 npm 插件包（@wails-net/plugin-updater）导入类型化封装</p>

      <div className="row">
        <button onClick={onCheck}>检查更新</button>
        <button onClick={onCheckAndDownload}>检查并下载</button>
        <button onClick={onDownload}>下载</button>
        <button onClick={onInstall}>安装</button>
      </div>

      {result && (
        <div className="result">
          <div>版本: <b>{result.version || '(未知)'}</b></div>
          <div>可用: <b>{result.available ? '是' : '否'}</b></div>
          {result.path && <div>路径: {result.path}</div>}
        </div>
      )}

      <div className="log">
        {logs.map((entry, i) => (
          <div key={i} className="log-line">
            <span className="log-time">[{entry.time}]</span> {entry.text}
          </div>
        ))}
      </div>
    </div>
  )
}
