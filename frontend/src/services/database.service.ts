import api from './api'

export type DatabaseState = 'Ready' | 'NeedsRepair' | 'Blocked'
// The API sends the state as a number (0, 1, 2) or, depending on configuration, its name.
const states: Record<string, DatabaseState> = { 0: 'Ready', 1: 'NeedsRepair', 2: 'Blocked', Ready: 'Ready', NeedsRepair: 'NeedsRepair', Blocked: 'Blocked' }

export interface MissingColumn { table: string; column: string; storeType: string }

export interface SeedReport {
  rolesFound: number
  rolesExpected: number
  missingRoles: string[]
  permissionsFound: number
  permissionsExpected: number
  hasActiveAdmin: boolean
}

export interface DatabaseReport {
  engine: string
  supported: boolean
  connected: boolean
  connectionProblem: 'unreachable' | 'login-failed' | 'no-database' | 'other' | null
  connectionMessage: string | null
  serverVersion: string | null
  databaseName: string | null
  collation: string | null
  caseInsensitive: boolean | null
  appliedMigrations: string[]
  pendingMigrations: string[]
  missingTables: string[]
  missingColumns: MissingColumn[]
  seed: SeedReport | null
  warnings: string[]
  state: DatabaseState
}

export interface RepairPreview { steps: string[]; script: string | null }
export interface RepairResult { done: string[]; report: DatabaseReport }
export interface RepairRequest { applyMigrations: boolean; createMissingObjects: boolean; seed: boolean }

export function normalizeReport(raw: Omit<DatabaseReport, 'state'> & { state: number | string }): DatabaseReport {
  return { ...raw, state: states[String(raw.state)] ?? 'Blocked' }
}

export const databaseService = {
  async status(): Promise<DatabaseReport> {
    return normalizeReport((await api.get('/api/database/status')).data)
  },
  async preview(): Promise<RepairPreview> {
    return (await api.get<RepairPreview>('/api/database/repair/preview')).data
  },
  async repair(request: RepairRequest): Promise<RepairResult> {
    const data = (await api.post('/api/database/repair', request)).data
    return { done: data.done, report: normalizeReport(data.report) }
  },
}
