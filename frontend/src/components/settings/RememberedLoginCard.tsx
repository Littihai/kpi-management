import { useEffect, useState } from 'react'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { canRememberPassword, forgetRemembered, loadRemembered, type Remembered } from '../../lib/rememberLogin'

/** What this computer remembers for the login page, with a way to forget it. Shown only when there is something. */
export default function RememberedLoginCard() {
  const [saved, setSaved] = useState<Remembered | null>(null)
  const [withPassword, setWithPassword] = useState(false)
  const [version, setVersion] = useState(0) // bumped after forgetting, to read again

  useEffect(() => {
    let alive = true
    Promise.all([loadRemembered(), canRememberPassword()]).then(([found, can]) => {
      if (!alive) return
      setSaved(found)
      setWithPassword(can)
    })
    return () => { alive = false }
  }, [version])

  if (!saved) return null

  return (
    <Card>
      <CardHeader eyebrow="เครื่องนี้" title="การเข้าสู่ระบบที่จำไว้" />
      <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 px-5 pb-5 pt-4">
        <div className="min-w-0 flex-1 basis-64">
          <p className="truncate text-sm font-semibold text-ink">{saved.email}</p>
          <p className="mt-0.5 text-xs text-ink-3">
            {saved.password || withPassword
              ? 'หน้าเข้าสู่ระบบกรอกอีเมลและรหัสผ่านนี้ให้ เก็บแบบเข้ารหัสด้วยบัญชี Windows ในเครื่องนี้เท่านั้น'
              : 'หน้าเข้าสู่ระบบกรอกอีเมลนี้ให้ ส่วนรหัสผ่านอยู่ในตัวจัดการรหัสผ่านของเบราว์เซอร์'}
          </p>
        </div>
        <Button icon="trash" onClick={async () => { await forgetRemembered(); setVersion((v) => v + 1) }}>ลืมที่จำไว้</Button>
      </div>
    </Card>
  )
}
