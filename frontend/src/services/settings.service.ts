import api from './api'

/** The API sends the type as a number (Number, Bool, Text) or, depending on configuration, its name. */
export type SettingKind = 'number' | 'bool' | 'text'
const kinds: Record<string, SettingKind> = { 0: 'number', 1: 'bool', 2: 'text', Number: 'number', Bool: 'bool', Text: 'text' }

interface RawSetting {
  key: string
  type: number | string
  category: string
  label: string
  description: string
  default: string
  value: string
  isDefault: boolean
  min: number | null
  max: number | null
  unit: string | null
  requiresRestart: boolean
  updatedAt: string | null
  updatedByName: string | null
  sensitive: boolean
  options: { value: string; label: string }[] | null
}

export interface SettingRow extends Omit<RawSetting, 'type'> {
  kind: SettingKind
}

/** One thing the connection check tried (name resolution, opening the port, the server's greeting, encryption). */
export interface EmailDiagnosisStep {
  name: string
  ok: boolean
  detail: string
}

/** What the connection check found; advice is in plain Thai, ready to show. */
export interface EmailDiagnosis {
  ok: boolean
  host: string
  port: number
  security: string
  steps: EmailDiagnosisStep[]
  advice: string[]
}

export interface SettingHistoryRow {
  key: string
  oldValue: string | null
  newValue: string | null
  changedAt: string
  changedByName: string | null
}

const toRow = ({ type, ...rest }: RawSetting): SettingRow => ({ ...rest, kind: kinds[String(type)] ?? 'text' })

export const settingsService = {
  async list(): Promise<SettingRow[]> {
    return (await api.get<RawSetting[]>('/api/settings')).data.map(toRow)
  },
  async update(key: string, value: string): Promise<SettingRow> {
    return toRow((await api.put<RawSetting>(`/api/settings/${encodeURIComponent(key)}`, { value })).data)
  },
  async reset(key: string): Promise<SettingRow> {
    return toRow((await api.delete<RawSetting>(`/api/settings/${encodeURIComponent(key)}`)).data)
  },
  async sendTestEmail(to: string): Promise<string> {
    return (await api.post<{ message: string }>('/api/settings/email/test', { to })).data.message
  },
  /** Checks the saved SMTP settings without logging in or sending anything. */
  async diagnoseEmail(): Promise<EmailDiagnosis> {
    return (await api.post<EmailDiagnosis>('/api/settings/email/diagnose')).data
  },
  async history(key: string): Promise<SettingHistoryRow[]> {
    return (await api.get<SettingHistoryRow[]>(`/api/settings/${encodeURIComponent(key)}/history`)).data
  },
}
