import { useCallback, useEffect, useState } from 'react'
import Badge from '../ui/Badge'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { Alert, Skeleton } from '../ui/Layout'
import Modal from '../ui/Modal'
import { databaseService, type DatabaseReport, type RepairPreview } from '../../services/database.service'
import { useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import type { Tone } from '../../types/issue'

const stateLabel = { Ready: 'พร้อมใช้งาน', NeedsRepair: 'ขาดบางอย่าง', Blocked: 'ตรวจไม่ได้' } as const
const stateTone: Record<keyof typeof stateLabel, Tone> = { Ready: 'green', NeedsRepair: 'amber', Blocked: 'red' }

const problemText: Record<string, string> = {
  unreachable: 'เชื่อมต่อเซิร์ฟเวอร์ฐานข้อมูลไม่ได้ ตรวจชื่อหรือที่อยู่ พอร์ต ว่าเซิร์ฟเวอร์ทำงานอยู่ และ firewall',
  'login-failed': 'เซิร์ฟเวอร์ปฏิเสธการล็อกอิน ตรวจชื่อผู้ใช้และรหัสผ่าน (หรือบัญชี Windows)',
  'no-database': 'เซิร์ฟเวอร์ตอบ แต่ไม่มีฐานข้อมูลนี้ หรือบัญชีนี้เปิดไม่ได้ ให้สร้างด้วยสคริปต์ 01-create-database.sql',
  other: 'เชื่อมต่อไม่สำเร็จ',
}

const stepLabel: Record<string, string> = {
  'apply-migrations': 'สร้างและอัปเดตตารางตามเวอร์ชันของโปรแกรม (migration ที่ค้างอยู่)',
  'create-tables': 'สร้างตารางที่หายไป',
  'add-columns': 'เพิ่มคอลัมน์ที่หายไป',
  seed: 'ใส่ข้อมูลตั้งต้น (บทบาทและสิทธิ์) ที่ขาด',
}

function Row({ ok, warn, title, children }: { ok: boolean; warn?: boolean; title: string; children?: React.ReactNode }) {
  const icon = ok ? 'circle-check' : warn ? 'alert-triangle' : 'circle-x'
  const color = ok ? 'text-green-ink' : warn ? 'text-amber-ink' : 'text-red-ink'
  return (
    <li className="flex items-start gap-3 px-5 py-3">
      <i className={`ti ti-${icon} mt-0.5 text-lg ${color}`} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-ink">{title}</p>
        {children && <div className="mt-0.5 text-[13px] text-ink-2">{children}</div>}
      </div>
    </li>
  )
}

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
  const details: [string, string][] = [
    ['ฐานข้อมูล', r.engine === 'SqlServer' ? 'SQL Server' : r.engine === 'PostgreSql' ? 'PostgreSQL' : r.engine],
    ...(r.serverVersion ? [['เวอร์ชันเซิร์ฟเวอร์', r.serverVersion] as [string, string]] : []),
    ...(r.databaseName ? [['ชื่อฐานข้อมูล', r.databaseName] as [string, string]] : []),
    ...(r.collation ? [['การเรียงลำดับตัวอักษร', r.collation] as [string, string]] : []),
  ]

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
        <dl className="divide-y divide-line pt-3 text-[13px]">
          {details.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-4 px-5 py-2.5">
              <dt className="shrink-0 text-ink-3">{label}</dt>
              <dd className="min-w-0 break-all text-right font-medium text-ink">{value}</dd>
            </div>
          ))}
        </dl>
      </Card>

      {!r.supported && <Alert tone="info">ระบบนี้ทำงานกับฐานข้อมูลชั่วคราวในหน่วยความจำ (โหมดทดสอบ) จึงไม่มีอะไรให้ตรวจ</Alert>}
      {r.supported && !r.connected && (
        <Alert tone="error">
          <p className="font-medium">{problemText[r.connectionProblem ?? 'other']}</p>
          {r.connectionMessage && r.connectionProblem === 'other' && <p className="mt-1 break-all text-xs">{r.connectionMessage}</p>}
        </Alert>
      )}

      {r.supported && r.connected && (
        <Card>
          <CardHeader eyebrow="รายการตรวจ" title="โปรแกรมต้องการอะไร และมีครบหรือไม่" />
          <ul className="divide-y divide-line pt-3">
            <Row ok title="เชื่อมต่อฐานข้อมูลได้">บัญชีที่โปรแกรมใช้เปิดฐานข้อมูลนี้ได้</Row>
            {r.caseInsensitive !== null && (
              <Row ok={r.caseInsensitive} warn title="การเรียงลำดับตัวอักษรไม่สนตัวพิมพ์ (CI)">
                {r.caseInsensitive ? 'อีเมลที่ต่างกันแค่ตัวพิมพ์จะถือว่าซ้ำกัน ตามที่โปรแกรมต้องการ'
                  : 'ฐานข้อมูลนี้สนตัวพิมพ์ อีเมลที่ต่างกันแค่ตัวพิมพ์เล็กใหญ่จะไม่ถูกมองว่าซ้ำ สร้างฐานข้อมูลใหม่ด้วยการเรียงลำดับแบบ CI (เช่น Thai_CI_AS) จะแก้ได้'}
              </Row>
            )}
            <Row ok={r.pendingMigrations.length === 0} title="โครงสร้างตารางเป็นเวอร์ชันล่าสุด">
              {r.pendingMigrations.length === 0
                ? `ใช้ migration แล้ว ${r.appliedMigrations.length} รายการ`
                : `ยังไม่ได้ใช้ ${r.pendingMigrations.length} รายการ: ${r.pendingMigrations.join(', ')}`}
            </Row>
            <Row ok={r.missingTables.length === 0} title="มีตารางครบ">
              {r.missingTables.length === 0 ? 'ทุกตารางที่โปรแกรมต้องการมีอยู่' : `ขาด ${r.missingTables.length} ตาราง: ${r.missingTables.join(', ')}`}
            </Row>
            <Row ok={r.missingColumns.length === 0} title="มีคอลัมน์ครบ">
              {r.missingColumns.length === 0 ? 'ทุกคอลัมน์ที่โปรแกรมต้องการมีอยู่' : (
                <ul className="list-inside list-disc">{r.missingColumns.map((c) => <li key={c.table + c.column}><span className="font-mono text-[12px]">{c.table}.{c.column}</span> ({c.storeType})</li>)}</ul>
              )}
            </Row>
            {r.seed && (
              <>
                <Row ok={r.seed.missingRoles.length === 0 && r.seed.permissionsFound >= r.seed.permissionsExpected} title="ข้อมูลตั้งต้น (บทบาทและสิทธิ์)">
                  บทบาท {r.seed.rolesFound}/{r.seed.rolesExpected} · สิทธิ์ {r.seed.permissionsFound}/{r.seed.permissionsExpected}
                  {r.seed.missingRoles.length > 0 && ` · ขาด ${r.seed.missingRoles.join(', ')}`}
                </Row>
                <Row ok={r.seed.hasActiveAdmin} warn title="มีผู้ดูแลระบบที่ใช้งานอยู่">
                  {r.seed.hasActiveAdmin ? 'มีบัญชีผู้ดูแลระบบสูงสุดอย่างน้อยหนึ่งบัญชี'
                    : 'ยังไม่มีผู้ดูแลระบบ จึงเข้าสู่ระบบเพื่อตั้งค่าไม่ได้ ให้สร้างด้วยคำสั่ง create-admin (ดูคู่มือติดตั้ง)'}
                </Row>
              </>
            )}
          </ul>
        </Card>
      )}

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
