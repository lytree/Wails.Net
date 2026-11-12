/** 待办事项（对应后端 TodoItem） */
export interface TodoItem {
  id: number
  title: string
  completed: boolean
  createdAt: string
}

/** 待办统计（对应后端 TodoService.GetStats） */
export interface TodoStats {
  total: number
  completed: number
  pending: number
}

/** 后端心跳事件载荷（vuesfc:tick） */
export interface TickPayload {
  seq: number
  time: string
}

/** 后端回显事件载荷（vuesfc:pong） */
export interface PongPayload {
  echo: unknown
  receivedAt: string
}

/** 服务器信息键值对 */
export interface InfoEntry {
  key: string
  value: string
}
