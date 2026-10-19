import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card, CardHeader, Stat } from '../../components/ui/Card'
import { Alert, Avatar, EmptyState, Page, PageHeader, PageSkeleton, Segmented } from '../../components/ui/Layout'
import { adminService, type CountByLabel, type Overview } from '../../services/admin.service'
import { apiError } from '../../utils/errors'
import { formatDateTime, formatNumber, formatRelative, formatShortDate, toDate } from '../../utils/format'
import { auditActionLabel, issueStatusLabel, moduleShort, roleLabel } from '../../utils/labels'

const INK = 'var(--lt-text)'
const ACCENT = 'var(--lt-accent)'
const MUTED = 'var(--lt-n400)'

const issueStatusByName: Record<string, string> = Object.fromEntries(
  ['New', 'Assigned', 'InProgress', 'WaitingForReporter', 'Resolved', 'Closed', 'Rejected', 'Reopened']
    .map((name, i) => [name, issueStatusLabel[i]]),
)
const moduleByName: Record<string, string> = Object.fromEntries(
  ['Application', 'Login', 'Dashboard', 'Export', 'Users', 'Issues', 'ItGeneral', 'Other'].map((name, i) => [name, moduleShort[i]]),
)

function BarList({ rows, labels, color = INK }: { rows: CountByLabel[]; labels?: Record<string, string>; color?: string }) {
  if (rows.length === 0) return <p className="py-6 text-center text-sm text-ink-3">ยังไม่มีข้อมูล</p>
  const data = rows.map((r) => ({ name: labels?.[r.label] ?? r.label, count: r.count }))
  return (
    <div style={{ height: Math.max(120, data.length * 34) }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ left: 0, right: 16, top: 4, bottom: 4 }}>
          <XAxis type="number" hide allowDecimals={false} />
          <YAxis type="category" dataKey="name" width={118} tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: 'var(--lt-n600)' }} />
          <Tooltip contentStyle={{ background: 'var(--lt-surface)', border: '1px solid var(--lt-n200)', borderRadius: 6, color: 'var(--lt-text)', fontSize: 12.5 }} cursor={{ fill: 'var(--lt-n100)' }} formatter={(v) => [formatNumber(Number(v)), 'จำนวน']} />
          <Bar dataKey="count" radius={[0, 6, 6, 0]} barSize={14} label={{ position: 'right', fontSize: 12, fill: INK }}>
            {data.map((_, i) => <Cell key={i} fill={i === 0 ? color : MUTED} />)}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

