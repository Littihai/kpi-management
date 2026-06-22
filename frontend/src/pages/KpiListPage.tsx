import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { kpiService } from '../services/kpi.service'
import { exportService } from '../services/dashboard.service'
import { useAuthStore } from '../stores/authStore'
import type { KpiDto } from '../types/kpi'

const statusBadge: Record<string, string> = {
  Draft: 'bg-gray-100 text-gray-600',
  PendingApproval: 'bg-yellow-50 text-yellow-700',
  Active: 'bg-green-50 text-green-700',
  AtRisk: 'bg-orange-50 text-orange-700',
  Delayed: 'bg-red-50 text-red-600',
  Completed: 'bg-green-50 text-green-700',
  Closed: 'bg-gray-100 text-gray-500',
}

const trafficColor: Record<string, string> = {
  green: 'bg-green-500',
  yellow: 'bg-yellow-400',
  red: 'bg-red-500',
  gray: 'bg-gray-300',
}

const trafficBar: Record<string, string> = {
  green: 'bg-green-500',
  yellow: 'bg-yellow-400',
  red: 'bg-red-500',
  gray: 'bg-gray-200',
}

export default function KpiListPage() {
  const navigate = useNavigate()
  const { token } = useAuthStore()
  const [kpis, setKpis] = useState<KpiDto[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState('All')

  useEffect(() => {
    kpiService.getAll()
      .then(setKpis)
      .finally(() => setLoading(false))
  }, [])

  const filters = ['All', 'Active', 'PendingApproval', 'AtRisk', 'Delayed', 'Completed']

  const filtered = kpis.filter(k => {
    const matchFilter = filter === 'All' || k.statusLabel === filter
    const matchSearch = k.name.toLowerCase().includes(search.toLowerCase()) ||
      k.departmentName.toLowerCase().includes(search.toLowerCase())
    return matchFilter && matchSearch
  })

  const metrics = {
    total: kpis.length,
    active: kpis.filter(k => k.statusLabel === 'Active').length,
    atRisk: kpis.filter(k => k.statusLabel === 'AtRisk').length,
    delayed: kpis.filter(k => k.statusLabel === 'Delayed').length,
  }

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Topbar */}
      <div className="bg-white border-b border-gray-100 px-8 py-4 flex justify-between items-center flex-shrink-0">
        <div>
          <h1 className="text-base font-medium text-gray-800">KPI Management</h1>
          <p className="text-xs text-gray-400 mt-0.5">{kpis.length} KPIs · 2026 Annual</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => exportService.downloadExcel(token!)}
            className="flex items-center gap-1.5 px-3 py-2 border border-gray-200 rounded-lg text-xs text-gray-600 hover:bg-gray-50"
          >
            <i className="ti ti-file-type-xls text-sm"></i> Export Excel
          </button>
          <button
            onClick={() => exportService.downloadPdf(token!)}
            className="flex items-center gap-1.5 px-3 py-2 border border-gray-200 rounded-lg text-xs text-gray-600 hover:bg-gray-50"
          >
            <i className="ti ti-file-type-pdf text-sm"></i> Export PDF
          </button>
          <button
            onClick={() => navigate('/kpi/create')}
            className="flex items-center gap-1.5 px-3 py-2 bg-indigo-600 text-white rounded-lg text-xs hover:bg-indigo-700"
          >
            <i className="ti ti-plus text-sm"></i> Create KPI
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-8">
        {/* Metrics */}
        <div className="grid grid-cols-4 gap-4 mb-6">
          {[
            { label: 'Total KPIs', value: metrics.total, color: 'text-gray-800', icon: 'ti-target', bg: 'bg-indigo-50', iconColor: 'text-indigo-600' },
            { label: 'Active', value: metrics.active, color: 'text-green-600', icon: 'ti-circle-check', bg: 'bg-green-50', iconColor: 'text-green-600' },
            { label: 'At Risk', value: metrics.atRisk, color: 'text-orange-500', icon: 'ti-alert-triangle', bg: 'bg-orange-50', iconColor: 'text-orange-500' },
            { label: 'Delayed', value: metrics.delayed, color: 'text-red-500', icon: 'ti-clock', bg: 'bg-red-50', iconColor: 'text-red-500' },
          ].map(m => (
            <div key={m.label} className="bg-white border border-gray-100 rounded-xl p-4">
              <div className={`w-8 h-8 ${m.bg} rounded-lg flex items-center justify-center mb-3`}>
                <i className={`ti ${m.icon} ${m.iconColor} text-base`}></i>
              </div>
              <div className="text-xs text-gray-400 mb-1">{m.label}</div>
              <div className={`text-2xl font-medium ${m.color}`}>{m.value}</div>
            </div>
          ))}
        </div>

        {/* Filters + Search */}
        <div className="flex items-center gap-2 mb-4">
          <div className="flex items-center gap-1 bg-white border border-gray-100 rounded-lg p-1">
            {filters.map(f => (
              <button
                key={f}
                onClick={() => setFilter(f)}
                className={`px-3 py-1.5 rounded-md text-xs transition-colors ${
                  filter === f ? 'bg-indigo-600 text-white' : 'text-gray-500 hover:text-gray-700'
                }`}
              >
                {f === 'PendingApproval' ? 'Pending' : f}
              </button>
            ))}
          </div>
          <div className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-100 rounded-lg ml-auto">
            <i className="ti ti-search text-gray-400 text-sm"></i>
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search KPIs..."
              className="text-xs outline-none bg-transparent text-gray-700 w-40"
            />
          </div>
        </div>

        {/* KPI List */}
        {loading ? (
          <div className="flex items-center justify-center h-40 text-sm text-gray-400">Loading...</div>
        ) : filtered.length === 0 ? (
          <div className="bg-white border border-gray-100 rounded-xl p-12 text-center text-sm text-gray-400">
            No KPIs found
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            {filtered.map(kpi => (
              <div
                key={kpi.id}
                onClick={() => navigate(`/kpi/${kpi.id}`)}
                className="bg-white border border-gray-100 rounded-xl px-5 py-4 cursor-pointer hover:border-indigo-200 transition-colors"
              >
                <div className="flex justify-between items-start mb-3">
                  <div className="flex-1 min-w-0 pr-4">
                    <h2 className="text-sm font-medium text-gray-800 truncate">{kpi.name}</h2>
                    <div className="flex items-center gap-2 mt-1">
                      <i className="ti ti-user text-gray-300 text-xs"></i>
                      <span className="text-xs text-gray-400">{kpi.ownerName}</span>
                      <span className="text-gray-200">·</span>
                      <span className="text-xs px-1.5 py-0.5 bg-gray-100 text-gray-500 rounded">{kpi.departmentName}</span>
                      <span className="text-gray-200">·</span>
                      <span className="text-xs text-gray-400">{kpi.year}</span>
                    </div>
                  </div>
                  <span className={`text-xs px-2.5 py-1 rounded-full font-medium flex-shrink-0 ${statusBadge[kpi.statusLabel] ?? 'bg-gray-100 text-gray-600'}`}>
                    {kpi.statusLabel}
                  </span>
                </div>

                <div className="flex items-center gap-3">
                  <div className="flex-1 h-1.5 bg-gray-100 rounded-full overflow-hidden">
                    <div
                      className={`h-full rounded-full ${trafficBar[kpi.trafficLight]}`}
                      style={{ width: `${kpi.progress}%` }}
                    />
                  </div>
                  <div className="flex items-center gap-1.5 min-w-fit">
                    <div className={`w-2 h-2 rounded-full ${trafficColor[kpi.trafficLight]}`}></div>
                    <span className="text-xs font-medium text-gray-700">{Number(kpi.progress).toFixed(0)}%</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}