interface WailsEventCallback {
  (data: any): void
}

interface WailsEvents {
  on(name: string, cb: WailsEventCallback): () => void
  off(name: string): void
  emit(name: string, data?: any): Promise<void>
}

interface WailsWindow {
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

interface WailsAPI {
  call(name: string, args?: any[]): Promise<any>
  events: WailsEvents
  window: WailsWindow
}

declare global {
  interface Window {
    wails: WailsAPI
  }
}

export {}
