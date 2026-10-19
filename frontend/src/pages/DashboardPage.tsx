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

/** Chart colours are CSS variables, so the charts follow the theme and the accent. */
const lightColor: Record<string, string> = {
  green: 'var(--tone-ok-solid)', yellow: 'var(--tone-warn-solid)', red: 'var(--tone-stop-solid)', gray: 'var(--lt-n400)',
}
const axisTick = { fontSize: 12, fill: 'var(--lt-n600)' }
const gridStroke = 'var(--lt-n200)'
const tooltipStyle = {
  contentStyle: { background: 'var(--lt-surface)', border: '1px solid var(--lt-n200)', borderRadius: 6, color: 'var(--lt-text)', fontSize: 12.5, boxShadow: 'var(--shadow-md)' },
  labelStyle: { color: 'var(--lt-text)', fontWeight: 600 },
  itemStyle: { color: 'var(--lt-n700)' },
}

/** A ring showing one percentage, with the number in the middle. */
function ArcGauge({ value, label }: { value: number; label: string }) {
  const r = 46
  const c = 2 * Math.PI * r
  const pct = Math.max(0, Math.min(100, value))
  return (
    <div className="relative h-[132px] w-[132px] flex-none" role="img" aria-label={`${label} ${formatNumber(value, 1)}%`}>
      <svg viewBox="0 0 120 120" className="h-full w-full -rotate-90">
        <circle cx="60" cy="60" r={r} fill="none" stroke="var(--lt-n200)" strokeWidth="11" />
        <circle cx="60" cy="60" r={r} fill="none" stroke="var(--lt-accent)" strokeWidth="11" strokeLinecap="round"
          strokeDasharray={`${(pct / 100) * c} ${c}`} style={{ transition: 'stroke-dasharray 0.6s ease' }} />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="tabular text-[26px] font-semibold leading-none text-ink">{formatNumber(value, 1)}%</span>
        <span className="mt-1 text-[11px] text-ink-3">{label}</span>
      </div>
    </div>
  )
}

/** A small number with a thin bar showing its share of the whole (the welcome card). */
function MiniStat({ label, value, share, color }: { label: string; value: number; share: number; color: string }) {
  return (
    <div className="min-w-[110px]">
      <p className="text-xs font-semibold text-ink-3">{label}</p>
      <p className="tabular mt-0.5 text-2xl font-semibold leading-tight text-ink">{formatNumber(value)}</p>
      <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-surface-2" aria-hidden="true">
        <div className="h-full rounded-full" style={{ width: `${Math.round(Math.max(0, Math.min(1, share)) * 100)}%`, background: color }} />
      </div>
    </div>
  )
}
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
          <section aria-label="ตัวเลขสำคัญ" className="grid gap-4 lg:grid-cols-4">
            <Card className="flex flex-wrap items-center justify-between gap-5 px-6 py-5 lg:col-span-2"
              style={{ background: 'linear-gradient(135deg, var(--lt-a100), var(--lt-surface) 70%)' }}>
              <div className="flex min-w-0 flex-1 basis-56 flex-col justify-between gap-5">
                <div>
                  <p className="text-xl font-semibold text-ink">สวัสดี {user?.fullName ?? ''}</p>
                  <p className="mt-1.5 max-w-sm text-[13.5px] text-ink-3">
                    ภาพรวม KPI ของ {user?.departmentName ?? 'องค์กร'} — {data.delayedKpis > 0
                      ? `มี ${data.delayedKpis} รายการที่ล่าช้า ควรติดตามก่อน`
                      : 'ตอนนี้ไม่มี KPI ที่ล่าช้า'}
                  </p>
                </div>
                <div className="flex flex-wrap gap-6">
                  <MiniStat label="กำลังดำเนินการ" value={data.activeKpis}
                    share={data.totalKpis ? data.activeKpis / data.totalKpis : 0} color="var(--lt-accent)" />
                  <MiniStat label="ตามเป้า" value={data.onTrackKpis}
                    share={data.activeKpis ? data.onTrackKpis / data.activeKpis : 0} color="var(--tone-ok-solid)" />
                </div>
              </div>
              <ArcGauge value={data.overallProgress} label="ความคืบหน้ารวม" />
            </Card>
            <Stat label="KPI ทั้งหมด" value={data.totalKpis} icon="target-arrow"
              hint="ดูรายการทั้งหมด" onClick={() => navigate('/kpi')} />
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
                          <CartesianGrid stroke={gridStroke} horizontal={false} />
                          <XAxis type="number" domain={[0, 100]} tickLine={false} axisLine={false} tick={{ ...axisTick, fontSize: 11 }} />
                          <YAxis type="category" dataKey="departmentName" width={120} tickLine={false} axisLine={false} tick={axisTick} />
                          <Tooltip {...tooltipStyle} cursor={{ fill: 'var(--lt-n100)' }} formatter={(v) => [`${v}%`, 'ความคืบหน้าเฉลี่ย']} />
                          <Bar dataKey="averageProgress" radius={[0, 6, 6, 0]} barSize={16}
                            label={{ position: 'right', fontSize: 12, fill: 'var(--lt-text)', formatter: (v: unknown) => `${v}%` }}>
                            {data.departmentRankings.map((d) => <Cell key={d.departmentName} fill={lightColor[d.trafficLight] ?? 'var(--lt-n400)'} />)}
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
                      <CartesianGrid stroke={gridStroke} vertical={false} />
                      <XAxis dataKey="label" tickLine={false} axisLine={false} tick={axisTick} />
                      <YAxis domain={[0, 100]} tickLine={false} axisLine={false} width={34} tick={axisTick} />
                      <Tooltip {...tooltipStyle} formatter={(v) => [`${v}%`, 'ความคืบหน้าเฉลี่ย']} />
                      <Area type="monotone" dataKey="averageProgress" stroke="var(--lt-accent)" strokeWidth={2.5} fill="var(--lt-a200)" fillOpacity={0.55}
                        dot={{ r: 3, fill: 'var(--lt-accent)', strokeWidth: 0 }} />
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
