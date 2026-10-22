import { useState } from 'react'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { Alert } from '../ui/Layout'
import ServerAddressDialog from '../ServerAddressDialog'
import api from '../../services/api'
import { serverChoiceAllowed } from '../../lib/server'
import { useDesktopUpdate } from '../../lib/desktop'

type Check = { ok: boolean; ms: number } | { ok: false; ms?: undefined }

/** Where the app is running and whether the server answers; useful when something looks stuck. */
export default function AboutSection() {
  const [check, setCheck] = useState<Check | null>(null)
  const [busy, setBusy] = useState(false)
  const base = String(api.defaults.baseURL ?? '')
  const app = typeof window !== 'undefined' ? window.desktop : undefined
  const desktop = !!app || (typeof navigator !== 'undefined' && navigator.userAgent.includes('Electron'))
  const update = useDesktopUpdate()
  const [checking, setChecking] = useState(false)
  const [asked, setAsked] = useState(false)
  const [serverOpen, setServerOpen] = useState(false)

  const checkUpdate = async () => {
    setAsked(true)
    setChecking(true)
    try { await app?.checkForUpdates() } finally { setChecking(false) }
  }

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
    ...(app ? [['เวอร์ชันแอป', app.version] as [string, string]] : []),
    ['เซิร์ฟเวอร์ (API)', base || window.location.origin],
  ]

  return (
    <div className="space-y-5">
      <Card>
        <CardHeader eyebrow="ข้อมูลระบบ" title="เกี่ยวกับ"
          action={serverChoiceAllowed() ? <Button size="sm" icon="server" onClick={() => setServerOpen(true)}>เปลี่ยนเซิร์ฟเวอร์</Button> : undefined} />
        <dl className="divide-y divide-line pt-3 text-[13px]">
          {rows.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-4 px-5 py-2.5">
              <dt className="shrink-0 text-ink-3">{label}</dt>
              <dd className="min-w-0 break-all text-right font-medium text-ink">{value}</dd>
            </div>
          ))}
        </dl>
      </Card>

      {app && (
        <Card>
          <CardHeader eyebrow="แอปบน Windows" title="อัปเดตแอป"
            action={<Button size="sm" icon="refresh" loading={checking || update?.status === 'checking'} onClick={checkUpdate}>ตรวจหาอัปเดต</Button>} />
          <div className="space-y-3 px-5 pb-5 pt-3">
            <p className="text-[13px] text-ink-3">แอปตรวจหาเวอร์ชันใหม่เองทุกไม่กี่ชั่วโมง และอัปเดตให้เมื่อปิดโปรแกรม</p>
            {asked && update?.status === 'not-available' && <Alert tone="success">คุณใช้เวอร์ชันล่าสุดแล้ว</Alert>}
            {update?.status === 'available' && <Alert tone="info">พบเวอร์ชันใหม่ {update.version} กำลังเริ่มดาวน์โหลด</Alert>}
            {update?.status === 'downloading' && <Alert tone="info">กำลังดาวน์โหลดเวอร์ชันใหม่ {update.percent ?? 0}%</Alert>}
            {update?.status === 'downloaded' && (
              <Alert tone="success">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>ดาวน์โหลดเวอร์ชัน {update.version} เรียบร้อย พร้อมอัปเดต</span>
                  <Button size="sm" variant="primary" onClick={() => app.restartToUpdate()}>รีสตาร์ตเพื่ออัปเดต</Button>
                </div>
              </Alert>
            )}
            {asked && update?.status === 'error' && <Alert tone="error">ตรวจหาอัปเดตไม่สำเร็จ ตรวจสอบอินเทอร์เน็ตแล้วลองอีกครั้ง</Alert>}
          </div>
        </Card>
      )}

      {serverOpen && <ServerAddressDialog open onClose={() => setServerOpen(false)} />}

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
