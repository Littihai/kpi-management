import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import AttachmentImage from '../../components/issues/AttachmentImage'
import { DueLabel, PriorityBadge, StatusBadge } from '../../components/issues/IssueBadges'
import { useIssueEvents } from '../../components/realtimeContext'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card, CardHeader, Eyebrow } from '../../components/ui/Card'
import { CompactSelect, SelectInput, TextArea, TextInput } from '../../components/ui/Field'
import { Alert, Avatar, EmptyState, Page, PageSkeleton } from '../../components/ui/Layout'
import Modal from '../../components/ui/Modal'
import { issueService } from '../../services/issue.service'
import { useAuthStore } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { formatCountdown, formatDateTime, formatRelative } from '../../utils/format'
import {
  auditActionLabel, categoryLabel, enumOptions, impactLabel, issueStatusLabel, moduleLabel, priorityLabel,
  rootCauseLabel,
} from '../../utils/labels'
import { IssueRootCause, IssueStatus } from '../../types/issue'
import type { Assignee, IssueActionName, IssueDetail, IssueEvent } from '../../types/issue'

type Dialog = null | 'requestInfo' | 'resolve' | 'reject' | 'reopen'

const statusByName: Record<string, string> = {
  New: issueStatusLabel[0], Assigned: issueStatusLabel[1], InProgress: issueStatusLabel[2],
  WaitingForReporter: issueStatusLabel[3], Resolved: issueStatusLabel[4], Closed: issueStatusLabel[5],
  Rejected: issueStatusLabel[6], Reopened: issueStatusLabel[7],
  Low: priorityLabel[0], Medium: priorityLabel[1], High: priorityLabel[2], Critical: priorityLabel[3],
}

/** "ใหม่ → รับเรื่องแล้ว" style text for a history row. Free text (reasons, replies) is shown as typed. */
function describeEvent(e: IssueEvent): string | null {
  const from = e.from ? (statusByName[e.from] ?? e.from) : null
  const to = e.to ? (statusByName[e.to.split(':')[0]] ?? e.to) : null
  if (e.action === 'Assign') return e.to ? `มอบหมายให้ ${e.to}` : null
  if (e.action === 'Create') return null
  if (from && to) return `${from} → ${to}`
  return to
}

