import { useEffect, useState } from 'react'
import Button from '../ui/Button'
import { SelectInput, TextInput } from '../ui/Field'
import { Alert } from '../ui/Layout'
import Modal from '../ui/Modal'
import api from '../../services/api'
import { apiError } from '../../utils/errors'
import { roleLabel } from '../../utils/labels'

interface Props {
  onClose: () => void
  onSuccess: () => void
}

const empty = { firstName: '', lastName: '', email: '', password: '', roleId: '', departmentId: '' }

export default function AddUserModal({ onClose, onSuccess }: Props) {
  const [form, setForm] = useState(empty)
  const [roles, setRoles] = useState<{ id: string; name: string }[]>([])
  const [departments, setDepartments] = useState<{ id: string; name: string }[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    api.get('/api/roles').then((r) => setRoles(r.data)).catch((err) => setError(apiError(err)))
    api.get('/api/departments').then((r) => setDepartments(r.data)).catch(() => {})
  }, [])

  const set = (key: keyof typeof empty) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((prev) => ({ ...prev, [key]: e.target.value }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      await api.post('/api/users', form)
      onSuccess()
      onClose()
    } catch (err) {
      setError(apiError(err, 'สร้างผู้ใช้ไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="เพิ่มผู้ใช้งาน"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button type="submit" form="add-user" variant="primary" loading={saving}>สร้างบัญชี</Button>
      </>}>
      <form id="add-user" onSubmit={submit} className="space-y-4">
        {error && <Alert tone="error">{error}</Alert>}
        <div className="grid gap-4 sm:grid-cols-2">
          <TextInput label="ชื่อ" required value={form.firstName} onChange={set('firstName')} autoComplete="off" />
          <TextInput label="นามสกุล" required value={form.lastName} onChange={set('lastName')} autoComplete="off" />
        </div>
        <TextInput label="อีเมล" type="email" required value={form.email} onChange={set('email')} autoComplete="off" />
        <TextInput label="รหัสผ่านเริ่มต้น" type="password" required minLength={8} value={form.password}
          onChange={set('password')} autoComplete="new-password" hint="อย่างน้อย 8 ตัวอักษร" />
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectInput label="บทบาท" required value={form.roleId} onChange={set('roleId')} placeholder="เลือกบทบาท"
            options={roles.map((r) => ({ value: r.id, label: roleLabel[r.name] ?? r.name }))} />
          <SelectInput label="แผนก" value={form.departmentId} onChange={set('departmentId')} placeholder="ไม่ระบุ"
            options={departments.map((d) => ({ value: d.id, label: d.name }))} />
        </div>
      </form>
    </Modal>
  )
}
