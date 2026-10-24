import { useEffect, useState } from 'react'
import Button from '../ui/Button'
import { TextInput } from '../ui/Field'
import { Alert } from '../ui/Layout'
import { updateRememberedPassword } from '../../lib/rememberLogin'
import { authService, type PasswordPolicy } from '../../services/auth.service'
import { useAuthStore } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { checkPassword } from './passwordRules'

const FALLBACK: PasswordPolicy = { minLength: 8, requireUpper: false, requireLower: false, requireDigit: false, requireSpecial: false, historyCount: 0, maxAgeDays: 0 }

/** Current password, new password twice, and a live checklist of what the policy asks for. */
export default function ChangePasswordForm({ onChanged }: { onChanged: () => void }) {
  const email = useAuthStore((s) => s.user?.email)
  const [policy, setPolicy] = useState<PasswordPolicy>(FALLBACK)
  const [form, setForm] = useState({ current: '', next: '', confirm: '' })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    authService.passwordPolicy().then(setPolicy).catch(() => { /* the server still checks; the form uses the default hints */ })
  }, [])

  const rules = checkPassword(policy, form.next)
  const mismatch = form.confirm.length > 0 && form.confirm !== form.next
  const same = form.next.length > 0 && form.next === form.current
  const ready = form.current.length > 0 && rules.every((r) => r.ok) && form.next === form.confirm && !same

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!ready) return
    setSaving(true)
    setError('')
    try {
      await authService.changePassword(form.current, form.next)
      // a password this computer remembers for this account would be the old one from now on
      if (email) void updateRememberedPassword(email, form.next)
      setForm({ current: '', next: '', confirm: '' })
      onChanged()
    } catch (err) {
      setError(apiError(err, 'เปลี่ยนรหัสผ่านไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      <TextInput label="รหัสผ่านปัจจุบัน" type="password" autoComplete="current-password" required
        value={form.current} onChange={(e) => setForm({ ...form, current: e.target.value })} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextInput label="รหัสผ่านใหม่" type="password" autoComplete="new-password" required
          error={same ? 'รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสเดิม' : undefined}
          value={form.next} onChange={(e) => setForm({ ...form, next: e.target.value })} />
        <TextInput label="ยืนยันรหัสผ่านใหม่" type="password" autoComplete="new-password" required
          error={mismatch ? 'รหัสผ่านใหม่สองช่องไม่ตรงกัน' : undefined}
          value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} />
      </div>
      <ul className="grid gap-x-6 gap-y-1 text-[12.5px] sm:grid-cols-2" aria-label="ข้อกำหนดรหัสผ่าน">
        {rules.map((r) => (
          <li key={r.label} className={`flex items-center gap-1.5 ${form.next ? (r.ok ? 'text-green-ink' : 'text-red-ink') : 'text-ink-3'}`}>
            <i className={`ti ti-${form.next && r.ok ? 'circle-check' : 'circle'} text-sm`} aria-hidden="true" />{r.label}
          </li>
        ))}
        {policy.historyCount > 0 && (
          <li className="flex items-center gap-1.5 text-ink-3"><i className="ti ti-history text-sm" aria-hidden="true" />ไม่ซ้ำกับ {policy.historyCount} รหัสล่าสุด</li>
        )}
      </ul>
      <div className="flex justify-end">
        <Button type="submit" variant="primary" loading={saving} disabled={!ready}>เปลี่ยนรหัสผ่าน</Button>
      </div>
    </form>
  )
}
