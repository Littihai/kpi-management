import api from './api'

export interface DashboardDto {
  totalKpis: number
  activeKpis: number
  delayedKpis: number
  onTrackKpis: number
  overallProgress: number
  departmentRankings: {
    departmentName: string
    averageProgress: number
    totalKpis: number
    trafficLight: string
  }[]
  recentKpis: {
    id: string
    name: string
    progress: number
    statusLabel: string
    trafficLight: string
    departmentName: string
  }[]
  monthlyTrend: {
    month: string
    averageProgress: number
  }[]
}

export const dashboardService = {
  async get(): Promise<DashboardDto> {
    const res = await api.get<DashboardDto>('/api/dashboard')
    return res.data
  }
}

export const exportService = {
  async downloadPdf(token: string) {
    const res = await fetch(`${api.defaults.baseURL}/api/export/kpi/pdf`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `kpi-report-${new Date().toISOString().slice(0, 10)}.pdf`
    a.click()
    URL.revokeObjectURL(url)
  },

  async downloadExcel(token: string) {
    const res = await fetch(`${api.defaults.baseURL}/api/export/kpi/excel`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `kpi-report-${new Date().toISOString().slice(0, 10)}.xlsx`
    a.click()
    URL.revokeObjectURL(url)
  }
}