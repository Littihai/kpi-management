import { useState } from 'react'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { TextInput } from '../ui/Field'
import { Alert, Avatar } from '../ui/Layout'
import { authService } from '../../services/auth.service'
import { useAuthStore } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { roleLabel } from '../../utils/labels'

const MIN_LENGTH = 8

/** Who the signed-in person is, and the one thing they may change about themselves: their password. */
export default function AccountSection() {
  const user = useAuthStore((s) => s.user)
  const [form, setForm] = useState({ current: '', next: '', confirm: '' })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)

  if (!user) return null

  const problem =
    form.next && form.next.length < MIN_LENGTH ? `รหัสผ่านใหม่ต้องยาวอย่างน้อย ${MIN_LENGTH} ตัวอักษร`
    : form.confirm && form.confirm !== form.next ? 'รหัสผ่านใหม่สองช่องไม่ตรงกัน'
    : form.next && form.next === form.current ? 'รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสเดิม'
    : ''
  const ready = form.current && form.next.length >= MIN_LENGTH && form.next === form.confirm && !problem

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!ready) return
    setSaving(true)
    setError('')
    setDone(false)
    try {
      await authService.changePassword(form.current, form.next)
      setForm({ current: '', next: '', confirm: '' })
      setDone(true)
    } catch (err) {
      setError(apiError(err, 'เปลี่ยนรหัสผ่านไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  const rows: [string, string][] = [
    ['อีเมล (ใช้เข้าสู่ระบบ)', user.email],
    ['บทบาท', roleLabel[user.role] ?? user.role],
    ['แผนก', user.departmentName ?? 'ไม่ระบุ'],
    ['สิทธิ์ที่ได้รับ', `${user.permissions?.length ?? 0} รายการ`],
  ]

  return (
    <div className="space-y-5">
      <Card>
        <div className="flex items-center gap-4 px-5 py-4">
          <Avatar name={user.fullName} size={52} />
          <div className="min-w-0">
            <p className="truncate text-base font-semibold text-ink">{user.fullName}</p>
            <p className="truncate text-[13px] text-ink-3">{roleLabel[user.role] ?? user.role}</p>
          </div>
        </div>
        <dl className="divide-y divide-line border-t border-line text-[13px]">
          {rows.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-4 px-5 py-2.5">
              <dt className="shrink-0 text-ink-3">{label}</dt>
              <dd className="min-w-0 truncate text-right font-medium text-ink">{value}</dd>
            </div>
          ))}
        </dl>
        <p className="border-t border-line px-5 py-3 text-xs text-ink-3">ชื่อ อีเมล แผนก และบทบาท แก้ไขโดยผู้ดูแลระบบ</p>
      </Card>

      <Card>
        <CardHeader eyebrow="ความปลอดภัย" title="เปลี่ยนรหัสผ่าน" />
        <form onSubmit={submit} className="space-y-4 px-5 pb-5 pt-4" noValidate>
          {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
          {done && <Alert tone="success" onClose={() => setDone(false)}>เปลี่ยนรหัสผ่านแล้ว ใช้รหัสใหม่ในการเข้าสู่ระบบครั้งถัดไป</Alert>}
          <TextInput label="รหัสผ่านปัจจุบัน" type="password" autoComplete="current-password" required
            value={form.current} onChange={(e) => setForm({ ...form, current: e.target.value })} />
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput label="รหัสผ่านใหม่" type="password" autoComplete="new-password" required
              hint={`อย่างน้อย ${MIN_LENGTH} ตัวอักษร`} value={form.next} onChange={(e) => setForm({ ...form, next: e.target.value })} />
            <TextInput label="ยืนยันรหัสผ่านใหม่" type="password" autoComplete="new-password" required
              error={problem || undefined} value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} />
          </div>
          <div className="flex justify-end">
            <Button type="submit" variant="primary" loading={saving} disabled={!ready}>เปลี่ยนรหัสผ่าน</Button>
          </div>
        </form>
      </Card>
    </div>
  )
}
