import axios, { type AxiosRequestConfig } from 'axios'
import api from './api'
import { normalizeReport, type DatabaseReport, type RepairRequest } from './database.service'

export type SetupReason = 'NotConfigured' | 'Unreachable' | 'StartupFailed'

export interface SetupCurrent { server: string | null; database: string | null; auth: string | null; user: string | null; trust: boolean }

/** What the server says while it is waiting to be told where its database is. */
export interface SetupStatus {
  mode: 'setup'
  reason: SetupReason
  problem: string | null
  engine: string
  remoteAllowed: boolean
  /** A setup key must be sent (it was set on purpose, or this is not the server itself). */
  keyRequired: boolean
  canWriteConfig: boolean
  jwtConfigured: boolean
  keyFile: string | null
  /** The name this machine's own SQL account will get (helpdesk_ plus the computer's name). */
  machineLogin: string
  current: SetupCurrent | null
}

/** Where SQL Server is and who is asking: usually sa, or the Windows account the service runs as. */
export interface SetupConnection { server: string; database: string; auth: 'sql' | 'windows'; user?: string; password?: string; trust: boolean }
export interface SetupAdmin { email: string; firstName: string; lastName: string; password: string }
/** "auto": the application gets an account of its own when the account typed in may create one; "same": it uses the account typed in. */
export interface SetupAppAccess { mode: 'auto' | 'dedicated' | 'same'; login?: string }

/** What a SQL Server says about itself and about the account that connected. */
export interface ServerProbe {
  connected: boolean
  problem: 'unreachable' | 'login-failed' | 'other' | null
  message: string | null
  version: string | null
  canCreateDatabase: boolean
  canCreateLogin: boolean
  databaseExists: boolean
  canOpenDatabase: boolean
}

/** What "start" will do. */
export interface SetupPlan {
  createDatabase: boolean
  access: 'windows' | 'dedicated' | 'same'
  login: string | null
  blocker: 'no-permission-database' | 'no-access' | 'no-permission-login' | null
}

export type SetupFailureCode =
  | 'bad-key' | 'local-only' | 'too-many-attempts' | 'invalid' | 'admin' | 'not-connected' | 'cannot-write' | 'jwt-missing'
  | 'no-permission' | 'no-permission-database' | 'no-access' | 'no-permission-login' | 'login-in-use' | 'weak-password' | 'app-login-failed'
  | 'network' | 'other'
const known: SetupFailureCode[] = [
  'bad-key', 'local-only', 'too-many-attempts', 'invalid', 'admin', 'not-connected', 'cannot-write', 'jwt-missing',
  'no-permission', 'no-permission-database', 'no-access', 'no-permission-login', 'login-in-use', 'weak-password', 'app-login-failed',
]

export class SetupFailure extends Error {
  code: SetupFailureCode
  report?: DatabaseReport
  server?: ServerProbe
  constructor(code: SetupFailureCode, message: string, report?: DatabaseReport, server?: ServerProbe) {
    super(message)
    this.code = code
    this.report = report
    this.server = server
  }
}

type RawReport = Parameters<typeof normalizeReport>[0]

function toFailure(error: unknown): SetupFailure {
  if (axios.isAxiosError(error)) {
    if (!error.response) return new SetupFailure('network', 'ติดต่อเซิร์ฟเวอร์ไม่ได้ ตรวจว่าโปรแกรมยังทำงานอยู่')
    const data = error.response.data as { error?: string; message?: string; report?: RawReport; server?: ServerProbe } | undefined
    const code = known.find((c) => c === data?.error) ?? 'other'
    return new SetupFailure(code, data?.message ?? `เกิดข้อผิดพลาด (${error.response.status})`, data?.report ? normalizeReport(data.report) : undefined, data?.server)
  }
  return new SetupFailure('other', 'เกิดข้อผิดพลาด')
}

// The setup calls have their own client: the shared one signs the person out and goes to the login page on a 401, and here
// a 401 only means the setup key was typed wrongly.
async function call<T>(config: AxiosRequestConfig, key?: string): Promise<T> {
  try {
    return (await axios.request<T>({ ...config, baseURL: api.defaults.baseURL, headers: key ? { 'X-Setup-Key': key } : undefined })).data
  } catch (error) {
    throw toFailure(error)
  }
}

export type SetupProbe = { kind: 'setup'; status: SetupStatus } | { kind: 'normal' } | { kind: 'unknown' }

export interface SetupTestResult {
  server: ServerProbe
  /** The database as it is now; null while it does not exist yet. */
  report: DatabaseReport | null
  plan: SetupPlan | null
  startupError: string | null
  jwtConfigured: boolean
}
export interface SetupApplyResult {
  saved: boolean
  createdDatabase: boolean
  access: SetupPlan['access']
  login: string | null
  done: string[]
  admin: { created: boolean; message: string } | null
  report: DatabaseReport
}

export const setupService = {
  /**
   * Is this server waiting to be set up? 'normal' only when the server answered and has no setup page (it is running);
   * anything else (asleep, restarting, not reachable) is 'unknown' so a restart in the middle of setup is not mistaken for the end of it.
   */
  async probe(): Promise<SetupProbe> {
    try {
      const response = await axios.get<SetupStatus>('/api/setup/status', { baseURL: api.defaults.baseURL, timeout: 6000, validateStatus: () => true })
      if (response.status === 200 && response.data?.mode === 'setup') return { kind: 'setup', status: response.data }
      if (response.status === 404 || response.status === 401 || response.status === 405) return { kind: 'normal' }
      return { kind: 'unknown' }
    } catch {
      return { kind: 'unknown' }
    }
  },

  async test(key: string | undefined, request: { connection: SetupConnection; appAccess?: SetupAppAccess }): Promise<SetupTestResult> {
    const data = await call<Omit<SetupTestResult, 'report'> & { report: RawReport | null }>(
      { method: 'POST', url: '/api/setup/test', data: request, timeout: 60000 }, key)
    return { ...data, report: data.report ? normalizeReport(data.report) : null }
  },

  async apply(key: string | undefined, request: { connection: SetupConnection; repair: RepairRequest; admin?: SetupAdmin; appAccess?: SetupAppAccess }): Promise<SetupApplyResult> {
    const data = await call<Omit<SetupApplyResult, 'report'> & { report: RawReport }>(
      { method: 'POST', url: '/api/setup/apply', data: request, timeout: 300000 }, key)
    return { ...data, report: normalizeReport(data.report) }
  },

  /** After saving: wait for the application to start in place of the setup page. */
  async waitUntilRunning(seconds = 180): Promise<boolean> {
    const deadline = Date.now() + seconds * 1000
    while (Date.now() < deadline) {
      try {
        const response = await axios.get('/health/ready', { baseURL: api.defaults.baseURL, timeout: 4000, validateStatus: () => true })
        if (response.status === 200) return true
      } catch { /* the server is changing over; try again */ }
      await new Promise((resolve) => setTimeout(resolve, 1500))
    }
    return false
  },
}
