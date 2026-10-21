import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import Button from '../components/ui/Button'
import { Card, CardHeader, Stat } from '../components/ui/Card'
import { Alert, EmptyState, Page, PageHeader, PageSkeleton } from '../components/ui/Layout'
import { DueLabel, PriorityBadge, StatusBadge } from '../components/issues/IssueBadges'
import { useIssueEvents } from '../components/realtimeContext'
import { useAuthStore, useCan } from '../stores/authStore'
import { issueService } from '../services/issue.service'
import type { IssueListItem, IssueSummary } from '../types/issue'
import { apiError } from '../utils/errors'
import { formatNumber, formatRelative } from '../utils/format'

/** A ring showing one percentage, with the number in the middle. */
function ArcGauge({ value, label }: { value: number; label: string }) {
  const r = 46
  const c = 2 * Math.PI * r
  const pct = Math.max(0, Math.min(100, value))
  return (
    <div className="relative h-[132px] w-[132px] flex-none" role="img" aria-label={`${label} ${formatNumber(value, 0)}%`}>
      <svg viewBox="0 0 120 120" className="h-full w-full -rotate-90">
        <circle cx="60" cy="60" r={r} fill="none" stroke="var(--lt-n200)" strokeWidth="11" />
        <circle cx="60" cy="60" r={r} fill="none" stroke="var(--lt-accent)" strokeWidth="11" strokeLinecap="round"
          strokeDasharray={`${(pct / 100) * c} ${c}`} style={{ transition: 'stroke-dasharray 0.6s ease' }} />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="tabular text-[26px] font-semibold leading-none text-ink">{formatNumber(value, 0)}%</span>
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

/**
 * The first page after sign-in. People who work the queue see what to pick up next; everyone else sees where their own
 * requests stand. Both have one-click ways in: report a problem, open the queue, jump to what is overdue.
 */
export default function DashboardPage() {
  const user = useAuthStore((s) => s.user)
  const navigate = useNavigate()
  const isAgent = useCan('issue.manage')
  const canReport = useCan('issue.create')
  const [summary, setSummary] = useState<IssueSummary | null>(null)
  const [recent, setRecent] = useState<IssueListItem[] | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => Promise.all([
    issueService.summary(),
    // Agents: what is due soonest. Everyone else: their own requests, newest activity first.
    issueService.list({ openOnly: true, sort: isAgent ? 'due' : 'updated', pageSize: 8 }),
  ])
    .then(([s, list]) => { setSummary(s); setRecent(list.items); setError('') })
    .catch((err) => setError(apiError(err, 'โหลดแดชบอร์ดไม่สำเร็จ'))), [isAgent])

  useEffect(() => { load().finally(() => setLoading(false)) }, [load])

  // Somebody reported, answered or closed something: refresh the numbers without a page reload.
  useIssueEvents(() => { load() })

  if (loading) return <Page wide><PageSkeleton /></Page>

  const open = summary?.open ?? 0
  const overdue = summary?.overdue ?? 0
  const onTime = open === 0 ? 100 : ((open - overdue) / open) * 100
  const greeting = isAgent
    ? overdue > 0
      ? `มี ${overdue} เรื่องเกินกำหนด ควรจัดการก่อน`
      : open > 0 ? `มี ${open} เรื่องที่รอดำเนินการ ยังไม่มีเรื่องเกินกำหนด` : 'ตอนนี้ไม่มีเรื่องค้าง'
    : (summary?.mine ?? 0) > 0
      ? `คุณมี ${summary?.mine} เรื่องที่ยังเปิดอยู่`
      : 'คุณไม่มีเรื่องที่ค้างอยู่ หากมีปัญหากดแจ้งได้เลย'

  return (
    <Page wide>
      <PageHeader title="แดชบอร์ด" subtitle={`สวัสดี ${user?.fullName ?? ''}${user?.departmentName ? ` · ${user.departmentName}` : ''}`}
        actions={canReport && <Button variant="primary" icon="plus" onClick={() => navigate('/issues/new')}>แจ้งปัญหาใหม่</Button>} />

      {error && <div className="mb-4"><Alert tone="error">{error}</Alert></div>}

      {summary && (
        <div className="space-y-6">
          <section aria-label="ภาพรวมเรื่องแจ้ง" className="grid gap-4 lg:grid-cols-4">
            <Card className="flex flex-wrap items-center justify-between gap-5 px-6 py-5 lg:col-span-2"
              style={{ background: 'linear-gradient(135deg, var(--lt-a100), var(--lt-surface) 70%)' }}>
              <div className="flex min-w-0 flex-1 basis-56 flex-col justify-between gap-5">
                <div>
                  <p className="text-xl font-semibold text-ink">{isAgent ? 'ภาพรวมงานวันนี้' : 'เรื่องของคุณ'}</p>
                  <p className="mt-1.5 max-w-sm text-[13.5px] text-ink-3">{greeting}</p>
                </div>
                <div className="flex flex-wrap gap-6">
                  <MiniStat label="ใหม่" value={summary.new} share={open ? summary.new / open : 0} color="var(--lt-accent)" />
                  <MiniStat label="กำลังดำเนินการ" value={summary.inProgress} share={open ? summary.inProgress / open : 0} color="var(--tone-ok-solid)" />
                  <MiniStat label="รอผู้แจ้ง" value={summary.waitingForReporter} share={open ? summary.waitingForReporter / open : 0} color="var(--tone-warn-solid)" />
                </div>
              </div>
              <ArcGauge value={onTime} label="ยังไม่เกินกำหนด" />
            </Card>
            <Stat label={isAgent ? 'เรื่องที่เปิดอยู่' : 'ที่ยังเปิดอยู่'} value={open} icon="inbox" hint="ดูรายการทั้งหมด" onClick={() => navigate('/issues')} />
            <Stat label="เกินกำหนด" value={overdue} icon="alarm" tone={overdue ? 'red' : 'default'}
              hint={overdue ? 'ต้องติดตามด่วน' : 'ไม่มีเรื่องเกินกำหนด'} onClick={() => navigate('/issues?overdue=1')} />
          </section>

          <section aria-label="สถิติ" className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <Stat label="แก้ไขแล้ว 7 วัน" value={summary.resolvedLast7Days} icon="circle-check" tone="green" />
            <Stat label="ของฉัน" value={summary.mine} icon="user" onClick={() => navigate('/issues?assignee=me')} />
            <Stat label="ใหม่ รอตอบรับ" value={summary.new} icon="bell" tone={summary.new ? 'amber' : 'default'} onClick={() => navigate('/issues?status=0')} />
            <Stat label="รอผู้แจ้งตอบ" value={summary.waitingForReporter} icon="hourglass" onClick={() => navigate('/issues?status=3')} />
          </section>

          <Card>
            <CardHeader eyebrow={isAgent ? 'ใกล้ครบกำหนด' : 'ล่าสุด'} title={isAgent ? 'เรื่องที่ควรทำต่อ' : 'เรื่องของฉันที่ยังเปิดอยู่'}
              action={<Link to="/issues" className="text-[13px] font-medium text-accent hover:underline">ดูทั้งหมด</Link>} />
            {!recent || recent.length === 0 ? (
              <EmptyState icon="inbox" title="ยังไม่มีเรื่องที่เปิดอยู่">
                {canReport ? 'เมื่อมีปัญหาการใช้งานหรือคอมพิวเตอร์ กดปุ่ม "แจ้งปัญหาใหม่" ได้ทันที' : 'เมื่อมีเรื่องใหม่จะแสดงที่นี่'}
              </EmptyState>
            ) : (
              <ul className="divide-y divide-line px-2 pb-2 pt-2">
                {recent.map((issue) => (
                  <li key={issue.id}>
                    <Link to={`/issues/${issue.id}`}
                      className="grid grid-cols-[1fr_auto] items-center gap-x-4 gap-y-1.5 rounded-control px-3 py-3 hover:bg-surface sm:grid-cols-[minmax(0,1fr)_7rem_6rem_9rem]">
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-ink">{issue.title}</p>
                        <p className="truncate text-xs text-ink-3">
                          {issue.number} · {issue.reporterName}{issue.departmentName ? ` · ${issue.departmentName}` : ''} · {formatRelative(issue.updatedAt)}
                        </p>
                      </div>
                      <StatusBadge status={issue.status} />
                      <span className="hidden sm:block"><PriorityBadge priority={issue.priority} /></span>
                      <span className="col-span-2 sm:col-span-1 sm:text-right"><DueLabel issue={issue} /></span>
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
