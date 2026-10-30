import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { StarsRead } from '../../components/issues/FeedbackCard'
import Button from '../../components/ui/Button'
import { Card, CardHeader, Stat } from '../../components/ui/Card'
import { TextInput } from '../../components/ui/Field'
import { Alert, EmptyState, Page, PageHeader, PageSkeleton, Pagination } from '../../components/ui/Layout'
import { issueService } from '../../services/issue.service'
import { useAuthStore } from '../../stores/authStore'
import type { FeedbackReport, MyFeedbackItem } from '../../types/issue'
import { apiError } from '../../utils/errors'
import { dayEndIso, dayStartIso, formatDateTime, formatNumber } from '../../utils/format'
import { feedbackLabel } from '../../utils/labels'
import { usePageSize } from '../../utils/paging'

/** IT reads every rating it may see; everyone else sees their own finished issues and which of them still await a rating. */
export default function FeedbackPage() {
  const isIt = useAuthStore((s) => s.user?.permissions)?.includes('issue.manage') ?? false
  return isIt ? <Report /> : <Mine />
}

function Mine() {
  const [items, setItems] = useState<MyFeedbackItem[] | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    issueService.myFeedback().then(setItems).catch((err) => setError(apiError(err, 'โหลดไม่สำเร็จ')))
  }, [])

  if (!items && !error) return <Page><PageSkeleton /></Page>
  const waiting = (items ?? []).filter((i) => i.canRate)

  return (
    <Page>
      <PageHeader title="ความพึงพอใจของฉัน" subtitle="เรื่องที่ทีม IT แก้ไขให้แล้ว ให้คะแนนได้ภายใน 30 วันหลังแก้ไขเสร็จ" />
      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}
      {waiting.length > 0 && (
        <div className="mb-4"><Alert tone="info">มี {waiting.length} เรื่องที่รอคะแนนจากคุณ</Alert></div>
      )}
      <Card>
        {items && items.length === 0 ? (
          <EmptyState icon="mood-smile" title="ยังไม่มีเรื่องที่ให้คะแนนได้">เมื่อทีม IT แก้ไขเรื่องที่คุณแจ้งเสร็จ จะมาปรากฏที่นี่</EmptyState>
        ) : (
          <ul className="divide-y divide-line">
            {(items ?? []).map((item) => (
              <li key={item.issueId}>
                <Link to={`/issues/${item.issueId}`} className="flex flex-wrap items-center justify-between gap-3 px-5 py-4 hover:bg-surface">
                  <div className="min-w-0">
                    <p className="text-xs text-ink-3 tabular">{item.number}</p>
                    <p className="truncate text-[14px] font-medium text-ink">{item.title}</p>
                    {item.note && <p className="mt-0.5 line-clamp-2 break-words text-[13px] text-ink-2">{item.note}</p>}
                  </div>
                  {item.rating != null && !item.canRate ? (
                    <p className="flex items-center gap-2 text-sm">
                      <StarsRead rating={item.rating} className="text-base" />
                      <span className="text-ink-2">{feedbackLabel[item.rating]}</span>
                    </p>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-full bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-deep">
                      <i className="ti ti-star" aria-hidden="true" />ให้คะแนน
                    </span>
                  )}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </Page>
  )
}

function Report() {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = usePageSize()
  const [report, setReport] = useState<FeedbackReport | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    issueService.feedbackReport({
      from: from ? dayStartIso(from) : undefined,
      to: to ? dayEndIso(to) : undefined,
      page, pageSize,
    })
      .then((data) => { setReport(data); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดรายงานไม่สำเร็จ')))
  }, [from, to, page, pageSize])

  useEffect(() => { load() }, [load])

  if (!report && !error) return <Page><PageSkeleton /></Page>
  const biggest = Math.max(1, ...(report?.distribution ?? []))

  return (
    <Page>
      <PageHeader title="ความพึงพอใจ" subtitle="คะแนนที่ผู้แจ้งให้หลังทีม IT แก้ไขเรื่องเสร็จ (เห็นเฉพาะเรื่องที่บทบาทของคุณเปิดดูได้)" />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <TextInput label="ตั้งแต่วันที่" type="date" value={from} max={to || undefined} onChange={(e) => { setFrom(e.target.value); setPage(1) }} />
        <TextInput label="ถึงวันที่" type="date" value={to} min={from || undefined} onChange={(e) => { setTo(e.target.value); setPage(1) }} />
        {(from || to) && <Button icon="x" onClick={() => { setFrom(''); setTo(''); setPage(1) }}>ล้างช่วงวันที่</Button>}
      </div>

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      {report && (
        <>
          <section aria-label="สรุปคะแนน" className="mb-6 grid grid-cols-2 gap-3 lg:gap-4">
            <Stat dark label="คะแนนเฉลี่ย" icon="mood-smile" value={report.average == null ? '–' : `${formatNumber(report.average, 2)} / 5`}
              hint={report.average == null ? 'ยังไม่มีผู้ให้คะแนน' : undefined} />
            <Stat label="จำนวนที่ให้คะแนน" icon="users" value={formatNumber(report.count)} hint="เรื่อง" />
          </section>

          {report.count === 0 ? (
            <Card><EmptyState icon="mood-empty" title="ยังไม่มีคะแนนในช่วงนี้" /></Card>
          ) : (
            <div className="space-y-6">
              <div className="grid gap-6 lg:grid-cols-2">
                <Card>
                  <CardHeader eyebrow="การกระจาย" title="จำนวนตามคะแนน" />
                  <ul className="space-y-2.5 px-5 pb-5 pt-4">
                    {[5, 4, 3, 2, 1].map((score) => {
                      const n = report.distribution[score - 1] ?? 0
                      return (
                        <li key={score} className="flex items-center gap-3 text-[13px]">
                          <span className="w-24 shrink-0 text-ink-2">{score} ดาว · {feedbackLabel[score]}</span>
                          <span className="h-2.5 min-w-0 flex-1 overflow-hidden rounded-full bg-surface-2" aria-hidden="true">
                            <span className="block h-full rounded-full bg-amber-dot" style={{ width: `${(n / biggest) * 100}%` }} />
                          </span>
                          <span className="w-8 shrink-0 text-right font-medium tabular text-ink">{n}</span>
                        </li>
                      )
                    })}
                  </ul>
                </Card>

                <Card>
                  <CardHeader eyebrow="รายบุคคล" title="ตามผู้รับผิดชอบ" />
                  <ul className="divide-y divide-line px-5 pb-2 pt-1">
                    {report.byAssignee.map((a) => (
                      <li key={a.assigneeId ?? 'none'} className="flex items-center justify-between gap-3 py-3 text-[13px]">
                        <span className="min-w-0 truncate font-medium text-ink">{a.assigneeName}</span>
                        <span className="flex shrink-0 items-center gap-2 text-ink-2">
                          <StarsRead rating={Math.round(a.average)} />
                          <span className="tabular font-medium text-ink">{formatNumber(a.average, 2)}</span>
                          <span className="tabular">({a.count} เรื่อง)</span>
                        </span>
                      </li>
                    ))}
                  </ul>
                </Card>
              </div>

              <Card>
                <CardHeader eyebrow="ล่าสุด" title="คะแนนและความเห็น" />
                <ul className="divide-y divide-line">
                  {report.items.items.map((item) => (
                    <li key={item.issueId}>
                      <Link to={`/issues/${item.issueId}`} className="block px-5 py-4 hover:bg-surface">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="min-w-0 truncate text-[14px] font-medium text-ink">
                            <span className="mr-2 text-xs font-normal text-ink-3 tabular">{item.number}</span>{item.title}
                          </p>
                          <StarsRead rating={item.rating} className="text-base" />
                        </div>
                        {item.note && <p className="mt-1 whitespace-pre-wrap break-words text-[13px] text-ink-2">{item.note}</p>}
                        <p className="mt-1 text-xs text-ink-3">
                          {item.reporterName} · ผู้รับผิดชอบ {item.assigneeName ?? 'ไม่ระบุ'} · {formatDateTime(item.ratedAt)}
                        </p>
                      </Link>
                    </li>
                  ))}
                </ul>
                <Pagination page={page} pageSize={pageSize} total={report.items.total} onChange={setPage}
                  onPageSizeChange={(size) => { setPageSize(size); setPage(1) }} />
              </Card>
            </div>
          )}
        </>
      )}
    </Page>
  )
}
