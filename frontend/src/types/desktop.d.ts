/** What the Electron preload (desktop/preload.cjs) exposes; undefined in a normal browser. */
interface DesktopUpdateState {
  status: 'idle' | 'checking' | 'available' | 'not-available' | 'downloading' | 'downloaded' | 'error'
  version?: string
  percent?: number
}

/** The sign-in this computer was asked to remember. Encrypted with the Windows account; one entry per server address. */
interface DesktopCredentials {
  /** false when the operating system cannot encrypt, in which case nothing is ever stored */
  available(): Promise<boolean>
  get(server: string): Promise<{ email: string; password: string } | null>
  save(server: string, email: string, password: string): Promise<boolean>
  /** Keeps a remembered password current after a change, only if the remembered account is this email. */
  updatePassword(server: string, email: string, password: string): Promise<boolean>
  forget(server: string): Promise<boolean>
}

interface DesktopApi {
  version: string
  isDesktop: true
  /** The server address the company set for this computer (HELPDESK_SERVER_URL or ProgramData\\Helpdesk\\server.txt), if any. */
  defaultServerUrl?: string
  checkForUpdates(): Promise<void>
  restartToUpdate(): void
  /** Absent in a desktop build older than this feature. */
  credentials?: DesktopCredentials
  onUpdateState(cb: (state: DesktopUpdateState) => void): () => void
}

interface Window {
  desktop?: DesktopApi
}
