import { useCallback, useEffect, useState } from 'react'
import Badge from '../ui/Badge'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { Alert, Skeleton } from '../ui/Layout'
import Modal from '../ui/Modal'
import { databaseService, type DatabaseReport, type RepairPreview } from '../../services/database.service'
import { useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { ConnectionProblem, DatabaseChecklist, DatabaseFacts } from './DatabaseChecklist'
import { stateLabel, stateTone, stepLabel } from './databaseText'

/** Is the database complete? What is missing compared with what the program needs, and a way to create what is missing. */
export default function DatabaseSection() {
  const canManage = useCan('settings.manage')
  const [report, setReport] = useState<DatabaseReport | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [preview, setPreview] = useState<RepairPreview | null>(null)
  const [confirm, setConfirm] = useState(false)
  const [repairing, setRepairing] = useState(false)
  const [notice, setNotice] = useState('')

  const load = useCallback(() => {
    databaseService.status()
      .then(async (next) => {
        setReport(next)
        setPreview(next.state === 'NeedsRepair' && canManage ? await databaseService.preview() : null)
        setError('')
      })
      .catch((err) => setError(apiError(err, 'ตรวจฐานข้อมูลไม่สำเร็จ')))
      .finally(() => setLoading(false))
  }, [canManage])
  useEffect(() => { load() }, [load])

  const repair = async () => {
    setRepairing(true)
    setError('')
    try {
      const result = await databaseService.repair({ applyMigrations: true, createMissingObjects: true, seed: true })
      setConfirm(false)
      setReport(result.report)
      setNotice(result.done.length ? `ซ่อมแล้ว: ${result.done.map((s) => stepLabel[s] ?? s).join(' · ')}` : 'ไม่มีอะไรต้องซ่อม')
      setPreview(result.report.state === 'NeedsRepair' ? await databaseService.preview() : null)
    } catch (err) {
      setConfirm(false)
      setError(apiError(err, 'ซ่อมฐานข้อมูลไม่สำเร็จ ไม่มีอะไรถูกลบหรือแก้ของเดิม'))
    } finally {
      setRepairing(false)
    }
  }

  if (!report) {
    return error ? <Alert tone="error">{error}</Alert> : <div className="space-y-3" role="status" aria-label="กำลังโหลด">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-20" />)}</div>
  }

  const r = report

  return (
    <div className="space-y-5">
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      {notice && <Alert tone="success" onClose={() => setNotice('')}>{notice}</Alert>}

      <Card>
        <CardHeader eyebrow="สถานะ" title="ฐานข้อมูล"
          action={<div className="flex items-center gap-2">
            <Badge tone={stateTone[r.state]} dot>{stateLabel[r.state]}</Badge>
            <Button size="sm" icon="refresh" loading={loading} onClick={() => { setLoading(true); load() }}>ตรวจอีกครั้ง</Button>
          </div>} />
        <DatabaseFacts report={r} />
      </Card>

      {!r.supported && <Alert tone="info">ระบบนี้ทำงานกับฐานข้อมูลชั่วคราวในหน่วยความจำ (โหมดทดสอบ) จึงไม่มีอะไรให้ตรวจ</Alert>}
      {r.supported && !r.connected && <ConnectionProblem report={r} />}

      {r.supported && r.connected && <DatabaseChecklist report={r} />}

      {r.state === 'NeedsRepair' && preview && preview.steps.length > 0 && (
        <Card>
          <CardHeader eyebrow="การซ่อม" title="สร้างสิ่งที่ขาดโดยไม่แตะของเดิม" />
          <div className="space-y-3 px-5 pb-5 pt-3">
            <ol className="list-inside list-decimal space-y-1 text-[13px] text-ink">
              {preview.steps.map((s) => <li key={s}>{stepLabel[s] ?? s}</li>)}
            </ol>
            <p className="text-xs text-ink-3">ซ่อมได้เฉพาะแบบเพิ่ม ไม่ลบหรือแก้ตารางและข้อมูลที่มีอยู่ ทำในรายการเดียวกัน ถ้ามีข้อผิดพลาดจะยกเลิกทั้งชุดของส่วนตารางและคอลัมน์</p>
            {preview.script && (
              <details className="rounded-control border border-line bg-surface">
                <summary className="cursor-pointer px-3.5 py-2 text-[13px] font-medium text-ink">ดูคำสั่ง SQL ที่จะรัน</summary>
                <pre className="max-h-72 overflow-auto border-t border-line p-3.5 text-[12px] leading-relaxed text-ink-2">{preview.script}</pre>
              </details>
            )}
            {canManage
              ? <div className="flex justify-end"><Button variant="primary" icon="tool" onClick={() => setConfirm(true)}>ซ่อมให้อัตโนมัติ</Button></div>
              : <Alert tone="info">การซ่อมต้องมีสิทธิ์ "แก้ไขการตั้งค่าระบบ"</Alert>}
          </div>
        </Card>
      )}

      <Modal open={confirm} onClose={() => setConfirm(false)} width="sm" title="ซ่อมฐานข้อมูล?"
        footer={<><Button onClick={() => setConfirm(false)}>ยกเลิก</Button><Button variant="primary" loading={repairing} onClick={repair}>ซ่อมเลย</Button></>}>
        <p className="text-sm text-ink-2">ระบบจะสร้างเฉพาะสิ่งที่ขาดตามรายการ ไม่ลบหรือแก้ข้อมูลเดิม ควรสำรองฐานข้อมูลไว้ก่อนเสมอ</p>
      </Modal>
    </div>
  )
}
