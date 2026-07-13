import { useState } from 'react'
import Greeting from './components/Greeting'
import Todo from './components/Todo'
import Counter from './components/Counter'
import SystemPanel from './components/SystemPanel'

type TabKey = 'greeting' | 'todo' | 'counter' | 'system'

interface TabItem {
  key: TabKey
  label: string
}

const tabs: TabItem[] = [
  { key: 'greeting', label: '问候服务' },
  { key: 'todo', label: '待办事项' },
  { key: 'counter', label: '计数器插件' },
  { key: 'system', label: '系统信息' },
]

export default function App() {
  const [activeTab, setActiveTab] = useState<TabKey>('greeting')

  return (
    <div className="app">
      <header className="header">
        <h1>Wails.Net React Demo</h1>
        <p className="subtitle">.NET 10 + React 18 + Vite 6 + TypeScript TSX</p>
      </header>

      <nav className="tabs">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            className={`tab ${activeTab === tab.key ? 'active' : ''}`}
            onClick={() => setActiveTab(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      {activeTab === 'greeting' && <Greeting />}
      {activeTab === 'todo' && <Todo />}
      {activeTab === 'counter' && <Counter />}
      {activeTab === 'system' && <SystemPanel />}
    </div>
  )
}
