import api from './api'
import type { Paged } from '../types/issue'

export interface CountByLabel { label: string; count: number }

export interface RequestLogRow {
  id: string
  createdAt: string
  userId: string | null
  userEmail: string | null
  userRole: string | null
  method: string
  path: string
  statusCode: number
  durationMs: number
  ipAddress: string | null
  userAgent: string | null
}

export interface AuditRow {
  id: string
  action: string
  entityName: string
  entityId: string
  oldValues: string | null
  newValues: string | null
  userId: string | null
  userEmail: string | null
  userName: string | null
  userRole: string | null
  departmentName: string | null
  ipAddress: string | null
  userAgent: string | null
  createdAt: string
}

export interface OnlineUser {
  userId: string
  name: string
  role: string
  ipAddress: string | null
  connections: number
  since: string
}

export interface Overview {
  days: number
  generatedAt: string
  users: { total: number; active: number; inactive: number; byRole: CountByLabel[] }
  requests: {
    total: number; writes: number; unauthorized: number; forbidden: number
    errors: number; failedLogins: number; distinctIps: number
  }
  issues: {
    total: number; open: number; overdue: number; resolvedInPeriod: number
    averageResolveHours: number | null
    byStatus: CountByLabel[]; byDepartment: CountByLabel[]; byModule: CountByLabel[]
  }
  topIps: CountByLabel[]
  securityEvents: RequestLogRow[]
  activity: AuditRow[]
  trend: { day: string; requests: number; failures: number }[]
  online: OnlineUser[]
}

export interface AuditQuery {
  search?: string
  entity?: string
  action?: string
  ip?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export interface RequestLogQuery {
  search?: string
  ip?: string
  method?: string
  minStatus?: number
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export const adminService = {
  async overview(days: number): Promise<Overview> {
    return (await api.get<Overview>('/api/admin/overview', { params: { days } })).data
  },
  async audit(query: AuditQuery): Promise<Paged<AuditRow>> {
    return (await api.get<Paged<AuditRow>>('/api/audit', { params: query })).data
  },
  async requestLogs(query: RequestLogQuery): Promise<Paged<RequestLogRow>> {
    return (await api.get<Paged<RequestLogRow>>('/api/admin/request-logs', { params: query })).data
  },
}
