import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { kpiService } from '../services/kpi.service'
import TrafficLight from '../components/TrafficLight'
import type { KpiDto } from '../types/kpi'
import { exportService } from '../services/dashboard.service'
import { useAuthStore } from '../stores/authStore'

export default function KpiListPage() {
  const navigate = useNavigate()
  const [kpis, setKpis] = useState<KpiDto[]>([])
  const [loading, setLoading] = useState(true)
  const { token } = useAuthStore()
  
  useEffect(() => {
    kpiService.getAll()
      .then(setKpis)
      .finally(() => setLoading(false))
  }, [])

  if (loading) return (
    <div className="flex items-center justify-center h-64 text-gray-400">
      Loading...
    </div>
  )

  return (
    <div className="flex justify-between items-center mb-6">
  <h1 className="text-xl font-medium text-gray-800">KPI Management</h1>
  <div className="flex gap-2">
    <button
      onClick={() => exportService.downloadExcel(token!)}
      className="border border-gray-200 text-gray-600 px-4 py-2 rounded-lg text-sm hover:bg-gray-50"
    >
      Export Excel
    </button>
    <button
      onClick={() => exportService.downloadPdf(token!)}
      className="border border-gray-200 text-gray-600 px-4 py-2 rounded-lg text-sm hover:bg-gray-50"
    >
      Export PDF
    </button>
    <button
      onClick={() => navigate('/kpi/create')}
      className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700"
    >
      + Create KPI
    </button>
  </div>

      {kpis.length === 0 ? (
        <div className="bg-white rounded-xl border border-gray-100 p-12 text-center text-gray-400">
          No KPIs yet — create your first one
        </div>
      ) : (
        <div className="space-y-3">
          {kpis.map(kpi => (
            <div
              key={kpi.id}
              onClick={() => navigate(`/kpi/${kpi.id}`)}
              className="bg-white rounded-xl border border-gray-100 p-5 cursor-pointer hover:border-blue-200 transition-colors"
            >
              <div className="flex justify-between items-start mb-3">
                <div>
                  <h2 className="font-medium text-gray-800">{kpi.name}</h2>
                  <p className="text-sm text-gray-400 mt-1">
                    {kpi.departmentName} · {kpi.year} · {kpi.ownerName}
                  </p>
                </div>
                <span className={`text-xs px-2 py-1 rounded-full font-medium ${
                  kpi.statusLabel === 'Active' ? 'bg-green-50 text-green-700' :
                  kpi.statusLabel === 'Draft' ? 'bg-gray-100 text-gray-600' :
                  kpi.statusLabel === 'PendingApproval' ? 'bg-yellow-50 text-yellow-700' :
                  kpi.statusLabel === 'Delayed' ? 'bg-red-50 text-red-700' :
                  'bg-blue-50 text-blue-700'
                }`}>
                  {kpi.statusLabel}
                </span>
              </div>

              <div className="flex items-center gap-3">
                <div className="flex-1 bg-gray-100 rounded-full h-2">
                  <div
                    className={`h-2 rounded-full transition-all ${
                      kpi.trafficLight === 'green' ? 'bg-green-500' :
                      kpi.trafficLight === 'yellow' ? 'bg-yellow-400' :
                      kpi.trafficLight === 'red' ? 'bg-red-500' : 'bg-gray-300'
                    }`}
                    style={{ width: `${kpi.progress}%` }}
                  />
                </div>
                <div className="flex items-center gap-2 min-w-fit">
                  <TrafficLight color={kpi.trafficLight} />
                  <span className="text-sm font-medium text-gray-700">
                    {kpi.progress}%
                  </span>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}