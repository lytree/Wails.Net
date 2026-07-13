/// <reference types="vite/client" />

interface WailsAPI {
  call(name: string, args?: any[]): Promise<any>
  bindings: {
    call(bindingId: number, args?: any[]): Promise<any>
  }
  window: {
    setTitle(title: string): Promise<void>
    minimize(): Promise<void>
    maximize(): Promise<void>
    close(): Promise<void>
    centre(): Promise<void>
    setAlwaysOnTop(onTop: boolean): Promise<void>
    openDevTools(): Promise<void>
  }
  events: {
    on(eventName: string, callback: (data: any) => void): () => void
    emit(eventName: string, data?: any): Promise<void>
  }
}

declare global {
  interface Window {
    wails: WailsAPI
  }
}

export {}