export default function OverviewPage() {
  const [days, setDays] = useState(7)
  const [data, setData] = useState<Overview | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    adminService.overview(days)
      .then((d) => { setData(d); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดภาพรวมไม่สำเร็จ')))
  }, [days])

  useEffect(() => { load() }, [load])

  // Keep "who is online" and the activity feed fresh without a manual refresh.
  useEffect(() => {
    const id = window.setInterval(load, 30000)
    return () => window.clearInterval(id)
  }, [load])

  // Nothing yet, or still showing another period's numbers: show the skeleton instead of stale data.
  if (!data || data.days !== days) {
    return error ? <Page wide><Alert tone="error">{error}</Alert></Page> : <Page wide><PageSkeleton /></Page>
  }

  return (
    <Page wide>
      <PageHeader
        title="ภาพรวมระบบ"
        subtitle={data ? `ข้อมูล ${days} วันล่าสุด · อัปเดต ${formatRelative(data.generatedAt)}` : undefined}
        actions={
          <>
            <Segmented<number> label="ช่วงเวลา" value={days} onChange={setDays}
              options={[{ value: 1, label: '24 ชม.' }, { value: 7, label: '7 วัน' }, { value: 30, label: '30 วัน' }]} />
            <Button icon="refresh" onClick={load}>รีเฟรช</Button>
          </>
        } />

      {error && <div className="mb-4"><Alert tone="error">{error}</Alert></div>}

      {data && (
        <div className="space-y-6">
          <section aria-label="ตัวเลขสำคัญ" className="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
            <Stat dark label="ออนไลน์ตอนนี้" value={data.online.length} icon="wifi"
              hint={`จากผู้ใช้ที่ใช้งานได้ ${data.users.active} คน`} />
            <Stat label="คำขอที่บันทึก" value={formatNumber(data.requests.total)} icon="arrows-exchange"
              hint={`แก้ไขข้อมูล ${formatNumber(data.requests.writes)} ครั้ง · จาก ${data.requests.distinctIps} IP`} />
            <Stat label="เข้าสู่ระบบไม่สำเร็จ" value={formatNumber(data.requests.failedLogins)} icon="lock-exclamation"
              tone={data.requests.failedLogins > 0 ? 'red' : 'default'}
              hint={data.requests.failedLogins > 0 ? 'ตรวจสอบรายการด้านล่าง' : 'ไม่พบความผิดปกติ'} />
            <Stat label="ถูกปฏิเสธสิทธิ์ / ผิดพลาด" value={formatNumber(data.requests.forbidden + data.requests.unauthorized + data.requests.errors)}
              icon="shield-x" tone={data.requests.errors > 0 ? 'amber' : 'default'}
              hint={`403: ${data.requests.forbidden} · 401: ${data.requests.unauthorized} · 5xx: ${data.requests.errors}`} />
          </section>

          <div className="grid gap-6 lg:grid-cols-[1fr_22rem]">
            <Card>
              <CardHeader eyebrow="การใช้งาน" title="คำขอต่อวัน"
                action={<Link to="/admin/audit?tab=requests" className="text-[13px] font-medium text-accent hover:underline">ดูทั้งหมด</Link>} />
              <div className="h-64 px-2 pb-3 pt-4">
                {data.trend.length === 0
                  ? <EmptyState icon="chart-area" title="ยังไม่มีข้อมูล">ระบบเริ่มบันทึกคำขอที่แก้ไขข้อมูลและที่ถูกปฏิเสธแล้ว</EmptyState>
                  : (
                    <ResponsiveContainer width="100%" height="100%">
                      <AreaChart data={data.trend.map((t) => ({ ...t, label: formatShortDate(t.day + 'T00:00:00Z') }))} margin={{ left: 4, right: 12 }}>
                        <CartesianGrid stroke="var(--lt-n200)" vertical={false} />
                        <XAxis dataKey="label" tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: 'var(--lt-n600)' }} />
                        <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={32} tick={{ fontSize: 12, fill: 'var(--lt-n600)' }} />
                        <Tooltip />
                        <Area type="monotone" dataKey="requests" name="คำขอ" stroke={ACCENT} fill="var(--lt-a200)" fillOpacity={0.55} strokeWidth={2} />
                        <Area type="monotone" dataKey="failures" name="ล้มเหลว (4xx/5xx)" stroke={INK} fill="transparent" strokeWidth={1.5} strokeDasharray="4 3" />
                      </AreaChart>
                    </ResponsiveContainer>
                  )}
              </div>
            </Card>

            <Card>
              <CardHeader eyebrow="ตอนนี้" title={`ผู้ใช้ออนไลน์ (${data.online.length})`} />
              {data.online.length === 0
                ? <EmptyState icon="user-off" title="ไม่มีใครออนไลน์">แสดงเฉพาะผู้ที่เปิดหน้าเว็บไว้ในขณะนี้</EmptyState>
                : (
                  <ul className="max-h-64 divide-y divide-line overflow-y-auto px-5 pb-2 pt-2">
                    {data.online.map((u) => (
                      <li key={u.userId} className="flex items-center gap-3 py-2.5">
                        <Avatar name={u.name} />
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm font-medium">{u.name}</p>
                          <p className="truncate text-xs text-ink-3">{roleLabel[u.role] ?? u.role} · {u.ipAddress ?? 'ไม่ทราบ IP'}</p>
                        </div>
                        <span className="h-2 w-2 shrink-0 rounded-full bg-green-dot" aria-label="ออนไลน์" />
                      </li>
                    ))}
                  </ul>
                )}
            </Card>
          </div>

          <section aria-label="งานแจ้งปัญหา" className="space-y-4">
            <h2 className="text-lg font-semibold">งานแจ้งปัญหา</h2>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
              <Stat label="ทั้งหมด" value={data.issues.total} icon="inbox" />
              <Stat label="เปิดอยู่" value={data.issues.open} icon="lifebuoy" tone={data.issues.open ? 'accent' : 'default'} />
              <Stat label="เกินกำหนด" value={data.issues.overdue} icon="alarm" tone={data.issues.overdue ? 'red' : 'default'} />
              <Stat label="เวลาแก้เฉลี่ย" value={data.issues.averageResolveHours == null ? '–' : `${formatNumber(data.issues.averageResolveHours, 1)} ชม.`}
                icon="hourglass" hint={`แก้ไขแล้ว ${data.issues.resolvedInPeriod} เรื่องในช่วงนี้`} />
            </div>
            <div className="grid gap-6 lg:grid-cols-3">
              <Card><CardHeader eyebrow="ตามสถานะ" title="สถานะของเรื่อง" />
                <div className="px-3 pb-3 pt-2"><BarList rows={data.issues.byStatus} labels={issueStatusByName} color={ACCENT} /></div></Card>
              <Card><CardHeader eyebrow="ตามส่วนของระบบ" title="ปัญหาเกิดที่ไหนบ่อย" />
                <div className="px-3 pb-3 pt-2"><BarList rows={data.issues.byModule} labels={moduleByName} /></div></Card>
              <Card><CardHeader eyebrow="ตามแผนก" title="แผนกที่แจ้งมากที่สุด" />
                <div className="px-3 pb-3 pt-2"><BarList rows={data.issues.byDepartment} /></div></Card>
            </div>
          </section>

          <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
            <Card>
              <CardHeader eyebrow="ความปลอดภัย" title="เหตุการณ์ที่ควรดู"
                action={<Link to="/admin/audit?tab=requests&minStatus=400" className="text-[13px] font-medium text-accent hover:underline">ดูทั้งหมด</Link>} />
              {data.securityEvents.length === 0
                ? <EmptyState icon="shield-check" title="ไม่พบเหตุการณ์ผิดปกติ">ไม่มีการเข้าสู่ระบบล้มเหลวหรือถูกปฏิเสธสิทธิ์ในช่วงนี้</EmptyState>
                : (
                  <div className="overflow-x-auto px-5 pb-4 pt-3">
                    <table className="w-full min-w-[560px] text-left text-[13px]">
                      <thead className="text-xs font-semibold text-ink-3">
                        <tr><th className="py-2 pr-3">เวลา</th><th className="py-2 pr-3">ผู้ใช้</th>
                          <th className="py-2 pr-3">คำขอ</th><th className="py-2 pr-3">ผล</th><th className="py-2">IP</th></tr>
                      </thead>
                      <tbody className="divide-y divide-line">
                        {data.securityEvents.map((e) => (
                          <tr key={e.id}>
                            <td className="whitespace-nowrap py-2.5 pr-3 tabular text-ink-2">{formatDateTime(e.createdAt)}</td>
                            <td className="max-w-[10rem] truncate py-2.5 pr-3">{e.userEmail ?? <span className="text-ink-3">ไม่ทราบ</span>}</td>
                            <td className="py-2.5 pr-3"><span className="font-medium">{e.method}</span> <span className="break-all text-ink-2">{e.path}</span></td>
                            <td className="py-2.5 pr-3"><Badge tone={e.statusCode >= 500 ? 'red' : 'amber'}>{e.statusCode}</Badge></td>
                            <td className="whitespace-nowrap py-2.5 tabular text-ink-2">{e.ipAddress ?? '–'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
            </Card>

            <Card>
              <CardHeader eyebrow="เครื่องที่ใช้งานมากสุด" title="IP ที่พบบ่อย" />
              {data.topIps.length === 0
                ? <p className="px-5 py-8 text-center text-sm text-ink-3">ยังไม่มีข้อมูล</p>
                : (
                  <ul className="space-y-2 px-5 pb-5 pt-3">
                    {data.topIps.map((ip, i) => {
                      const max = data.topIps[0].count
                      return (
                        <li key={ip.label}>
                          <Link to={`/admin/audit?tab=requests&ip=${encodeURIComponent(ip.label)}`}
                            className="flex items-center justify-between text-[13px] hover:text-accent">
                            <span className="tabular">{ip.label}</span><span className="tabular text-ink-2">{formatNumber(ip.count)}</span>
                          </Link>
                          <div className="mt-1 h-1.5 rounded-full bg-surface-2">
                            <div className="h-full rounded-full" style={{ width: `${(ip.count / max) * 100}%`, background: i === 0 ? ACCENT : MUTED }} />
                          </div>
                        </li>
                      )
                    })}
                  </ul>
                )}
            </Card>
          </div>

          <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
            <Card>
              <CardHeader eyebrow="ล่าสุด" title="ความเคลื่อนไหวทั้งระบบ"
                action={<Link to="/admin/audit" className="text-[13px] font-medium text-accent hover:underline">ดูบันทึกทั้งหมด</Link>} />
              {data.activity.length === 0
                ? <EmptyState icon="history" title="ยังไม่มีความเคลื่อนไหว" />
                : (
                  <ul className="divide-y divide-line px-5 pb-2 pt-1">
                    {data.activity.map((a) => (
                      <li key={a.id} className="flex items-start gap-3 py-3">
                        <Avatar name={a.userName ?? a.userEmail ?? 'ระบบ'} />
                        <div className="min-w-0 flex-1 text-[13px]">
                          <p>
                            <span className="font-medium">{a.userName ?? a.userEmail ?? 'ระบบ'}</span>
                            {a.userRole && <span className="text-ink-3"> · {roleLabel[a.userRole] ?? a.userRole}</span>}
                            {a.departmentName && <span className="text-ink-3"> · {a.departmentName}</span>}
                          </p>
                          <p className="text-ink-2">
                            {auditActionLabel[a.action] ?? a.action} <Badge tone="gray">{a.entityName === 'Issue' ? 'แจ้งปัญหา' : a.entityName}</Badge>
                            {a.newValues && <span className="ml-1 break-words text-ink-3">{a.newValues.length > 90 ? `${a.newValues.slice(0, 90)}…` : a.newValues}</span>}
                          </p>
                          <p className="text-xs text-ink-3">
                            {formatRelative(a.createdAt)}{a.ipAddress ? ` · IP ${a.ipAddress}` : ''}
                          </p>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
            </Card>

            <div className="space-y-6">
              <Card>
                <CardHeader eyebrow="ผู้ใช้" title={`${data.users.total} บัญชี`} />
                <div className="px-3 pb-3 pt-2"><BarList rows={data.users.byRole} labels={roleLabel} /></div>
                <p className="px-5 pb-5 text-xs text-ink-3">ปิดใช้งานแล้ว {data.users.inactive} บัญชี</p>
              </Card>
            </div>
          </div>

          <p className="text-center text-xs text-ink-3">
            สร้างเมื่อ {formatDateTime(toDate(data.generatedAt))} · แสดงเฉพาะผู้ดูแลระบบ
          </p>
        </div>
      )}
    </Page>
  )
}
