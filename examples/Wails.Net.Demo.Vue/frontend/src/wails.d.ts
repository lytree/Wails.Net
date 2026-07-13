interface WailsAPI {
  call(name: string, args?: any[]): Promise<any>
  bindings: {
    call(bindingId: number, args?: any[]): Promise<any>
  }
  window: {
    setTitle(title: string): Promise<void>
    minimize(): Promise<void>
    maximize(): Promise<void>
    unmaximize(): Promise<void>
    close(): Promise<void>
    centre(): Promise<void>
    show(): Promise<void>
    hide(): Promise<void>
    setSize(width: number, height: number): Promise<void>
    setAlwaysOnTop(onTop: boolean): Promise<void>
    setMinSize(width: number, height: number): Promise<void>
    setMaxSize(width: number, height: number): Promise<void>
    setPosition(x: number, y: number): Promise<void>
    focus(): Promise<void>
  }
  events: {
    on(eventName: string, callback: (data: any) => void): () => void
    off(eventName: string): void
    emit(eventName: string, data?: any): Promise<void>
  }
}

declare global {
  interface Window {
    wails: WailsAPI
  }
}

export {}
