import api from './api'
import type { Paged } from '../types/issue'

export interface AuthLogRow {
  id: string
  at: string
  event: string
  success: boolean
  userId: string | null
  email: string | null
  ipAddress: string | null
  userAgent: string | null
  reason: string | null
  detail: string | null
}

export interface AuthLogSearch {
  search?: string
  event?: string
  success?: boolean
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export const authLogService = {
  async search(params: AuthLogSearch): Promise<Paged<AuthLogRow>> {
    return (await api.get<Paged<AuthLogRow>>('/api/auth-logs', { params })).data
  },
  async events(): Promise<string[]> {
    return (await api.get<string[]>('/api/auth-logs/events')).data
  },
}
