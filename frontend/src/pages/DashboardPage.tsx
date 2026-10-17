import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { TrafficBadge } from '../components/ui/Badge'
import { Card, CardHeader, Stat } from '../components/ui/Card'
import { Alert, EmptyState, Page, PageHeader, PageSkeleton } from '../components/ui/Layout'
import Progress from '../components/ui/Progress'
import { useKpiEvents } from '../components/realtimeContext'
import { useAuthStore } from '../stores/authStore'
import { dashboardService, type DashboardDto } from '../services/dashboard.service'
import { apiError } from '../utils/errors'
import { formatNumber } from '../utils/format'

const lightColor: Record<string, string> = { green: '#22c55e', yellow: '#f5a524', red: '#ef4444', gray: '#bcc0c7' }
const lightLabel: Record<string, string> = { green: 'ตามเป้า', yellow: 'ต้องระวัง', red: 'ล่าช้า', gray: 'ยังไม่เริ่ม' }

/** "2569-10" → "ต.ค. 69" */
function monthLabel(month: string): string {
  const [year, m] = month.split('-').map(Number)
  if (!year || !m) return month
  return new Intl.DateTimeFormat('th-TH', { month: 'short', year: '2-digit' }).format(new Date(Date.UTC(year, m - 1, 15)))
}

