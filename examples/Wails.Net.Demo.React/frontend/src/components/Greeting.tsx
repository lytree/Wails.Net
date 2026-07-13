import { useState } from 'react'

export default function Greeting() {
  const [name, setName] = useState('世界')
  const [greetResult, setGreetResult] = useState('')
  const [timeResult, setTimeResult] = useState('')
  const [numA, setNumA] = useState(10)
  const [numB, setNumB] = useState(20)
  const [addResult, setAddResult] = useState('')

  const onGreet = async () => {
    try {
      const result = await window.wails.call('GreetingService.Greet', [name])
      setGreetResult(String(result))
    } catch (e) {
      console.error(e)
      setGreetResult('调用失败：' + String(e))
    }
  }

  const onGetTime = async () => {
    try {
      const result = await window.wails.call('GreetingService.GetCurrentTimeAsync', [])
      setTimeResult(String(result))
    } catch (e) {
      console.error(e)
      setTimeResult('调用失败：' + String(e))
    }
  }

  const onAdd = async () => {
    try {
      const result = await window.wails.call('GreetingService.Add', [numA, numB])
      setAddResult(String(result))
    } catch (e) {
      console.error(e)
      setAddResult('调用失败：' + String(e))
    }
  }

  return (
    <div className="panel">
      <h2>问候服务</h2>
      <p className="description">演示 C# 后端方法绑定到前端调用（React TSX）</p>

      <h3>打招呼</h3>
      <div className="form-group">
        <label>名字：</label>
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="请输入名字"
        />
        <button className="btn" onClick={onGreet}>打招呼</button>
      </div>
      {greetResult && <div className="result">{greetResult}</div>}

      <h3>获取当前时间</h3>
      <div className="form-group">
        <button className="btn" onClick={onGetTime}>获取时间</button>
      </div>
      {timeResult && <div className="result">{timeResult}</div>}

      <h3>加法计算</h3>
      <div className="form-group">
        <label>数字 A：</label>
        <input
          type="number"
          value={numA}
          onChange={(e) => setNumA(Number(e.target.value))}
        />
        <label>数字 B：</label>
        <input
          type="number"
          value={numB}
          onChange={(e) => setNumB(Number(e.target.value))}
        />
        <button className="btn" onClick={onAdd}>计算 A+B</button>
      </div>
      {addResult && <div className="result">{addResult}</div>}
    </div>
  )
}
