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
  departmentName: string
  ownerName: string
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

export interface UpdateProgressRequest {
  kpiId: string
  progress: number
  notes: string
}