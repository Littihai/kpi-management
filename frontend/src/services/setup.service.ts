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
  canWriteConfig: boolean
  jwtConfigured: boolean
  keyFile: string | null
  current: SetupCurrent | null
}

export interface SetupConnection { server: string; database: string; auth: 'sql' | 'windows'; user?: string; password?: string; trust: boolean }
export interface SetupAdmin { email: string; firstName: string; lastName: string; password: string }

export type SetupFailureCode = 'bad-key' | 'local-only' | 'too-many-attempts' | 'invalid' | 'admin' | 'not-connected' | 'cannot-write' | 'jwt-missing' | 'network' | 'other'
const known: SetupFailureCode[] = ['bad-key', 'local-only', 'too-many-attempts', 'invalid', 'admin', 'not-connected', 'cannot-write', 'jwt-missing']

export class SetupFailure extends Error {
  code: SetupFailureCode
  report?: DatabaseReport
  constructor(code: SetupFailureCode, message: string, report?: DatabaseReport) {
    super(message)
    this.code = code
    this.report = report
  }
}

function toFailure(error: unknown): SetupFailure {
  if (axios.isAxiosError(error)) {
    if (!error.response) return new SetupFailure('network', 'ติดต่อเซิร์ฟเวอร์ไม่ได้ ตรวจว่าโปรแกรมยังทำงานอยู่')
    const data = error.response.data as { error?: string; message?: string; report?: Parameters<typeof normalizeReport>[0] } | undefined
    const code = known.find((c) => c === data?.error) ?? 'other'
    return new SetupFailure(code, data?.message ?? `เกิดข้อผิดพลาด (${error.response.status})`, data?.report ? normalizeReport(data.report) : undefined)
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

export interface SetupTestResult { report: DatabaseReport; startupError: string | null; jwtConfigured: boolean }
export interface SetupApplyResult { saved: boolean; done: string[]; admin: { created: boolean; message: string } | null; report: DatabaseReport }

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

  async test(key: string, connection: SetupConnection): Promise<SetupTestResult> {
    const data = await call<Omit<SetupTestResult, 'report'> & { report: Parameters<typeof normalizeReport>[0] }>(
      { method: 'POST', url: '/api/setup/test', data: connection, timeout: 60000 }, key)
    return { ...data, report: normalizeReport(data.report) }
  },

  async apply(key: string, request: { connection: SetupConnection; repair: RepairRequest; admin?: SetupAdmin }): Promise<SetupApplyResult> {
    const data = await call<Omit<SetupApplyResult, 'report'> & { report: Parameters<typeof normalizeReport>[0] }>(
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
