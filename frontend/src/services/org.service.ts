import api from './api'
import type { Paged } from '../types/issue'

export interface DepartmentRow {
  id: string
  code: string
  name: string
  isActive: boolean
  managerId: string | null
  managerName: string | null
  userCount: number
}

export interface SaveDepartment {
  code: string
  name: string
  managerId: string | null
  isActive: boolean
}

export interface UserRow {
  id: string
  firstName: string
  lastName: string
  email: string
  isActive: boolean
  role: string
  departmentId: string | null
  department: string | null
  /** Set while the account is locked after too many wrong passwords. */
  lockedUntil?: string | null
}

export interface UserSearch {
  search?: string
  role?: string
  departmentId?: string
  active?: boolean
  page?: number
  pageSize?: number
}

export interface UpdateUser {
  firstName: string
  lastName: string
  email: string
  departmentId: string | null
}

/** People and departments: the organisation data the rest of the helpdesk leans on. */
export const orgService = {
  async departments(): Promise<DepartmentRow[]> {
    return (await api.get<DepartmentRow[]>('/api/departments/all')).data
  },

  /** Active departments as id and name, for pickers. */
  async departmentOptions(): Promise<{ id: string; name: string }[]> {
    return (await api.get('/api/departments')).data
  },

  async createDepartment(body: SaveDepartment): Promise<DepartmentRow> {
    return (await api.post<DepartmentRow>('/api/departments', body)).data
  },

  async updateDepartment(id: string, body: SaveDepartment): Promise<DepartmentRow> {
    return (await api.put<DepartmentRow>(`/api/departments/${id}`, body)).data
  },

  async users(query: UserSearch): Promise<Paged<UserRow>> {
    return (await api.get<Paged<UserRow>>('/api/users', { params: query })).data
  },

  async updateUser(id: string, body: UpdateUser): Promise<UserRow> {
    return (await api.put<UserRow>(`/api/users/${id}`, body)).data
  },

  async unlock(id: string): Promise<UserRow> {
    return (await api.post<UserRow>(`/api/users/${id}/unlock`)).data
  },

  async reactivate(id: string): Promise<UserRow> {
    return (await api.put<UserRow>(`/api/users/${id}/reactivate`)).data
  },

  async deactivate(id: string): Promise<void> {
    await api.put(`/api/users/${id}/deactivate`)
  },

  async changeRole(id: string, roleName: string): Promise<void> {
    await api.put(`/api/users/${id}/role`, { roleName })
  },

  async resetPassword(id: string, newPassword: string): Promise<void> {
    await api.post(`/api/users/${id}/reset-password`, { newPassword })
  },
}