export default function IssueDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const me = useAuthStore((s) => s.user)

  const [issue, setIssue] = useState<IssueDetail | null>(null)
  const [assignees, setAssignees] = useState<Assignee[]>([])
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [dialog, setDialog] = useState<Dialog>(null)
  const [comment, setComment] = useState('')
  const [internal, setInternal] = useState(false)
  const [showTech, setShowTech] = useState(false)
  const attachInput = useRef<HTMLInputElement>(null)

  const load = useCallback(() => {
    if (!id) return Promise.resolve()
    return issueService.get(id)
      .then((data) => { setIssue(data); setNotFound(false) })
      .catch((err) => {
        if (err?.response?.status === 404) setNotFound(true)
        else setError(apiError(err, 'โหลดเรื่องนี้ไม่สำเร็จ'))
      })
  }, [id])

  useEffect(() => { load().finally(() => setLoading(false)) }, [load])

  useIssueEvents((event) => { if (event.issueId === id) load() })

  const can = (action: IssueActionName) => issue?.allowedActions.includes(action) ?? false
  const isIt = can('internalNote')

  useEffect(() => {
    if (isIt) issueService.assignees().then(setAssignees).catch(() => {})
  }, [isIt])

  const run = async (action: () => Promise<IssueDetail>): Promise<boolean> => {
    setBusy(true)
    setError('')
    try {
      setIssue(await action())
      return true
    } catch (err) {
      setError(apiError(err))
      load()
      return false
    } finally {
      setBusy(false)
    }
  }

  const sendComment = async () => {
    if (!issue || !comment.trim()) return
    if (await run(() => issueService.comment(issue.id, comment.trim(), internal))) {
      setComment('')
      setInternal(false)
    }
  }

  if (loading) return <Page><PageSkeleton /></Page>
  if (notFound || !issue) {
    return (
      <Page>
        <EmptyState icon="file-unknown" title="ไม่พบเรื่องนี้"
          action={<Button onClick={() => navigate('/issues')}>กลับไปรายการ</Button>}>
          {error || 'เรื่องนี้อาจถูกลบ หรือคุณไม่มีสิทธิ์ดู'}
        </EmptyState>
      </Page>
    )
  }

  const finished = issue.status === IssueStatus.Resolved || issue.status === IssueStatus.Closed || issue.status === IssueStatus.Rejected
  const waiting = issue.status === IssueStatus.WaitingForReporter
  const responded = Boolean(issue.firstResponseAt)

  return (
    <Page wide>
      <Link to="/issues" className="mb-4 inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink">
        <i className="ti ti-arrow-left" aria-hidden="true" />รายการเรื่องทั้งหมด
      </Link>

      <header className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <Eyebrow>{issue.number}</Eyebrow>
          <h1 className="mt-1 text-2xl font-semibold leading-snug text-ink">{issue.title}</h1>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <StatusBadge status={issue.status} />
            <PriorityBadge priority={issue.priority} />
            <Badge tone="gray" icon="apps">{moduleLabel[issue.module].split(' (')[0]}</Badge>
            <Badge tone="gray">{categoryLabel[issue.category]}</Badge>
            {issue.reopenCount > 0 && <Badge tone="orange" icon="rotate">เปิดใหม่ {issue.reopenCount} ครั้ง</Badge>}
            {issue.isPublicSubmission && <Badge tone="gray" icon="user-question">ผู้แจ้งยังไม่ได้เข้าสู่ระบบ</Badge>}
          </div>
        </div>
      </header>

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}
      {waiting && !isIt && (
        <div className="mb-4"><Alert tone="warning">ทีม IT รอข้อมูลจากคุณ — ตอบกลับด้านล่างเพื่อให้ดำเนินการต่อ</Alert></div>
      )}
      {issue.status === IssueStatus.Resolved && can('confirm') && (
        <div className="mb-4">
          <Alert tone="success">ทีม IT แก้ไขเรื่องนี้แล้ว กรุณาตรวจสอบและยืนยัน (ถ้าไม่ตอบใน 3 วัน ระบบจะปิดเรื่องให้อัตโนมัติ)</Alert>
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[1fr_21rem]">
        {/* ─── Main column ─── */}
        <div className="min-w-0 space-y-6">
          <Card>
            <CardHeader eyebrow="รายละเอียด" title="เกิดอะไรขึ้น" />
            <div className="space-y-5 px-5 pb-5 pt-3">
              <p className="whitespace-pre-wrap break-words text-[15px] leading-relaxed text-ink">{issue.description}</p>

              {issue.attachments.length > 0 && (
                <div>
                  <p className="mb-2 text-xs font-medium text-ink-3">ภาพแนบ ({issue.attachments.length})</p>
                  <div className="flex flex-wrap gap-3">
                    {issue.attachments.map((a) => <AttachmentImage key={a.id} issueId={issue.id} attachment={a} />)}
                  </div>
                </div>
              )}

              {can('attach') && (
                <div>
                  <input ref={attachInput} type="file" accept="image/png,image/jpeg,image/gif,image/webp" multiple
                    className="sr-only" aria-label="แนบรูปเพิ่ม"
                    onChange={async (e) => {
                      const files = [...(e.target.files ?? [])]
                      e.target.value = ''
                      if (files.length) await run(() => issueService.attach(issue.id, files))
                    }} />
                  <Button size="sm" icon="photo-plus" onClick={() => attachInput.current?.click()} disabled={busy}>แนบรูปเพิ่ม</Button>
                </div>
              )}

              {isIt && (issue.pageUrl || issue.clientInfo || issue.userAgent || issue.reporterIp || issue.errorRequestId) && (
                <div className="rounded-control bg-surface p-3">
                  <button type="button" onClick={() => setShowTech((v) => !v)} aria-expanded={showTech}
                    className="flex w-full items-center justify-between text-[13px] font-medium text-ink-2">
                    ข้อมูลทางเทคนิค (ระบบเก็บให้อัตโนมัติ)
                    <i className={`ti ti-chevron-${showTech ? 'up' : 'down'}`} aria-hidden="true" />
                  </button>
                  {showTech && (
                    <dl className="mt-3 grid gap-x-6 gap-y-2 text-[13px] sm:grid-cols-2">
                      {issue.pageUrl && <div className="sm:col-span-2"><dt className="text-ink-3">หน้าที่ใช้อยู่</dt><dd className="break-all">{issue.pageUrl}</dd></div>}
                      {issue.clientInfo && <div><dt className="text-ink-3">เครื่องผู้แจ้ง</dt><dd>{issue.clientInfo}</dd></div>}
                      {issue.reporterIp && <div><dt className="text-ink-3">ที่อยู่ IP</dt><dd className="tabular">{issue.reporterIp}</dd></div>}
                      {issue.errorRequestId && <div><dt className="text-ink-3">รหัสคำขอที่ผิดพลาด</dt><dd className="break-all tabular">{issue.errorRequestId}</dd></div>}
                      {issue.userAgent && <div className="sm:col-span-2"><dt className="text-ink-3">เบราว์เซอร์</dt><dd className="break-all">{issue.userAgent}</dd></div>}
                    </dl>
                  )}
                </div>
              )}
            </div>
          </Card>

          {(issue.resolution || issue.rejectReason) && (
            <Card className={issue.rejectReason ? '' : 'border-green-soft'}>
              <CardHeader eyebrow={issue.rejectReason ? 'เหตุผล' : 'วิธีแก้ไข'}
                title={issue.rejectReason ? 'ไม่ดำเนินการต่อ' : 'แก้ไขอย่างไร'} />
              <div className="space-y-4 px-5 pb-5 pt-3">
                <p className="whitespace-pre-wrap break-words text-[15px] leading-relaxed">{issue.resolution ?? issue.rejectReason}</p>
                {issue.resolution && (
                  <dl className="grid gap-x-6 gap-y-3 border-t border-line pt-4 text-[13px] sm:grid-cols-2">
                    <div><dt className="text-ink-3">สาเหตุ</dt><dd className="font-medium">{rootCauseLabel[issue.rootCause]}</dd></div>
                    {issue.timeSpentMinutes != null && (
                      <div><dt className="text-ink-3">เวลาที่ใช้</dt><dd className="font-medium tabular">{issue.timeSpentMinutes} นาที</dd></div>
                    )}
                    {issue.prevention && <div className="sm:col-span-2"><dt className="text-ink-3">วิธีป้องกันไม่ให้เกิดซ้ำ</dt><dd className="whitespace-pre-wrap">{issue.prevention}</dd></div>}
                    {issue.fixReference && (
                      <div className="sm:col-span-2"><dt className="text-ink-3">อ้างอิง</dt>
                        <dd className="break-all">
                          {/^https?:\/\//.test(issue.fixReference)
                            ? <a href={issue.fixReference} target="_blank" rel="noreferrer" className="text-accent hover:underline">{issue.fixReference}</a>
                            : issue.fixReference}
                        </dd>
                      </div>
                    )}
                  </dl>
                )}
              </div>
            </Card>
          )}

          <Card>
            <CardHeader eyebrow="การสนทนา" title={`ข้อความ (${issue.comments.length})`} />
            <ul className="space-y-4 px-5 pb-2 pt-4">
              {issue.comments.length === 0 && (
                <li className="py-4 text-center text-sm text-ink-3">ยังไม่มีข้อความ</li>
              )}
              {issue.comments.map((c) => (
                <li key={c.id} className="flex gap-3">
                  <Avatar name={c.authorName} />
                  <div className={`min-w-0 flex-1 rounded-control px-4 py-3
                    ${c.isInternal ? 'bg-amber-soft' : c.isStaff ? 'bg-accent-soft' : 'bg-surface'}`}>
                    <p className="flex flex-wrap items-center gap-x-2 text-[13px]">
                      <span className="font-medium text-ink">{c.authorName}</span>
                      {c.isStaff && <Badge tone="orange" className="!py-0">ทีม IT</Badge>}
                      {c.isInternal && <Badge tone="amber" icon="lock" className="!py-0">บันทึกภายใน — ผู้แจ้งมองไม่เห็น</Badge>}
                      <span className="text-xs text-ink-3" title={formatDateTime(c.createdAt)}>{formatRelative(c.createdAt)}</span>
                    </p>
                    <p className="mt-1 whitespace-pre-wrap break-words text-[14px] text-ink">{c.body}</p>
                  </div>
                </li>
              ))}
            </ul>

            {can('comment') && (
              <div className="border-t border-line p-5">
                <TextArea label={waiting && !isIt ? 'ตอบกลับทีม IT' : 'เขียนข้อความ'} rows={3} maxLength={4000}
                  value={comment} onChange={(e) => setComment(e.target.value)}
                  placeholder={waiting && !isIt ? 'ให้ข้อมูลที่ทีม IT ขอ...' : 'พิมพ์ข้อความ...'} />
                <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
                  {isIt ? (
                    <label className="inline-flex cursor-pointer items-center gap-2 text-[13px] text-ink-2">
                      <input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)}
                        className="h-4 w-4 rounded border-line-strong accent-accent" />
                      บันทึกภายใน (เห็นเฉพาะทีม IT)
                    </label>
                  ) : <span />}
                  <Button variant={internal ? 'dark' : 'primary'} icon={internal ? 'lock' : 'send'}
                    disabled={!comment.trim()} loading={busy} onClick={sendComment}>
                    {internal ? 'บันทึกโน้ต' : 'ส่งข้อความ'}
                  </Button>
                </div>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader eyebrow="ประวัติ" title="เกิดอะไรขึ้นบ้าง" />
            <ol className="relative px-5 pb-5 pt-4">
              {issue.timeline.map((e, i) => {
                const detail = describeEvent(e)
                return (
                  <li key={i} className="relative flex gap-3 pb-5 last:pb-0">
                    {i < issue.timeline.length - 1 && <span className="absolute left-[5px] top-3 h-full w-px bg-line" aria-hidden="true" />}
                    <span className="relative mt-1.5 h-[11px] w-[11px] shrink-0 rounded-full border-2 border-white bg-accent ring-1 ring-line" aria-hidden="true" />
                    <div className="min-w-0 text-[13px]">
                      <p className="font-medium text-ink">{auditActionLabel[e.action] ?? e.action}</p>
                      {detail && <p className="text-ink-2">{detail}</p>}
                      <p className="text-xs text-ink-3">{e.actorName ?? 'ระบบ'} · {formatDateTime(e.at)}</p>
                    </div>
                  </li>
                )
              })}
            </ol>
          </Card>
        </div>

        {/* ─── Side column ─── */}
        <aside className="space-y-6">
          <Card>
            <CardHeader eyebrow="กำหนดเวลา" title="ข้อตกลงการให้บริการ" />
            <dl className="space-y-4 px-5 pb-5 pt-4 text-sm">
              <div>
                <dt className="text-xs text-ink-3">ตอบรับภายใน 2 ชั่วโมง</dt>
                <dd className="mt-0.5 font-medium">
                  {responded
                    ? <span className="text-green-ink"><i className="ti ti-circle-check" aria-hidden="true" /> ตอบรับแล้ว {formatDateTime(issue.firstResponseAt)}</span>
                    : finished
                      ? '–'
                      : <span className={formatCountdown(issue.responseDueAt).late ? 'text-red-ink' : ''}>{formatCountdown(issue.responseDueAt).text}</span>}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-ink-3">แก้ไขภายใน</dt>
                <dd className="mt-0.5 font-medium">
                  {issue.resolvedAt
                    ? <span className="text-green-ink"><i className="ti ti-circle-check" aria-hidden="true" /> แก้ไขแล้ว {formatDateTime(issue.resolvedAt)}</span>
                    : <DueLabel issue={issue} />}
                  {!finished && <span className="mt-0.5 block text-xs font-normal text-ink-3">กำหนด {formatDateTime(issue.resolveDueAt)}</span>}
                </dd>
              </div>
            </dl>
          </Card>

          {(isIt || can('confirm') || can('reopen')) && (
            <Card>
              <CardHeader eyebrow="การดำเนินการ" title="จัดการเรื่องนี้" />
              <div className="space-y-4 px-5 pb-5 pt-4">
                {isIt && can('assign') && (
                  <div>
                    <label htmlFor="assignee" className="mb-1.5 block text-[13px] font-medium">ผู้รับผิดชอบ</label>
                    <div className="flex gap-2">
                      <CompactSelect id="assignee" className="min-w-0 flex-1" value={issue.assigneeId ?? ''} disabled={busy}
                        onChange={(e) => e.target.value && run(() => issueService.assign(issue.id, e.target.value))}>
                        <option value="" disabled>เลือกผู้รับผิดชอบ</option>
                        {assignees.map((a) => <option key={a.id} value={a.id}>{a.name} ({a.openIssues} งาน)</option>)}
                      </CompactSelect>
                      {me && assignees.some((a) => a.name === me.fullName) && issue.assigneeName !== me.fullName && (
                        <Button size="sm" disabled={busy}
                          onClick={() => { const mine = assignees.find((a) => a.name === me.fullName); if (mine) run(() => issueService.assign(issue.id, mine.id)) }}>
                          รับเอง
                        </Button>
                      )}
                    </div>
                  </div>
                )}

                {can('setPriority') && (
                  <div>
                    <label htmlFor="priority" className="mb-1.5 block text-[13px] font-medium">ความสำคัญ</label>
                    <CompactSelect id="priority" className="w-full" value={issue.priority} disabled={busy}
                      onChange={(e) => run(() => issueService.setPriority(issue.id, Number(e.target.value)))}>
                      {Object.entries(priorityLabel).reverse().map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                    </CompactSelect>
                  </div>
                )}

                <div className="flex flex-col gap-2">
                  {can('start') && <Button variant="primary" icon="player-play" disabled={busy} onClick={() => run(() => issueService.start(issue.id))}>เริ่มแก้ไข</Button>}
                  {can('resolve') && <Button variant="dark" icon="circle-check" disabled={busy} onClick={() => setDialog('resolve')}>แก้ไขแล้ว</Button>}
                  {can('requestInfo') && <Button icon="message-question" disabled={busy} onClick={() => setDialog('requestInfo')}>ขอข้อมูลเพิ่ม</Button>}
                  {can('confirm') && <Button variant="primary" icon="thumb-up" disabled={busy} onClick={() => run(() => issueService.confirm(issue.id))}>ใช้งานได้แล้ว ปิดเรื่อง</Button>}
                  {can('reopen') && <Button icon="rotate" disabled={busy} onClick={() => setDialog('reopen')}>ยังไม่หาย / เปิดใหม่</Button>}
                  {can('reject') && <Button variant="danger" icon="ban" disabled={busy} onClick={() => setDialog('reject')}>ไม่ดำเนินการ</Button>}
                </div>
              </div>
            </Card>
          )}

          <Card>
            <CardHeader eyebrow="ข้อมูล" title="สรุปเรื่อง" />
            <dl className="space-y-3 px-5 pb-5 pt-4 text-[13px]">
              {[
                ['ผู้แจ้ง', issue.reporterName],
                isIt && issue.reporterEmail ? ['อีเมล', issue.reporterEmail] : null,
                isIt && issue.reporterPhone ? ['เบอร์โทร', issue.reporterPhone] : null,
                ['แผนก', issue.departmentName ?? 'ไม่ระบุ'],
                ['ผู้รับผิดชอบ', issue.assigneeName ?? 'ยังไม่มี'],
                ['ส่วนของระบบ', moduleLabel[issue.module]],
                ['ผลกระทบ', impactLabel[issue.impact]],
                ['แจ้งเมื่อ', formatDateTime(issue.createdAt)],
                issue.closedAt ? ['ปิดเมื่อ', formatDateTime(issue.closedAt)] : null,
              ].filter((row): row is string[] => row !== null).map(([label, value]) => (
                <div key={label} className="flex justify-between gap-4">
                  <dt className="shrink-0 text-ink-3">{label}</dt>
                  <dd className="text-right font-medium text-ink">{value}</dd>
                </div>
              ))}
            </dl>
          </Card>
        </aside>
      </div>

      <RequestInfoDialog open={dialog === 'requestInfo'} onClose={() => setDialog(null)} busy={busy}
        onSubmit={async (message) => { if (await run(() => issueService.requestInfo(issue.id, message))) setDialog(null) }} />
      <ResolveDialog open={dialog === 'resolve'} onClose={() => setDialog(null)} busy={busy}
        onSubmit={async (body) => { if (await run(() => issueService.resolve(issue.id, body))) setDialog(null) }} />
      <ReopenDialog open={dialog === 'reopen'} onClose={() => setDialog(null)} busy={busy}
        onSubmit={async (reason) => { if (await run(() => issueService.reopen(issue.id, reason))) setDialog(null) }} />
      <RejectDialog open={dialog === 'reject'} onClose={() => setDialog(null)} busy={busy}
        onSubmit={async (reason, duplicateNumber) => {
          let duplicateId: string | undefined
          if (duplicateNumber) {
            const found = (await issueService.list({ search: duplicateNumber, pageSize: 5 })).items
              .find((i) => i.number.toLowerCase() === duplicateNumber.toLowerCase())
            if (!found) { setError(`ไม่พบเรื่องเลขที่ ${duplicateNumber}`); return }
            duplicateId = found.id
          }
          if (await run(() => issueService.reject(issue.id, reason, duplicateId))) setDialog(null)
        }} />
    </Page>
  )
}

// ───────────── Dialogs ─────────────

interface DialogProps<T> { open: boolean; onClose: () => void; busy: boolean; onSubmit: (value: T) => void }

function RequestInfoDialog({ open, onClose, busy, onSubmit }: DialogProps<string>) {
  const [message, setMessage] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="ขอข้อมูลเพิ่มจากผู้แจ้ง"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" loading={busy} disabled={!message.trim()} onClick={() => { onSubmit(message.trim()); setMessage('') }}>ส่งคำถาม</Button>
      </>}>
      <p className="mb-3 text-sm text-ink-2">เรื่องจะเปลี่ยนเป็น "รอข้อมูลจากผู้แจ้ง" และหยุดนับเวลาจนกว่าผู้แจ้งจะตอบ</p>
      <TextArea label="ต้องการทราบอะไรเพิ่ม" rows={4} maxLength={4000} value={message} onChange={(e) => setMessage(e.target.value)}
        placeholder="เช่น เกิดกับ KPI ตัวไหน และกดตอนกี่โมง" />
    </Modal>
  )
}

