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
}

interface Window {
  desktop?: DesktopApi
}
