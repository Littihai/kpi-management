export type KpiActionName =
  | 'edit'
  | 'delete'
  | 'submit'
  | 'approve'
  | 'reject'
  | 'updateProgress'
  | 'close'

export interface KpiDto {
  id: string
  name: string
  description: string
  year: number
  target: number
  progress: number
  status: number
  statusLabel: string
  trafficLight: 'green' | 'yellow' | 'red' | 'gray'
  startDate: string
  dueDate: string
  departmentId: string
  departmentName: string
  ownerId: string
  ownerName: string
  rejectReason: string | null
  submittedAt: string | null
  approvedAt: string | null
  closedAt: string | null
  /** What the signed-in user may do with this KPI right now (decided by the server). */
  allowedActions: KpiActionName[]
}

export interface CreateKpiRequest {
  name: string
  description: string
  year: number
  target: number
  startDate: string
  dueDate: string
  departmentId: string
}

export type UpdateKpiRequest = CreateKpiRequest

export interface UpdateProgressRequest {
  kpiId: string
  progress: number
  notes: string
}

export interface DepartmentOption {
  id: string
  name: string
}
