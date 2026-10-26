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

/** The Helpdesk server inside the app, connected to SQL Server by the person. Used when no company server was chosen. */
interface DesktopLocalServerStatus {
  /** the program is part of this installation (a development run without it can only use a company server) */
  available: boolean
  /** turned on: it starts with the app */
  enabled: boolean
  running: boolean
  url: string
  /** why it is not running, when it should be */
  error: string
}

interface DesktopLocalServer {
  status(): Promise<DesktopLocalServerStatus>
  enable(): Promise<DesktopLocalServerStatus>
  disable(): Promise<DesktopLocalServerStatus>
  /** "Disconnect": forgets where the database is on this PC (its data is untouched) and starts again, waiting to be connected. */
  resetDatabase(): Promise<DesktopLocalServerStatus>
  /** The address of the running built-in server, or '' when it is off. Answered at once. */
  url(): string
}

/** What the person chose for the app on this computer; kept in a file in the app's data folder. */
interface DesktopPrefs {
  /** show a Windows notification when something concerns the signed-in person and the window is not in front */
  notifications: boolean
  /** closing the window keeps the app running in the system tray */
  closeToTray: boolean
  startWithWindows: boolean
}

/** The state shown by the tray icon, the taskbar overlay and the tooltip: green check, red number, amber or grey. */
interface DesktopStatus {
  state: 'ok' | 'attention' | 'connecting' | 'offline'
  /** the number on the red state */
  count: number
  tooltip?: string
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
  /** Absent in a desktop build older than this feature. */
  localServer?: DesktopLocalServer
  onUpdateState(cb: (state: DesktopUpdateState) => void): () => void
  /** Offered for every realtime notification; the app shows a Windows notification only when the window is not in front and the person left them on. */
  notify(notification: { title: string; message: string; issueId: string | null }): void
  setStatus(status: DesktopStatus): void
  /** Calls cb when the person clicks a notification that belongs to an issue; returns an unsubscribe function. */
  onOpenIssue(cb: (issueId: string) => void): () => void
  getPrefs(): Promise<DesktopPrefs>
  setPrefs(prefs: Partial<DesktopPrefs>): Promise<DesktopPrefs>
}

interface Window {
  desktop?: DesktopApi
}
