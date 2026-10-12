import api from './api'
import type {
  KpiDto, CreateKpiRequest, UpdateKpiRequest, UpdateProgressRequest, DepartmentOption,
} from '../types/kpi'

export const kpiService = {
  async getAll(): Promise<KpiDto[]> {
    const res = await api.get<KpiDto[]>('/api/kpi')
    return res.data
  },

  async getById(id: string): Promise<KpiDto> {
    const res = await api.get<KpiDto>(`/api/kpi/${id}`)
    return res.data
  },

  async create(data: CreateKpiRequest): Promise<KpiDto> {
    const res = await api.post<KpiDto>('/api/kpi', data)
    return res.data
  },

  async update(id: string, data: UpdateKpiRequest): Promise<KpiDto> {
    const res = await api.put<KpiDto>(`/api/kpi/${id}`, data)
    return res.data
  },

  async remove(id: string): Promise<void> {
    await api.delete(`/api/kpi/${id}`)
  },

  async submit(id: string): Promise<KpiDto> {
    const res = await api.post<KpiDto>(`/api/kpi/${id}/submit`)
    return res.data
  },

  async approve(id: string): Promise<KpiDto> {
    const res = await api.post<KpiDto>(`/api/kpi/${id}/approve`)
    return res.data
  },

  async reject(id: string, reason: string): Promise<KpiDto> {
    const res = await api.post<KpiDto>(`/api/kpi/${id}/reject`, { reason })
    return res.data
  },

  async close(id: string): Promise<KpiDto> {
    const res = await api.post<KpiDto>(`/api/kpi/${id}/close`)
    return res.data
  },

  async updateProgress(data: UpdateProgressRequest): Promise<KpiDto> {
    const res = await api.post<KpiDto>('/api/kpi/progress', data)
    return res.data
  },

  async departments(): Promise<DepartmentOption[]> {
    const res = await api.get<DepartmentOption[]>('/api/departments')
    return res.data
  },
}
