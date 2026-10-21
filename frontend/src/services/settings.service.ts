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
}

export interface SettingRow extends Omit<RawSetting, 'type'> {
  kind: SettingKind
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
  async history(key: string): Promise<SettingHistoryRow[]> {
    return (await api.get<SettingHistoryRow[]>(`/api/settings/${encodeURIComponent(key)}/history`)).data
  },
}