export default function DashboardPage() {
  const user = useAuthStore((s) => s.user)
  const navigate = useNavigate()
  const [data, setData] = useState<DashboardDto | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const load = () => dashboardService.get()
    .then((d) => { setData(d); setError('') })
    .catch((err) => setError(apiError(err, 'โหลดแดชบอร์ดไม่สำเร็จ')))

  useEffect(() => { load().finally(() => setLoading(false)) }, [])

  // Another user changed a KPI: refresh the numbers without a page reload.
  useKpiEvents(() => { load() })

  if (loading) return <Page wide><PageSkeleton /></Page>

  return (
    <Page wide>
      <PageHeader title="แดชบอร์ด" subtitle={`สวัสดี ${user?.fullName ?? ''} — ภาพรวม KPI ของ ${user?.departmentName ?? 'องค์กร'}`} />

      {error && <div className="mb-4"><Alert tone="error">{error}</Alert></div>}

      {data && (
        <div className="space-y-6">
          <section aria-label="ตัวเลขสำคัญ" className="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
            <div className="col-span-2 lg:col-span-1">
              <Stat dark label="ความคืบหน้ารวม" value={`${formatNumber(data.overallProgress, 1)}%`} icon="chart-donut-3"
                hint={<Progress value={data.overallProgress} className="mt-1 !bg-white/15" />} />
            </div>
            <Stat label="KPI ทั้งหมด" value={data.totalKpis} icon="target-arrow" onClick={() => navigate('/kpi')} />
            <Stat label="กำลังดำเนินการ" value={data.activeKpis} icon="player-play" tone="green"
              hint={`ตามเป้า ${data.onTrackKpis} รายการ`} onClick={() => navigate('/kpi')} />
            <Stat label="ล่าช้า" value={data.delayedKpis} icon="alarm" tone={data.delayedKpis ? 'red' : 'default'}
              hint={data.delayedKpis ? 'ต้องติดตามด่วน' : 'ไม่มี KPI ล่าช้า'} onClick={() => navigate('/kpi')} />
          </section>

          <div className="grid gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader eyebrow="เปรียบเทียบ" title="ความคืบหน้าแยกตามแผนก" />
              <div className="px-3 pb-4 pt-3">
                {data.departmentRankings.length === 0 ? (
                  <EmptyState icon="building" title="ยังไม่มีข้อมูล" />
                ) : (
                  <>
                    <div style={{ height: Math.max(160, data.departmentRankings.length * 44) }}>
                      <ResponsiveContainer width="100%" height="100%">
                        <BarChart data={data.departmentRankings} layout="vertical" margin={{ left: 0, right: 28, top: 4, bottom: 4 }}>
                          <CartesianGrid stroke="#e4e6ea" horizontal={false} />
                          <XAxis type="number" domain={[0, 100]} tickLine={false} axisLine={false} tick={{ fontSize: 11, fill: '#5f656d' }} />
                          <YAxis type="category" dataKey="departmentName" width={120} tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: '#0e0f11' }} />
                          <Tooltip cursor={{ fill: '#f5f6f8' }} formatter={(v) => [`${v}%`, 'ความคืบหน้าเฉลี่ย']} />
                          <Bar dataKey="averageProgress" radius={[0, 6, 6, 0]} barSize={16}
                            label={{ position: 'right', fontSize: 12, fill: '#0e0f11', formatter: (v: unknown) => `${v}%` }}>
                            {data.departmentRankings.map((d) => <Cell key={d.departmentName} fill={lightColor[d.trafficLight] ?? '#bcc0c7'} />)}
                          </Bar>
                        </BarChart>
                      </ResponsiveContainer>
                    </div>
                    <p className="px-3 pt-1 text-xs text-ink-3">
                      สี: <span className="text-green-ink">เขียว ≥ 70%</span> · <span className="text-amber-ink">เหลือง 40–69%</span> · <span className="text-red-ink">แดง &lt; 40%</span>
                    </p>
                  </>
                )}
              </div>
            </Card>

            <Card>
              <CardHeader eyebrow="แนวโน้ม" title="ความคืบหน้าเฉลี่ยรายเดือน" />
              <div className="h-60 px-2 pb-3 pt-4">
                {data.monthlyTrend.length === 0 ? (
                  <EmptyState icon="chart-line" title="ยังไม่มีข้อมูล">เมื่อมีการอัปเดตความคืบหน้า กราฟจะแสดงที่นี่</EmptyState>
                ) : (
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={data.monthlyTrend.map((m) => ({ ...m, label: monthLabel(m.month) }))} margin={{ left: 0, right: 16, top: 8 }}>
                      <CartesianGrid stroke="#e4e6ea" vertical={false} />
                      <XAxis dataKey="label" tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: '#5f656d' }} />
                      <YAxis domain={[0, 100]} tickLine={false} axisLine={false} width={34} tick={{ fontSize: 12, fill: '#5f656d' }} />
                      <Tooltip formatter={(v) => [`${v}%`, 'ความคืบหน้าเฉลี่ย']} />
                      <Area type="monotone" dataKey="averageProgress" stroke="#ff5a1f" strokeWidth={2.5} fill="#fff0ea"
                        dot={{ r: 3, fill: '#ff5a1f', strokeWidth: 0 }} />
                    </AreaChart>
                  </ResponsiveContainer>
                )}
              </div>
            </Card>
          </div>

          <Card>
            <CardHeader eyebrow="ล่าสุด" title="KPI ที่อัปเดตล่าสุด"
              action={<Link to="/kpi" className="text-[13px] font-medium text-accent hover:underline">ดูทั้งหมด</Link>} />
            {data.recentKpis.length === 0 ? (
              <EmptyState icon="target-arrow" title="ยังไม่มี KPI" />
            ) : (
              <ul className="divide-y divide-line px-2 pb-2 pt-2">
                {data.recentKpis.map((kpi) => (
                  <li key={kpi.id}>
                    <Link to={`/kpi/${kpi.id}`}
                      className="grid grid-cols-[1fr_auto] items-center gap-x-4 gap-y-2 rounded-control px-3 py-3 hover:bg-surface sm:grid-cols-[minmax(0,1fr)_9rem_8.5rem_3rem]">
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium">{kpi.name}</p>
                        <p className="truncate text-xs text-ink-3">{kpi.departmentName}</p>
                      </div>
                      <TrafficBadge light={kpi.trafficLight} label={lightLabel[kpi.trafficLight] ?? kpi.statusLabel} />
                      <Progress value={kpi.progress} color={kpi.trafficLight} label={`ความคืบหน้า ${kpi.name}`}
                        className="order-last col-span-2 sm:order-none sm:col-span-1" />
                      <span className="hidden text-right text-sm font-medium tabular sm:block">{kpi.progress}%</span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
    </Page>
  )
}
