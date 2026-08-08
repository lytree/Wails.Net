import { useState } from 'react'
// M2 联调：从独立插件包导入（workspace:* 解析，类型随包走）
import {
  check,
  checkAndDownload,
  download,
  install,
  type UpdateCheckResult,
} from '@wails-net/plugin-updater'

interface LogEntry {
  time: string
  text: string
}

export default function UpdaterPanel() {
  const [result, setResult] = useState<UpdateCheckResult | null>(null)
  const [logs, setLogs] = useState<LogEntry[]>([])

  const appendLog = (text: string) => {
    setLogs((prev) => [...prev, { time: new Date().toLocaleTimeString(), text }])
  }

  const onCheck = async () => {
    appendLog('调用 updater.check（插件包封装）…')
    const res = await check() // 返回 { version, available, path }
    setResult(res)
    appendLog(`check 结果: ${JSON.stringify(res)}`)
  }

  const onCheckAndDownload = async () => {
    appendLog('调用 updater.checkAndDownload …')
    const res = await checkAndDownload()
    setResult(res)
    appendLog(`checkAndDownload 结果: ${JSON.stringify(res)}`)
  }

  const onDownload = async () => {
    appendLog('调用 updater.download …')
    const path = await download()
    appendLog(`download 路径: ${path || '(空)'}`)
  }

  const onInstall = async () => {
    appendLog('调用 updater.install（无本地路径，演示调用链）…')
    await install('')
    appendLog('install 已返回')
  }

  return (
    <div className="panel">
      <h2>更新检查插件（@wails-net/plugin-updater）</h2>
      <p className="hint">演示 M2 联调：从独立 npm 插件包 import 类型化封装</p>

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
