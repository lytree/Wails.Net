import { useEffect, useState } from 'react'

export default function Counter() {
  const [value, setValue] = useState(0)
  const [error, setError] = useState('')

  const refreshValue = async () => {
    try {
      const result = await window.wails.call('counter.getValue', [])
      setValue(Number(result) || 0)
      setError('')
    } catch (e) {
      console.error(e)
      setError('获取值失败：' + String(e))
    }
  }

  useEffect(() => {
    refreshValue()
  }, [])

  const onIncrement = async () => {
    try {
      const result = await window.wails.call('counter.increment', [])
      setValue(Number(result) || 0)
      setError('')
    } catch (e) {
      console.error(e)
      setError('增加失败：' + String(e))
    }
  }

  const onDecrement = async () => {
    try {
      const result = await window.wails.call('counter.decrement', [])
      setValue(Number(result) || 0)
      setError('')
    } catch (e) {
      console.error(e)
      setError('减少失败：' + String(e))
    }
  }

  const onReset = async () => {
    try {
      const result = await window.wails.call('counter.reset', [])
      setValue(Number(result) || 0)
      setError('')
    } catch (e) {
      console.error(e)
      setError('重置失败：' + String(e))
    }
  }

  return (
    <div className="panel">
      <h2>计数器插件</h2>
      <p className="description">演示 Wails.Net 插件系统的基本用法</p>

      <div className="counter-display">
        <span>{value}</span>
      </div>

      <div className="counter-buttons">
        <button className="btn" onClick={onDecrement}>减少 -</button>
        <button className="btn" onClick={onReset}>重置</button>
        <button className="btn" onClick={onIncrement}>增加 +</button>
      </div>

      <div className="counter-buttons" style={{ marginTop: 16 }}>
        <button className="btn btn-small" onClick={refreshValue}>刷新值</button>
      </div>

      {error && <div className="result">{error}</div>}
    </div>
  )
}