function ReopenDialog({ open, onClose, busy, onSubmit }: DialogProps<string>) {
  const [reason, setReason] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="ปัญหายังไม่หาย"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" loading={busy} disabled={!reason.trim()} onClick={() => { onSubmit(reason.trim()); setReason('') }}>เปิดเรื่องใหม่</Button>
      </>}>
      <TextArea label="ยังพบปัญหาอะไรอยู่" rows={4} maxLength={4000} value={reason} onChange={(e) => setReason(e.target.value)}
        hint="ทีม IT จะได้รับแจ้งทันที และเริ่มนับเวลาแก้ไขใหม่" />
    </Modal>
  )
}

function RejectDialog({ open, onClose, busy, onSubmit }: Omit<DialogProps<string>, 'onSubmit'> & { onSubmit: (reason: string, duplicateNumber: string) => void }) {
  const [reason, setReason] = useState('')
  const [duplicate, setDuplicate] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="ไม่ดำเนินการต่อ"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="dark" loading={busy} disabled={!reason.trim()} onClick={() => onSubmit(reason.trim(), duplicate.trim())}>ยืนยัน</Button>
      </>}>
      <div className="space-y-4">
        <TextArea label="เหตุผล (ผู้แจ้งจะเห็นข้อความนี้)" rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)}
          placeholder="เช่น ซ้ำกับเรื่องที่แจ้งไว้แล้ว / ไม่ใช่ปัญหาของระบบนี้" />
        <TextInput label="ซ้ำกับเรื่องเลขที่ (ถ้ามี)" value={duplicate} onChange={(e) => setDuplicate(e.target.value)} placeholder="ISS-2569-0001" />
      </div>
    </Modal>
  )
}

