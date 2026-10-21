import { useEffect, useState } from 'react'
import Button from '../ui/Button'
import { SelectInput, TextInput } from '../ui/Field'
import { Alert } from '../ui/Layout'
import Modal from '../ui/Modal'
import { orgService, type UserRow } from '../../services/org.service'
import { apiError } from '../../utils/errors'

/** Change a person's name, e-mail and department. The role has its own control in the list. */
export function EditUserModal({ user, onClose, onSaved }: { user: UserRow; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ firstName: user.firstName, lastName: user.lastName, email: user.email, departmentId: user.departmentId ?? '' })
  const [departments, setDepartments] = useState<{ value: string; label: string }[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    orgService.departmentOptions()
      .then((list) => {
        const options = list.map((d) => ({ value: d.id, label: d.name }))
        // A department that was switched off since still shows for the people in it, so the form does not silently change it.
        if (user.departmentId && !options.some((o) => o.value === user.departmentId)) {
          options.push({ value: user.departmentId, label: `${user.department ?? 'แผนกเดิม'} (ปิดใช้งาน)` })
        }
        setDepartments(options)
      })
      .catch(() => { /* leave the list empty; the department can still be cleared */ })
  }, [user.departmentId, user.department])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      await orgService.updateUser(user.id, { ...form, departmentId: form.departmentId || null })
      onSaved()
      onClose()
    } catch (err) {
      setError(apiError(err, 'บันทึกไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="แก้ไขข้อมูลผู้ใช้"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button type="submit" form="edit-user" variant="primary" loading={saving}>บันทึก</Button>
      </>}>
      <form id="edit-user" onSubmit={submit} className="space-y-4">
        {error && <Alert tone="error">{error}</Alert>}
        <div className="grid gap-4 sm:grid-cols-2">
          <TextInput label="ชื่อ" required value={form.firstName} autoComplete="off" onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
          <TextInput label="นามสกุล" value={form.lastName} autoComplete="off" onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
        </div>
        <TextInput label="อีเมล" type="email" required value={form.email} autoComplete="off"
          hint="ใช้เป็นชื่อเข้าสู่ระบบ ถ้าเปลี่ยน ให้แจ้งเจ้าของบัญชี" onChange={(e) => setForm({ ...form, email: e.target.value })} />
        <SelectInput label="แผนก" value={form.departmentId} placeholder="ไม่ระบุ" options={departments}
          onChange={(e) => setForm({ ...form, departmentId: e.target.value })} />
      </form>
    </Modal>
  )
}

/** An administrator sets a new password for someone who forgot theirs. */
export function ResetPasswordModal({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const [password, setPassword] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      await orgService.resetPassword(user.id, password)
      onDone()
      onClose()
    } catch (err) {
      setError(apiError(err, 'รีเซ็ตรหัสผ่านไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} width="sm" title="ตั้งรหัสผ่านใหม่"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button type="submit" form="reset-password" variant="primary" loading={saving}>ตั้งรหัสผ่านใหม่</Button>
      </>}>
      <form id="reset-password" onSubmit={submit} className="space-y-4">
        {error && <Alert tone="error">{error}</Alert>}
        <p className="text-sm text-ink-2">ตั้งรหัสผ่านใหม่ให้ <strong className="text-ink">{user.firstName} {user.lastName}</strong> แล้วแจ้งรหัสให้เจ้าของบัญชีโดยตรง</p>
        <TextInput label="รหัสผ่านใหม่" type="password" required minLength={8} autoComplete="new-password"
          hint="อย่างน้อย 8 ตัวอักษร" value={password} onChange={(e) => setPassword(e.target.value)} />
      </form>
    </Modal>
  )
}
