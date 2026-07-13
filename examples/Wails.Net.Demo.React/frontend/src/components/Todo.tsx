import { useEffect, useState } from 'react'

interface TodoItem {
  id: number
  title: string
  completed: boolean
  createdAt: string
}

interface TodoStats {
  total: number
  completed: number
  pending: number
}

export default function Todo() {
  const [todos, setTodos] = useState<TodoItem[]>([])
  const [stats, setStats] = useState<TodoStats>({ total: 0, completed: 0, pending: 0 })
  const [newTitle, setNewTitle] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const refreshAll = async () => {
    try {
      const [all, st] = await Promise.all([
        window.wails.call('TodoService.GetAll', []),
        window.wails.call('TodoService.GetStats', []),
      ])
      setTodos(Array.isArray(all) ? (all as TodoItem[]) : [])
      setStats((st as TodoStats) ?? { total: 0, completed: 0, pending: 0 })
      setError('')
    } catch (e) {
      console.error(e)
      setError('加载失败：' + String(e))
    }
  }

  useEffect(() => {
    refreshAll()
  }, [])

  const onAdd = async () => {
    const title = newTitle.trim()
    if (!title) return
    setLoading(true)
    try {
      await window.wails.call('TodoService.Add', [title])
      setNewTitle('')
      await refreshAll()
    } catch (e) {
      console.error(e)
      setError('添加失败：' + String(e))
    } finally {
      setLoading(false)
    }
  }

  const onToggle = async (id: number) => {
    try {
      await window.wails.call('TodoService.Toggle', [id])
      await refreshAll()
    } catch (e) {
      console.error(e)
      setError('切换状态失败：' + String(e))
    }
  }

  const onDelete = async (id: number) => {
    try {
      await window.wails.call('TodoService.Delete', [id])
      await refreshAll()
    } catch (e) {
      console.error(e)
      setError('删除失败：' + String(e))
    }
  }

  const onClearCompleted = async () => {
    try {
      await window.wails.call('TodoService.ClearCompleted', [])
      await refreshAll()
    } catch (e) {
      console.error(e)
      setError('清除已完成失败：' + String(e))
    }
  }

  return (
    <div className="panel">
      <h2>待办事项</h2>
      <p className="description">演示增删改查与统计信息</p>

      <div className="todo-stats">
        <span>总计：<strong>{stats.total}</strong></span>
        <span>已完成：<strong>{stats.completed}</strong></span>
        <span>待办：<strong>{stats.pending}</strong></span>
        <button className="btn btn-small" onClick={onClearCompleted}>清除已完成</button>
      </div>

      <div className="form-group">
        <input
          type="text"
          value={newTitle}
          onChange={(e) => setNewTitle(e.target.value)}
          placeholder="输入新的待办事项..."
          onKeyDown={(e) => {
            if (e.key === 'Enter') onAdd()
          }}
        />
        <button className="btn" onClick={onAdd} disabled={loading}>
          {loading ? '添加中...' : '添加'}
        </button>
      </div>

      {error && <div className="result">{error}</div>}

      <ul className="todo-list">
        {todos.map((todo) => (
          <li
            key={todo.id}
            className={`todo-item ${todo.completed ? 'completed' : ''}`}
          >
            <input
              type="checkbox"
              checked={todo.completed}
              onChange={() => onToggle(todo.id)}
            />
            <span className="todo-title">{todo.title}</span>
            <span className="todo-date">{todo.createdAt}</span>
            <button
              className="btn btn-small btn-danger"
              onClick={() => onDelete(todo.id)}
            >
              删除
            </button>
          </li>
        ))}
      </ul>

      {todos.length === 0 && !error && (
        <div className="result">暂无待办事项</div>
      )}
    </div>
  )
}
