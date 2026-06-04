import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  BarChart, Bar, XAxis, YAxis, Tooltip,
  LineChart, Line, ResponsiveContainer, CartesianGrid
} from 'recharts'
import { useAuthStore } from '../stores/authStore'
import { dashboardService, type DashboardDto } from '../services/dashboard.service'
import TrafficLight from '../components/TrafficLight'

export default function DashboardPage() {
  const { user, logout } = useAuthStore()
  const navigate = useNavigate()
  const [data, setData] = useState<DashboardDto | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    dashboardService.get()
      .then(setData)
      .finally(() => setLoading(false))
  }, [])

  const handleLogout = () => { logout(); navigate('/login') }

  if (loading) return (
    <div className="flex items-center justify-center h-64 text-gray-400">Loading...</div>
  )

  if (!data) return null

  return (
    <div className="p-8">
      <div className="flex justify-between items-center mb-8">
        <div>
          <h1 className="text-xl font-medium text-gray-800">Executive Dashboard</h1>
          <p className="text-sm text-gray-400 mt-1">Welcome back, {user?.fullName}</p>
        </div>
        <button onClick={handleLogout} className="text-sm text-gray-400 hover:text-gray-600">
          Sign out
        </button>
      </div>

      {/* Metrics */}
      <div className="grid grid-cols-4 gap-4 mb-6">
        {[
          { label: 'Overall KPI', value: `${data.overallProgress}%`, color: 'text-gray-800' },
          { label: 'Active KPIs', value: data.activeKpis, color: 'text-green-600' },
          { label: 'Delayed', value: data.delayedKpis, color: 'text-red-500' },
          { label: 'On Track', value: data.onTrackKpis, color: 'text-blue-600' },
        ].map(m => (
          <div key={m.label} className="bg-white rounded-xl border border-gray-100 p-5">
            <p className="text-xs text-gray-400 mb-2">{m.label}</p>
            <p className={`text-2xl font-semibold ${m.color}`}>{m.value}</p>
          </div>
        ))}
      </div>

      <div className="grid grid-cols-2 gap-6 mb-6">
        {/* Department Ranking */}
        <div className="bg-white rounded-xl border border-gray-100 p-5">
          <h2 className="text-sm font-medium text-gray-700 mb-4">Department Ranking</h2>
          <ResponsiveContainer width="100%" height={200}>
            <BarChart data={data.departmentRankings} layout="vertical">
              <CartesianGrid strokeDasharray="3 3" horizontal={false} />
              <XAxis type="number" domain={[0, 100]} tick={{ fontSize: 11 }} />
              <YAxis type="category" dataKey="departmentName" tick={{ fontSize: 11 }} width={90} />
              <Tooltip formatter={(v) => `${v}%`} />
              <Bar dataKey="averageProgress" fill="#534AB7" radius={[0, 4, 4, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>

        {/* Monthly Trend */}
        <div className="bg-white rounded-xl border border-gray-100 p-5">
          <h2 className="text-sm font-medium text-gray-700 mb-4">Monthly Trend</h2>
          <ResponsiveContainer width="100%" height={200}>
            <LineChart data={data.monthlyTrend}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="month" tick={{ fontSize: 11 }} />
              <YAxis domain={[0, 100]} tick={{ fontSize: 11 }} />
              <Tooltip formatter={(v) => `${v}%`} />
              <Line
                type="monotone"
                dataKey="averageProgress"
                stroke="#534AB7"
                strokeWidth={2}
                dot={{ fill: '#534AB7' }}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Recent KPIs */}
      <div className="bg-white rounded-xl border border-gray-100 p-5">
        <h2 className="text-sm font-medium text-gray-700 mb-4">Recent KPIs</h2>
        <div className="space-y-3">
          {data.recentKpis.map(kpi => (
            <div
              key={kpi.id}
              onClick={() => navigate(`/kpi/${kpi.id}`)}
              className="flex items-center gap-4 cursor-pointer hover:bg-gray-50 rounded-lg p-2 transition-colors"
            >
              <TrafficLight color={kpi.trafficLight as any} />
              <div className="flex-1">
                <p className="text-sm font-medium text-gray-700">{kpi.name}</p>
                <p className="text-xs text-gray-400">{kpi.departmentName}</p>
              </div>
              <div className="w-32 bg-gray-100 rounded-full h-1.5">
                <div
                  className={`h-1.5 rounded-full ${
                    kpi.trafficLight === 'green' ? 'bg-green-500' :
                    kpi.trafficLight === 'yellow' ? 'bg-yellow-400' : 'bg-red-500'
                  }`}
                  style={{ width: `${kpi.progress}%` }}
                />
              </div>
              <span className="text-sm font-medium text-gray-600 w-10 text-right">
                {kpi.progress}%
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}