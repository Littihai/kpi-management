import api from './api'

export const IssueVisibility = { Own: 0, Department: 1, All: 2 } as const
export type IssueVisibilityValue = (typeof IssueVisibility)[keyof typeof IssueVisibility]

export interface PermissionInfo {
  name: string
  module: string
  group: string
  label: string
  description: string
}

export interface RoleDetail {
  id: string
  name: string
  description: string
  isSystem: boolean
  userCount: number
  permissions: string[]
  issueVisibility: IssueVisibilityValue
  seesAllDepartments: boolean
}

export interface SaveRole {
  name: string
  description: string
  permissions: string[]
  issueVisibility: IssueVisibilityValue
  seesAllDepartments: boolean
}

export const rolesService = {
  async details(): Promise<RoleDetail[]> {
    return (await api.get<RoleDetail[]>('/api/roles/details')).data
  },
  async catalog(): Promise<PermissionInfo[]> {
    return (await api.get<PermissionInfo[]>('/api/roles/permissions')).data
  },
  async create(body: SaveRole): Promise<RoleDetail> {
    return (await api.post<RoleDetail>('/api/roles', body)).data
  },
  async update(id: string, body: SaveRole): Promise<RoleDetail> {
    return (await api.put<RoleDetail>(`/api/roles/${id}`, body)).data
  },
  async remove(id: string): Promise<void> {
    await api.delete(`/api/roles/${id}`)
  },
}
