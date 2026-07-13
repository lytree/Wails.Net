import { defineComponent, onMounted, ref } from 'vue'

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

export default defineComponent({
  name: 'Todo',
  setup() {
    const newTitle = ref('')
    const todos = ref<TodoItem[]>([])
    const stats = ref<TodoStats>({ total: 0, completed: 0, pending: 0 })

    const refresh = async () => {
      try {
        const all = await window.wails.call('TodoService.GetAll', [])
        todos.value = Array.isArray(all) ? (all as TodoItem[]) : []
        const s = await window.wails.call('TodoService.GetStats', [])
        stats.value = {
          total: s?.total ?? 0,
          completed: s?.completed ?? 0,
          pending: s?.pending ?? 0,
        }
      } catch (e) {
        console.error('获取待办事项失败:', e)
      }
    }

    const onAdd = async () => {
      const title = newTitle.value.trim()
      if (!title) return
      try {
        await window.wails.call('TodoService.Add', [title])
        newTitle.value = ''
        await refresh()
      } catch (e) {
        console.error('添加待办失败:', e)
      }
    }

    const onToggle = async (id: number) => {
      try {
        await window.wails.call('TodoService.Toggle', [id])
        await refresh()
      } catch (e) {
        console.error('切换待办失败:', e)
      }
    }

    const onDelete = async (id: number) => {
      try {
        await window.wails.call('TodoService.Delete', [id])
        await refresh()
      } catch (e) {
        console.error('删除待办失败:', e)
      }
    }

    const onClearCompleted = async () => {
      try {
        await window.wails.call('TodoService.ClearCompleted', [])
        await refresh()
      } catch (e) {
        console.error('清除已完成失败:', e)
      }
    }

    const formatDate = (iso: string): string => {
      try {
        return new Date(iso).toLocaleString()
      } catch {
        return iso
      }
    }

    onMounted(() => {
      void refresh()
    })

    return () => (
      <div class="panel">
        <h2>待办事项</h2>
        <p class="description">演示 CRUD 操作的绑定（TodoService）</p>

        <div class="todo-stats">
          <span>总数: <strong>{stats.value.total}</strong></span>
          <span>已完成: <strong>{stats.value.completed}</strong></span>
          <span>未完成: <strong>{stats.value.pending}</strong></span>
          <button class="btn btn-small" onClick={onClearCompleted}>
            清除已完成
          </button>
          <button class="btn btn-small" onClick={() => void refresh()}>
            刷新
          </button>
        </div>

        <div class="form-group">
          <input
            type="text"
            placeholder="输入待办事项标题..."
            value={newTitle.value}
            onInput={(e) => (newTitle.value = (e.target as HTMLInputElement).value)}
            onKeydown={(e) => {
              if (e.key === 'Enter') void onAdd()
            }}
          />
          <button class="btn" onClick={() => void onAdd()}>
            添加
          </button>
        </div>

        <ul class="todo-list">
          {todos.value.length === 0 && (
            <li class="todo-item">
              <span class="todo-title">暂无待办事项</span>
            </li>
          )}
          {todos.value.map((todo) => (
            <li
              key={todo.id}
              class={'todo-item' + (todo.completed ? ' completed' : '')}
            >
              <input
                type="checkbox"
                checked={todo.completed}
                onChange={() => void onToggle(todo.id)}
              />
              <span class="todo-title">{todo.title}</span>
              <span class="todo-date">{formatDate(todo.createdAt)}</span>
              <button
                class="btn btn-small btn-danger"
                onClick={() => void onDelete(todo.id)}
              >
                删除
              </button>
            </li>
          ))}
        </ul>
      </div>
    )
  },
})
