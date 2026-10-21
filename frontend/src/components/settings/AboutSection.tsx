import { useState } from 'react'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { Alert } from '../ui/Layout'
import api from '../../services/api'

type Check = { ok: boolean; ms: number } | { ok: false; ms?: undefined }

/** Where the app is running and whether the server answers; useful when something looks stuck. */
export default function AboutSection() {
  const [check, setCheck] = useState<Check | null>(null)
  const [busy, setBusy] = useState(false)
  const base = String(api.defaults.baseURL ?? '')
  const desktop = typeof navigator !== 'undefined' && navigator.userAgent.includes('Electron')

  const test = async () => {
    setBusy(true)
    const start = performance.now()
    try {
      const res = await fetch(base + '/health/ready', { cache: 'no-store' })
      setCheck({ ok: res.ok, ms: Math.round(performance.now() - start) })
    } catch {
      setCheck({ ok: false })
    } finally {
      setBusy(false)
    }
  }

  const rows: [string, string][] = [
    ['ระบบ', 'Helpdesk — ศูนย์ช่วยเหลือ'],
    ['ใช้งานผ่าน', desktop ? 'แอปบน Windows' : 'เว็บเบราว์เซอร์'],
    ['เซิร์ฟเวอร์ (API)', base],
  ]

  return (
    <div className="space-y-5">
      <Card>
        <CardHeader eyebrow="ข้อมูลระบบ" title="เกี่ยวกับ" />
        <dl className="divide-y divide-line pt-3 text-[13px]">
          {rows.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-4 px-5 py-2.5">
              <dt className="shrink-0 text-ink-3">{label}</dt>
              <dd className="min-w-0 break-all text-right font-medium text-ink">{value}</dd>
            </div>
          ))}
        </dl>
      </Card>

      <Card>
        <CardHeader eyebrow="การเชื่อมต่อ" title="ทดสอบการเชื่อมต่อเซิร์ฟเวอร์"
          action={<Button size="sm" icon="plug-connected" loading={busy} onClick={test}>ทดสอบ</Button>} />
        <div className="space-y-3 px-5 pb-5 pt-3">
          <p className="text-[13px] text-ink-3">เซิร์ฟเวอร์แบบฟรีจะหลับเมื่อไม่มีคนใช้ การทดสอบครั้งแรกอาจใช้เวลาครึ่งนาที</p>
          {check && (check.ok
            ? <Alert tone="success">เซิร์ฟเวอร์พร้อมใช้งาน ตอบกลับใน {check.ms} มิลลิวินาที</Alert>
            : <Alert tone="error">เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ตรวจสอบอินเทอร์เน็ตหรือรอสักครู่แล้วลองอีกครั้ง</Alert>)}
        </div>
      </Card>
    </div>
  )
}
