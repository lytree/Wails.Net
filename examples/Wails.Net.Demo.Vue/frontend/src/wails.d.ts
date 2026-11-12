interface WailsAPI {
  call(name: string, args?: any[]): Promise<any>
  bindings: {
    call(bindingId: number, args?: any[]): Promise<any>
  }
  // 注意：运行时自 M3 起不再挂载 window / clipboard / dialog 等命名空间，
  // 这些能力统一通过 call('<命名空间>.<动作>', [选项对象]) 调用，例如：
  //   await window.wails.call('window.setTitle', [{ title: '新标题' }])
  // 也可改用类型化封装：import { window } from '@wails-net/plugin-window'
  events: {
    on(eventName: string, callback: (data: any) => void): () => void
    off(eventName: string): void
    emit(eventName: string, data?: any): void
  }
}

declare global {
  interface Window {
    wails: WailsAPI
  }
}

export {}
