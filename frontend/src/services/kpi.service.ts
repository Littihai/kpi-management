import api from './api'
import type { KpiDto, CreateKpiRequest, UpdateProgressRequest } from '../types/kpi'

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

  async submit(id: string): Promise<KpiDto> {
    const res = await api.post<KpiDto>(`/api/kpi/${id}/submit`)
    return res.data
  },

  async approve(id: string): Promise<KpiDto> {
    const res = await api.post<KpiDto>(`/api/kpi/${id}/approve`)
    return res.data
  },

  async updateProgress(data: UpdateProgressRequest): Promise<KpiDto> {
    const res = await api.post<KpiDto>('/api/kpi/progress', data)
    return res.data
  }
}