function ResolveDialog({ open, onClose, busy, onSubmit }: DialogProps<{
  resolution: string; rootCause: number; prevention?: string; fixReference?: string; timeSpentMinutes?: number | null
}>) {
  const [resolution, setResolution] = useState('')
  const [rootCause, setRootCause] = useState<number>(IssueRootCause.None)
  const [prevention, setPrevention] = useState('')
  const [fixReference, setFixReference] = useState('')
  const [minutes, setMinutes] = useState('')
  const valid = resolution.trim().length >= 5 && rootCause !== IssueRootCause.None

  return (
    <Modal open={open} onClose={onClose} title="บันทึกวิธีแก้ไข" width="lg"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" loading={busy} disabled={!valid}
          onClick={() => onSubmit({
            resolution: resolution.trim(), rootCause,
            prevention: prevention.trim() || undefined,
            fixReference: fixReference.trim() || undefined,
            timeSpentMinutes: minutes ? Number(minutes) : null,
          })}>
          แก้ไขแล้ว แจ้งผู้แจ้ง
        </Button>
      </>}>
      <div className="space-y-4">
        <TextArea label="แก้ไขอย่างไร (ผู้แจ้งจะเห็นข้อความนี้)" required rows={4} maxLength={4000}
          value={resolution} onChange={(e) => setResolution(e.target.value)}
          placeholder="เช่น ปรับสิทธิ์ของปุ่มอนุมัติให้ตรงกับบทบาท ผู้ใช้ลองกดใหม่ได้เลย" />
        <SelectInput label="สาเหตุของปัญหา" required value={rootCause} placeholder="เลือกสาเหตุ"
          options={enumOptions(rootCauseLabel).filter((o) => o.value !== 0)}
          onChange={(e) => setRootCause(e.target.value ? Number(e.target.value) : IssueRootCause.None)} />
        <TextArea label="ป้องกันไม่ให้เกิดซ้ำอย่างไร" rows={2} maxLength={2000} value={prevention}
          onChange={(e) => setPrevention(e.target.value)} hint="ไม่บังคับ แต่ช่วยให้ทีมเรียนรู้ได้" />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextInput label="ลิงก์อ้างอิง (PR / commit)" value={fixReference} maxLength={500}
            onChange={(e) => setFixReference(e.target.value)} placeholder="https://github.com/..." />
          <TextInput label="เวลาที่ใช้ (นาที)" type="number" min={0} max={100000} value={minutes}
            onChange={(e) => setMinutes(e.target.value)} />
        </div>
      </div>
    </Modal>
  )
}
