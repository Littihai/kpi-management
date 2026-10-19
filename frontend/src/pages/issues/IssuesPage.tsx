import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { DueLabel, PriorityBadge, StatusBadge } from '../../components/issues/IssueBadges'
import { useIssueEvents } from '../../components/realtimeContext'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card, Stat } from '../../components/ui/Card'
import { CompactSelect } from '../../components/ui/Field'
import { Alert, EmptyState, Page, PageHeader, Pagination, Segmented, Skeleton } from '../../components/ui/Layout'
import { issueService } from '../../services/issue.service'
import { useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { formatRelative } from '../../utils/format'
import { usePageSize } from '../../utils/paging'
import { categoryLabel, issueStatusLabel, moduleShort, priorityLabel } from '../../utils/labels'
import type { Assignee, IssueListItem, IssueSummary, Paged } from '../../types/issue'

type Tab = 'open' | 'done' | 'all'

export default function IssuesPage() {
  const navigate = useNavigate()
  const isIt = useCan('issue.manage')
  const [params, setParams] = useSearchParams()

  const tab = (params.get('tab') as Tab) || 'open'
  const search = params.get('q') ?? ''
  const status = params.get('status') ?? ''
  const priority = params.get('priority') ?? ''
  const assignee = params.get('assignee') ?? ''
  const sort = params.get('sort') ?? (isIt ? 'priority' : 'updated')
  const overdue = params.get('overdue') === '1'
  const page = Number(params.get('page') ?? 1)
  const [pageSize, setPageSize] = usePageSize()

  const [data, setData] = useState<Paged<IssueListItem> | null>(null)
  const [summary, setSummary] = useState<IssueSummary | null>(null)
  const [assignees, setAssignees] = useState<Assignee[]>([])
  const [searchText, setSearchText] = useState(search)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const update = useCallback((changes: Record<string, string | null>) => {
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [key, value] of Object.entries(changes)) {
        if (value === null || value === '') next.delete(key)
        else next.set(key, value)
      }
      if (!('page' in changes)) next.delete('page')
      return next
    }, { replace: true })
  }, [setParams])

  // Changing the page size can start two loads back to back; only the newest one may show its result.
  const latestLoad = useRef(0)

  const load = useCallback(() => {
    const mine = ++latestLoad.current
    const current = () => mine === latestLoad.current
    issueService.list({
      search: search || undefined,
      openOnly: tab === 'open' ? true : tab === 'done' ? false : undefined,
      status: status ? Number(status) : undefined,
      priority: priority ? Number(priority) : undefined,
      assigneeId: assignee && assignee !== 'me' ? assignee : undefined,
      mine: assignee === 'me' ? true : undefined,
      overdue: overdue || undefined,
      sort,
      page,
      pageSize,
    })
      .then((result) => { if (current()) { setData(result); setError('') } })
      .catch((err) => { if (current()) setError(apiError(err, 'โหลดรายการไม่สำเร็จ')) })
      .finally(() => { if (current()) setLoading(false) })
    issueService.summary().then(setSummary).catch(() => {})
  }, [search, tab, status, priority, assignee, overdue, sort, page, pageSize])

  useEffect(() => { load() }, [load])
  useEffect(() => { if (isIt) issueService.assignees().then(setAssignees).catch(() => {}) }, [isIt])

  // Live: a new report or a reply shows up without refreshing. Bursts are collapsed into one reload.
  const timer = useRef<number | undefined>(undefined)
  useIssueEvents(() => {
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(load, 400)
  })
  useEffect(() => () => window.clearTimeout(timer.current), [])

  // Typing in the search box updates the list after a short pause.
  useEffect(() => {
    if (searchText === search) return
    const id = window.setTimeout(() => update({ q: searchText.trim() }), 350)
    return () => window.clearTimeout(id)
  }, [searchText, search, update])

  const items = data?.items ?? []
  const filtered = Boolean(search || status || priority || assignee || overdue)

  return (
    <Page wide>
      <PageHeader
        title={isIt ? 'งานแจ้งปัญหา' : 'เรื่องที่ฉันแจ้ง'}
        subtitle={isIt ? 'คิวงานของทีม IT — เรียงตามความสำคัญ' : 'ติดตามเรื่องที่คุณแจ้งไว้และคำตอบจากทีม IT'}
        actions={<Link to="/issues/new" state={{ from: '/issues' }}><Button variant="primary" icon="plus">แจ้งปัญหาใหม่</Button></Link>} />

      {isIt && (
        <section aria-label="สรุปคิวงาน" className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
          <Stat dark label="เปิดอยู่" value={summary?.open ?? '–'} icon="inbox"
            hint={summary ? `ปิดได้ ${summary.resolvedLast7Days} เรื่องใน 7 วัน` : undefined}
            onClick={() => update({ tab: 'open', status: null, overdue: null })} />
          <Stat label="รอตอบรับ (ใหม่)" value={summary?.new ?? '–'} icon="bell-ringing" tone={summary?.new ? 'accent' : 'default'}
            onClick={() => update({ tab: 'open', status: '0', overdue: null })} />
          <Stat label="รอข้อมูลจากผู้แจ้ง" value={summary?.waitingForReporter ?? '–'} icon="message-question"
            onClick={() => update({ tab: 'open', status: '3', overdue: null })} />
          <Stat label="เกินกำหนด" value={summary?.overdue ?? '–'} icon="alarm" tone={summary?.overdue ? 'red' : 'default'}
            hint={summary?.overdue ? 'ควรจัดการก่อน' : 'ไม่มีเรื่องค้างเกินเวลา'}
            onClick={() => update({ tab: 'open', status: null, overdue: '1' })} />
        </section>
      )}

      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <Segmented<Tab> label="สถานะงาน" value={tab} onChange={(v) => update({ tab: v })}
            options={[{ value: 'open', label: 'กำลังดำเนินการ' }, { value: 'done', label: 'จบแล้ว' }, { value: 'all', label: 'ทั้งหมด' }]} />

          <div className="relative ml-auto w-full sm:w-64">
            <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
            <input type="search" value={searchText} onChange={(e) => setSearchText(e.target.value)}
              placeholder="ค้นหาเลขที่ หัวข้อ หรือชื่อผู้แจ้ง" aria-label="ค้นหาเรื่อง"
              className="h-9 w-full rounded-control border border-line bg-white pl-9 pr-3 text-[13px] focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-4 py-3 [&>select]:min-w-[9rem] [&>select]:flex-1 sm:[&>select]:flex-none">
          <CompactSelect aria-label="กรองตามสถานะ" value={status} onChange={(e) => update({ status: e.target.value })}>
            <option value="">ทุกสถานะ</option>
            {Object.entries(issueStatusLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </CompactSelect>
          {isIt && (
            <>
              <CompactSelect aria-label="กรองตามความสำคัญ" value={priority} onChange={(e) => update({ priority: e.target.value })}>
                <option value="">ทุกความสำคัญ</option>
                {Object.entries(priorityLabel).reverse().map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </CompactSelect>
              <CompactSelect aria-label="กรองตามผู้รับผิดชอบ" value={assignee} onChange={(e) => update({ assignee: e.target.value })}>
                <option value="">ทุกคน</option>
                <option value="me">ของฉัน</option>
                {assignees.map((a) => <option key={a.id} value={a.id}>{a.name} ({a.openIssues})</option>)}
              </CompactSelect>
            </>
          )}
          <CompactSelect aria-label="เรียงลำดับ" value={sort} onChange={(e) => update({ sort: e.target.value })} className="sm:ml-auto">
            <option value="priority">เรียงตามความสำคัญ</option>
            <option value="due">ใกล้ครบกำหนดก่อน</option>
            <option value="updated">อัปเดตล่าสุด</option>
            <option value="newest">ใหม่สุดก่อน</option>
            <option value="oldest">เก่าสุดก่อน</option>
          </CompactSelect>
          {(overdue || filtered) && (
            <Button size="sm" variant="ghost" icon="x"
              onClick={() => { setSearchText(''); update({ q: null, status: null, priority: null, assignee: null, overdue: null }) }}>
              ล้างตัวกรอง
            </Button>
          )}
        </div>

        {overdue && <div className="px-4 pt-3"><Badge tone="red" icon="alarm">แสดงเฉพาะเรื่องที่เกินกำหนด</Badge></div>}
        {error && <div className="p-4"><Alert tone="error">{error}</Alert></div>}

        {loading ? (
          <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">
            {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-16" />)}
          </div>
        ) : items.length === 0 ? (
          <EmptyState icon={filtered ? 'search-off' : 'mood-smile'}
            title={filtered ? 'ไม่พบเรื่องที่ตรงกับตัวกรอง' : tab === 'open' ? 'ไม่มีเรื่องค้างอยู่' : 'ยังไม่มีเรื่อง'}
            action={!filtered && !isIt ? <Link to="/issues/new" state={{ from: '/issues' }}><Button variant="primary" icon="plus">แจ้งปัญหา</Button></Link> : undefined}>
            {filtered ? 'ลองเปลี่ยนหรือล้างตัวกรอง' : isIt ? 'เมื่อมีคนแจ้งปัญหา เรื่องจะขึ้นที่นี่ทันที' : 'เมื่อพบปัญหาการใช้งาน กดปุ่มแจ้งปัญหาได้ตลอดเวลา'}
          </EmptyState>
        ) : (
          <>
            {/* Desktop: table */}
            <div className="hidden overflow-x-auto md:block">
              <table className="w-full min-w-[880px] text-left">
                <thead>
                  <tr className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                    <th scope="col" className="px-5 py-3">เลขที่</th>
                    <th scope="col" className="px-3 py-3">เรื่อง</th>
                    <th scope="col" className="px-3 py-3">สถานะ</th>
                    <th scope="col" className="px-3 py-3">ความสำคัญ</th>
                    <th scope="col" className="px-3 py-3">ผู้รับผิดชอบ</th>
                    <th scope="col" className="px-3 py-3">กำหนด</th>
                    <th scope="col" className="px-5 py-3 text-right">อัปเดต</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {items.map((issue) => (
                    <tr key={issue.id} onClick={() => navigate(`/issues/${issue.id}`)}
                      className="cursor-pointer transition-colors hover:bg-surface">
                      <td className="whitespace-nowrap px-5 py-3.5 text-[13px] tabular text-ink-2">{issue.number}</td>
                      <td className="max-w-md px-3 py-3.5">
                        <Link to={`/issues/${issue.id}`} onClick={(e) => e.stopPropagation()}
                          className="line-clamp-1 font-medium text-ink hover:text-accent">{issue.title}</Link>
                        <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-ink-3">
                          <span>{moduleShort[issue.module]}</span><span aria-hidden="true">·</span>
                          <span>{issue.reporterName}{issue.departmentName ? ` · ${issue.departmentName}` : ''}</span>
                          {issue.isPublicSubmission && <Badge tone="gray" icon="user-question">ไม่ได้เข้าสู่ระบบ</Badge>}
                          {issue.commentCount > 0 && <span className="inline-flex items-center gap-0.5"><i className="ti ti-message text-sm" aria-hidden="true" />{issue.commentCount}</span>}
                          {issue.attachmentCount > 0 && <span className="inline-flex items-center gap-0.5"><i className="ti ti-paperclip text-sm" aria-hidden="true" />{issue.attachmentCount}</span>}
                        </p>
                      </td>
                      <td className="px-3 py-3.5"><StatusBadge status={issue.status} /></td>
                      <td className="px-3 py-3.5"><PriorityBadge priority={issue.priority} /></td>
                      <td className="px-3 py-3.5 text-[13px] text-ink-2">{issue.assigneeName ?? <span className="text-ink-3">ยังไม่มี</span>}</td>
                      <td className="px-3 py-3.5"><DueLabel issue={issue} /></td>
                      <td className="whitespace-nowrap px-5 py-3.5 text-right text-xs text-ink-3">{formatRelative(issue.updatedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Phone: cards */}
            <ul className="divide-y divide-line md:hidden">
              {items.map((issue) => (
                <li key={issue.id}>
                  <Link to={`/issues/${issue.id}`} className="block px-4 py-4 active:bg-surface">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs tabular text-ink-3">{issue.number}</span>
                      <StatusBadge status={issue.status} />
                    </div>
                    <p className="mt-1 font-medium text-ink">{issue.title}</p>
                    <p className="mt-0.5 text-xs text-ink-3">
                      {moduleShort[issue.module]} · {categoryLabel[issue.category]} · {issue.reporterName}
                    </p>
                    <div className="mt-2 flex flex-wrap items-center gap-2">
                      <PriorityBadge priority={issue.priority} />
                      <DueLabel issue={issue} />
                      <span className="ml-auto text-xs text-ink-3">{formatRelative(issue.updatedAt)}</span>
                    </div>
                  </Link>
                </li>
              ))}
            </ul>

            <Pagination page={data!.page} pageSize={data!.pageSize} total={data!.total}
              onChange={(p) => update({ page: String(p) })}
              onPageSizeChange={(size) => { setPageSize(size); update({}) }} />
          </>
        )}
      </Card>
    </Page>
  )
}
