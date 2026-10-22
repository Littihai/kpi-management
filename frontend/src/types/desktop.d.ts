/** What the Electron preload (desktop/preload.cjs) exposes; undefined in a normal browser. */
interface DesktopUpdateState {
  status: 'idle' | 'checking' | 'available' | 'not-available' | 'downloading' | 'downloaded' | 'error'
  version?: string
  percent?: number
}

interface DesktopApi {
  version: string
  isDesktop: true
  /** The server address the company set for this computer (HELPDESK_SERVER_URL or ProgramData\\Helpdesk\\server.txt), if any. */
  defaultServerUrl?: string
  checkForUpdates(): Promise<void>
  restartToUpdate(): void
  onUpdateState(cb: (state: DesktopUpdateState) => void): () => void
}

interface Window {
  desktop?: DesktopApi
}
