import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import Badge, { TrafficBadge } from '../components/ui/Badge'
import Button from '../components/ui/Button'
import { Card, CardHeader, Eyebrow } from '../components/ui/Card'
import { TextArea, TextInput } from '../components/ui/Field'
import { Alert, EmptyState, Page, PageSkeleton } from '../components/ui/Layout'
import Modal from '../components/ui/Modal'
import Progress from '../components/ui/Progress'
import { useKpiEvents } from '../components/realtimeContext'
import { kpiService } from '../services/kpi.service'
import { apiError } from '../utils/errors'
import { formatDate, formatDateTime } from '../utils/format'
import { kpiStatusLabel, kpiStatusTone } from '../utils/labels'
import type { KpiActionName, KpiDto } from '../types/kpi'

const lightLabel: Record<string, string> = { green: 'ตามเป้า', yellow: 'ต้องระวัง', red: 'ล่าช้า', gray: 'ยังไม่เริ่ม' }

export default function KpiDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [kpi, setKpi] = useState<KpiDto | null>(null)
  const [progress, setProgress] = useState('')
  const [notes, setNotes] = useState('')
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notFound, setNotFound] = useState(false)
  const [confirm, setConfirm] = useState<'close' | 'delete' | null>(null)

  const load = useCallback(() => {
    if (!id) return Promise.resolve()
    return kpiService.getById(id)
      .then((data) => { setKpi(data); setNotFound(false) })
      .catch((err) => {
        if (err?.response?.status === 404) setNotFound(true)
        else setError(apiError(err, 'โหลด KPI ไม่สำเร็จ'))
      })
  }, [id])

  useEffect(() => { load().finally(() => setLoading(false)) }, [load])

  // Someone else changed this KPI (or deleted it): show the latest state.
  useKpiEvents((event) => {
    if (event.kpiId !== id) return
    if (event.action === 'deleted') navigate('/kpi')
    else load()
  })

  const can = (action: KpiActionName) => kpi?.allowedActions.includes(action) ?? false

  const run = async (action: () => Promise<KpiDto | void>) => {
    setSaving(true)
    setError('')
    try {
      const updated = await action()
      if (updated) setKpi(updated)
      return true
    } catch (err) {
      setError(apiError(err))
      load() // the KPI may have changed under us; show what is true now
      return false
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Page><PageSkeleton /></Page>

  if (notFound || !kpi) {
    return (
      <Page>
        <EmptyState icon="file-unknown" title="ไม่พบ KPI นี้" action={<Button onClick={() => navigate('/kpi')}>กลับไปรายการ KPI</Button>}>
          {error || 'KPI นี้อาจถูกลบ หรือคุณไม่มีสิทธิ์ดู'}
        </EmptyState>
      </Page>
    )
  }

  const hasActions = (['submit', 'approve', 'reject', 'close', 'edit', 'delete'] as KpiActionName[]).some(can)
  const target = kpi.target || 100

  return (
    <Page>
      <Link to="/kpi" className="mb-4 inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink">
        <i className="ti ti-arrow-left" aria-hidden="true" />รายการ KPI
      </Link>

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      {kpi.statusLabel === 'Draft' && kpi.rejectReason && (
        <div className="mb-4">
          <Alert tone="warning"><span className="font-medium">ถูกตีกลับให้แก้ไข:</span> {kpi.rejectReason}</Alert>
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <div className="min-w-0 space-y-6">
          <Card className="p-5 sm:p-6">
            <Eyebrow>{kpi.departmentName} · ปี {kpi.year}</Eyebrow>
            <h1 className="mt-1 text-2xl font-semibold leading-snug">{kpi.name}</h1>
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <Badge tone={kpiStatusTone[kpi.statusLabel] ?? 'gray'} dot>{kpiStatusLabel[kpi.statusLabel] ?? kpi.statusLabel}</Badge>
              <TrafficBadge light={kpi.trafficLight} label={lightLabel[kpi.trafficLight] ?? ''} />
            </div>

            {kpi.description && <p className="mt-4 whitespace-pre-wrap text-[15px] leading-relaxed text-ink-2">{kpi.description}</p>}

            <div className="mt-6">
              <div className="mb-2 flex items-baseline justify-between">
                <span className="text-[13px] text-ink-3">ความคืบหน้า</span>
                <span className="text-lg font-semibold tabular">
                  {kpi.progress} <span className="text-sm font-normal text-ink-3">/ {kpi.target}%</span>
                </span>
              </div>
              <Progress value={kpi.progress} max={target} color={kpi.trafficLight} label="ความคืบหน้า" className="!h-2.5" />
            </div>

            <dl className="mt-6 grid grid-cols-2 gap-4 border-t border-line pt-5 text-sm sm:grid-cols-4">
              <div><dt className="text-ink-3">เจ้าของ</dt><dd className="font-medium">{kpi.ownerName}</dd></div>
              <div><dt className="text-ink-3">เริ่ม</dt><dd className="font-medium tabular">{formatDate(kpi.startDate)}</dd></div>
              <div><dt className="text-ink-3">ครบกำหนด</dt><dd className="font-medium tabular">{formatDate(kpi.dueDate)}</dd></div>
              <div><dt className="text-ink-3">อนุมัติเมื่อ</dt><dd className="font-medium tabular">{kpi.approvedAt ? formatDateTime(kpi.approvedAt) : '–'}</dd></div>
            </dl>
          </Card>

          {can('updateProgress') && (
            <Card>
              <CardHeader eyebrow="รายงาน" title="อัปเดตความคืบหน้า" />
              <div className="space-y-4 px-5 pb-5 pt-4">
                <TextInput label={`ความคืบหน้าล่าสุด (สูงสุด ${kpi.target})`} type="number" min={0} max={kpi.target}
                  value={progress} onChange={(e) => setProgress(e.target.value)} placeholder={`0–${kpi.target}`} wrapperClassName="max-w-xs" />
                <TextArea label="บันทึก" rows={3} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)}
                  placeholder="เดือนนี้ทำอะไรไปบ้าง?" />
                <Button variant="primary" icon="device-floppy" loading={saving} disabled={progress === ''}
                  onClick={async () => { if (await run(() => kpiService.updateProgress({ kpiId: kpi.id, progress: Number(progress), notes }))) { setProgress(''); setNotes('') } }}>
                  บันทึกความคืบหน้า
                </Button>
              </div>
            </Card>
          )}
        </div>

        <aside className="space-y-6">
          {hasActions && (
            <Card>
              <CardHeader eyebrow="การดำเนินการ" title="จัดการ KPI นี้" />
              <div className="flex flex-col gap-2 px-5 pb-5 pt-4">
                {can('submit') && <Button variant="primary" icon="send" loading={saving} onClick={() => run(() => kpiService.submit(kpi.id))}>ส่งขออนุมัติ</Button>}
                {can('approve') && <Button variant="primary" icon="circle-check" loading={saving} onClick={() => run(() => kpiService.approve(kpi.id))}>อนุมัติ</Button>}
                {can('reject') && <Button icon="arrow-back-up" disabled={saving} onClick={() => setRejecting(true)}>ตีกลับให้แก้ไข</Button>}
                {can('edit') && <Button icon="edit" disabled={saving} onClick={() => navigate(`/kpi/${kpi.id}/edit`)}>แก้ไข</Button>}
                {can('close') && <Button variant="dark" icon="lock" disabled={saving} onClick={() => setConfirm('close')}>ปิด KPI</Button>}
                {can('delete') && <Button variant="danger" icon="trash" disabled={saving} onClick={() => setConfirm('delete')}>ลบ</Button>}
              </div>
            </Card>
          )}

          {!hasActions && !can('updateProgress') && (
            <Card className="p-5 text-sm text-ink-2">
              <p className="flex items-center gap-2 font-medium text-ink"><i className="ti ti-eye text-lg text-ink-3" aria-hidden="true" />ดูอย่างเดียว</p>
              <p className="mt-1">คุณไม่มีการดำเนินการที่ทำได้กับ KPI นี้ในสถานะปัจจุบัน</p>
            </Card>
          )}
        </aside>
      </div>

      <Modal open={rejecting} onClose={() => setRejecting(false)} title="ตีกลับให้แก้ไข"
        footer={<>
          <Button onClick={() => setRejecting(false)}>ยกเลิก</Button>
          <Button variant="primary" loading={saving} disabled={!rejectReason.trim()}
            onClick={async () => { if (await run(() => kpiService.reject(kpi.id, rejectReason))) { setRejecting(false); setRejectReason('') } }}>
            ตีกลับเป็นแบบร่าง
          </Button>
        </>}>
        <TextArea label="เหตุผล (บังคับ)" rows={4} maxLength={1000} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)}
          placeholder="ต้องแก้อะไรก่อนจึงจะอนุมัติได้?" />
      </Modal>

      <Modal open={confirm !== null} onClose={() => setConfirm(null)} width="sm"
        title={confirm === 'delete' ? 'ลบ KPI นี้?' : 'ปิด KPI นี้?'}
        footer={<>
          <Button onClick={() => setConfirm(null)}>ยกเลิก</Button>
          <Button variant={confirm === 'delete' ? 'danger' : 'dark'} loading={saving}
            onClick={async () => {
              const ok = confirm === 'delete'
                ? await run(async () => { await kpiService.remove(kpi.id); navigate('/kpi') })
                : await run(() => kpiService.close(kpi.id))
              if (ok) setConfirm(null)
            }}>
            {confirm === 'delete' ? 'ลบ' : 'ปิด KPI'}
          </Button>
        </>}>
        <p className="text-sm text-ink-2">
          {confirm === 'delete' ? 'แบบร่างนี้จะถูกลบถาวร ย้อนกลับไม่ได้' : 'เมื่อปิดแล้วจะแก้ไขหรืออัปเดตความคืบหน้าไม่ได้อีก'}
        </p>
      </Modal>
    </Page>
  )
}
