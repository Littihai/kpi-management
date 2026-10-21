import { useCallback, useEffect, useState } from 'react'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { SelectInput, TextInput } from '../ui/Field'
import { Alert, Skeleton } from '../ui/Layout'
import { settingsService, type SettingRow } from '../../services/settings.service'
import { useAuthStore, useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'

const KEYS = {
  enabled: 'email.enabled',
  provider: 'email.provider',
  host: 'smtp.host',
  port: 'smtp.port',
  security: 'smtp.security',
  username: 'smtp.username',
  password: 'smtp.password',
  fromAddress: 'smtp.fromAddress',
  fromName: 'smtp.fromName',
  timeout: 'smtp.timeoutSeconds',
}

/** Which of the settings are saved first, so that choosing SMTP comes after the server it needs has been saved. */
const SAVE_LAST = [KEYS.provider]

/** How the system sends e-mail: Resend (set up on the server) or any SMTP server (set up here), plus a test send. */
export default function EmailSection() {
  const canManage = useCan('settings.manage')
  const myEmail = useAuthStore((s) => s.user?.email ?? '')
  const [rows, setRows] = useState<Record<string, SettingRow> | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [to, setTo] = useState(myEmail)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ ok: boolean; text: string } | null>(null)

  const load = useCallback(() => {
    settingsService.list()
      .then((list) => {
        setRows(Object.fromEntries(list.filter((r) => r.key.startsWith('email.') || r.key.startsWith('smtp.')).map((r) => [r.key, r])))
        setError('')
      })
      .catch((err) => setError(apiError(err, 'โหลดการตั้งค่าอีเมลไม่สำเร็จ')))
  }, [])
  useEffect(() => { load() }, [load])

  if (!rows) {
    return error
      ? <Alert tone="error">{error}</Alert>
      : <div className="space-y-3" role="status" aria-label="กำลังโหลด">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-24" />)}</div>
  }

  const value = (key: string) => draft[key] ?? rows[key].value
  const changedKeys = Object.keys(draft).filter((k) => draft[k] !== rows[k].value)
  const set = (key: string, v: string) => {
    setNotice('')
    setTestResult(null)
    setDraft((d) => {
      const next = { ...d }
      if (v === rows[key].value) delete next[key]; else next[key] = v
      return next
    })
  }
  const field = (key: string) => ({
    value: value(key),
    disabled: !canManage || saving,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => set(key, e.target.value),
  })
  const optionsOf = (key: string) => (rows[key].options ?? []).map((o) => ({ value: o.value, label: o.label }))

  const save = async () => {
    setSaving(true)
    setError('')
    setNotice('')
    const ordered = [...changedKeys.filter((k) => !SAVE_LAST.includes(k)), ...changedKeys.filter((k) => SAVE_LAST.includes(k))]
    const failures: string[] = []
    let saved = 0
    for (const key of ordered) {
      try {
        const updated = await settingsService.update(key, draft[key])
        setRows((r) => (r ? { ...r, [key]: updated } : r))
        setDraft((d) => { const next = { ...d }; delete next[key]; return next })
        saved++
      } catch (err) {
        failures.push(`${rows[key].label}: ${apiError(err, 'บันทึกไม่สำเร็จ')}`)
      }
    }
    setSaving(false)
    if (failures.length) setError(failures.join(' · '))
    if (saved) setNotice(`บันทึกแล้ว ${saved} รายการ มีผลทันที`)
  }

  const clearPassword = async () => {
    setSaving(true)
    try {
      const updated = await settingsService.update(KEYS.password, '')
      setRows((r) => (r ? { ...r, [KEYS.password]: updated } : r))
      setDraft((d) => { const next = { ...d }; delete next[KEYS.password]; return next })
      setNotice('ล้างรหัสผ่าน SMTP แล้ว')
    } catch (err) {
      setError(apiError(err, 'ล้างรหัสผ่านไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  const sendTest = async () => {
    setTesting(true)
    setTestResult(null)
    try {
      setTestResult({ ok: true, text: await settingsService.sendTestEmail(to.trim()) })
    } catch (err) {
      setTestResult({ ok: false, text: apiError(err, 'ส่งอีเมลทดสอบไม่สำเร็จ') })
    } finally {
      setTesting(false)
    }
  }

  const provider = value(KEYS.provider)
  const passwordSet = !rows[KEYS.password].isDefault
  const unsaved = changedKeys.length > 0

  return (
    <div className="space-y-5">
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      {notice && <Alert tone="success" onClose={() => setNotice('')}>{notice}</Alert>}
      {!canManage && <Alert tone="info">คุณดูการตั้งค่าได้อย่างเดียว การแก้ไขต้องมีสิทธิ์ "แก้ไขการตั้งค่าระบบ"</Alert>}

      <Card>
        <CardHeader eyebrow="การส่ง" title="ช่องทางส่งอีเมล" />
        <div className="space-y-4 px-5 pb-5 pt-4">
          <label className="flex items-start gap-3 text-[13px] text-ink">
            <input type="checkbox" className="mt-0.5 h-4 w-4 accent-[var(--lt-accent)]" disabled={!canManage || saving}
              checked={value(KEYS.enabled) === 'true'} onChange={(e) => set(KEYS.enabled, e.target.checked ? 'true' : 'false')} />
            <span>ส่งอีเมลแจ้งเตือน<span className="block text-xs text-ink-3">{rows[KEYS.enabled].description}</span></span>
          </label>
          <SelectInput label="ช่องทางที่ใช้ส่ง" options={optionsOf(KEYS.provider)} hint={rows[KEYS.provider].description} {...field(KEYS.provider)} />
          {provider === 'resend' && (
            <Alert tone="info">Resend ใช้คีย์และที่อยู่ผู้ส่งที่ตั้งไว้ที่เซิร์ฟเวอร์ (Resend__ApiKey, Resend__From) แก้ไขจากหน้านี้ไม่ได้ ถ้าต้องการใช้เซิร์ฟเวอร์อีเมลขององค์กร ให้กรอก SMTP ด้านล่างแล้วเลือกช่องทาง SMTP</Alert>
          )}
        </div>
      </Card>

      <Card>
        <CardHeader eyebrow="SMTP" title="เซิร์ฟเวอร์อีเมล (SMTP)"
          action={provider === 'smtp' ? <span className="rounded-full bg-green-soft px-2.5 py-0.5 text-[11.5px] font-semibold text-green-ink">กำลังใช้งาน</span> : undefined} />
        <div className="space-y-4 px-5 pb-5 pt-4">
          <div className="grid gap-4 sm:grid-cols-[1fr_140px]">
            <TextInput label="เซิร์ฟเวอร์ SMTP" placeholder="smtp.example.com" autoComplete="off" hint={rows[KEYS.host].description} {...field(KEYS.host)} />
            <TextInput label="พอร์ต" type="number" min={1} max={65535} {...field(KEYS.port)} />
          </div>
          <SelectInput label="การเข้ารหัสการเชื่อมต่อ" options={optionsOf(KEYS.security)} hint={rows[KEYS.security].description} {...field(KEYS.security)} />
          {value(KEYS.security) === 'none' && (
            <Alert tone="warning">การเชื่อมต่อแบบไม่เข้ารหัสส่งรหัสผ่านและเนื้อหาอีเมลเป็นข้อความธรรมดา ใช้เฉพาะกับเซิร์ฟเวอร์ในเครือข่ายภายในที่เชื่อถือได้</Alert>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput label="ชื่อผู้ใช้" autoComplete="off" hint={rows[KEYS.username].description} {...field(KEYS.username)} />
            <div>
              <TextInput label="รหัสผ่าน" type="password" autoComplete="new-password"
                placeholder={passwordSet ? 'ตั้งค่าไว้แล้ว — พิมพ์เพื่อเปลี่ยน' : ''}
                hint={passwordSet ? 'เก็บแบบเข้ารหัส ไม่แสดงกลับ' : 'เว้นว่างถ้าเซิร์ฟเวอร์ไม่ต้องล็อกอิน'} {...field(KEYS.password)} />
              {passwordSet && canManage && (
                <button type="button" onClick={clearPassword} disabled={saving} className="mt-1 text-xs font-medium text-red-ink hover:underline">ล้างรหัสผ่านที่บันทึกไว้</button>
              )}
            </div>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput label="ที่อยู่ผู้ส่ง" type="email" placeholder="noreply@example.com" autoComplete="off" hint={rows[KEYS.fromAddress].description} {...field(KEYS.fromAddress)} />
            <TextInput label="ชื่อผู้ส่ง" autoComplete="off" hint={rows[KEYS.fromName].description} {...field(KEYS.fromName)} />
          </div>
          <TextInput label={`เวลารอเชื่อมต่อสูงสุด (${rows[KEYS.timeout].unit})`} type="number" min={rows[KEYS.timeout].min ?? 3} max={rows[KEYS.timeout].max ?? 60}
            wrapperClassName="sm:w-56" {...field(KEYS.timeout)} />
        </div>
        {canManage && (
          <div className="flex items-center justify-end gap-2 border-t border-line bg-surface px-5 py-3">
            {unsaved && <Button variant="ghost" onClick={() => setDraft({})} disabled={saving}>ยกเลิกที่แก้</Button>}
            <Button variant="primary" icon="device-floppy" loading={saving} disabled={!unsaved} onClick={save}>บันทึก{unsaved ? ` (${changedKeys.length})` : ''}</Button>
          </div>
        )}
      </Card>

      {canManage && (
        <Card>
          <CardHeader eyebrow="ตรวจสอบ" title="ส่งอีเมลทดสอบ" />
          <div className="space-y-3 px-5 pb-5 pt-4">
            <p className="text-[13px] text-ink-3">ส่งผ่านช่องทางที่บันทึกไว้แล้วเท่านั้น {unsaved ? '— บันทึกการแก้ไขก่อนจึงจะทดสอบได้' : ''}</p>
            <div className="flex flex-wrap items-end gap-3">
              <TextInput label="ส่งไปที่" type="email" value={to} onChange={(e) => { setTo(e.target.value); setTestResult(null) }} wrapperClassName="w-full sm:w-80" />
              <Button icon="send" loading={testing} disabled={unsaved || !to.trim()} onClick={sendTest}>ส่งอีเมลทดสอบ</Button>
            </div>
            {testResult && <Alert tone={testResult.ok ? 'success' : 'error'}>{testResult.text}</Alert>}
          </div>
        </Card>
      )}
    </div>
  )
}
