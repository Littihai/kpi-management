/** What the Electron preload (desktop/preload.cjs) exposes; undefined in a normal browser. */
interface DesktopUpdateState {
  status: 'idle' | 'checking' | 'available' | 'not-available' | 'downloading' | 'downloaded' | 'error'
  version?: string
  percent?: number
}

interface DesktopApi {
  version: string
  isDesktop: true
  checkForUpdates(): Promise<void>
  restartToUpdate(): void
  onUpdateState(cb: (state: DesktopUpdateState) => void): () => void
}

interface Window {
  desktop?: DesktopApi
}
