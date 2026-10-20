import { useCallback, useEffect, useState } from 'react'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { SelectInput, TextInput } from '../../components/ui/Field'
import { Alert, EmptyState, Page, PageHeader, Skeleton } from '../../components/ui/Layout'
import Modal from '../../components/ui/Modal'
import { orgService, type DepartmentRow } from '../../services/org.service'
import { apiError } from '../../utils/errors'

interface FormState { code: string; name: string; managerId: string; isActive: boolean }
const blank: FormState = { code: '', name: '', managerId: '', isActive: true }

function DepartmentModal({ department, onClose, onSaved }: {
  department: DepartmentRow | null
  onClose: () => void
  onSaved: () => void
}) {
  const editing = department !== null
  const [form, setForm] = useState<FormState>(department
    ? { code: department.code, name: department.name, managerId: department.managerId ?? '', isActive: department.isActive }
    : blank)
  const [people, setPeople] = useState<{ value: string; label: string }[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    orgService.users({ active: true, pageSize: 200 })
      .then((page) => setPeople(page.items.map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}`.trim() }))))
      .catch(() => { /* the head is optional; the form still works without the list */ })
  }, [])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    const body = { code: form.code, name: form.name, managerId: form.managerId || null, isActive: form.isActive }
    try {
      if (department) await orgService.updateDepartment(department.id, body)
      else await orgService.createDepartment(body)
      onSaved()
      onClose()
    } catch (err) {
      setError(apiError(err, 'บันทึกแผนกไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={editing ? 'แก้ไขแผนก' : 'เพิ่มแผนก'}
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button type="submit" form="department-form" variant="primary" loading={saving}>บันทึก</Button>
      </>}>
      <form id="department-form" onSubmit={submit} className="space-y-4">
        {error && <Alert tone="error">{error}</Alert>}
        <TextInput label="ชื่อแผนก" required maxLength={100} value={form.name} autoComplete="off"
          onChange={(e) => setForm({ ...form, name: e.target.value })} />
        <TextInput label="รหัสแผนก" maxLength={20} value={form.code} autoComplete="off"
          hint="ไม่บังคับ ใช้ตัวย่อสั้น ๆ เช่น IT, HR (ซ้ำกันไม่ได้)" onChange={(e) => setForm({ ...form, code: e.target.value })} />
        <SelectInput label="หัวหน้าแผนก" value={form.managerId} placeholder="ยังไม่กำหนด" options={people}
          hint="ใช้เป็นผู้อนุมัติและผู้รับแจ้งเตือนของแผนกในภายหลัง"
          onChange={(e) => setForm({ ...form, managerId: e.target.value })} />
        {editing && (
          <label className="flex cursor-pointer items-start gap-2.5 text-sm text-ink">
            <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              className="mt-0.5 h-4 w-4 rounded border-line-strong accent-accent" />
            <span>
              เปิดใช้งาน
              <span className="block text-xs text-ink-3">ถ้าปิด แผนกจะไม่ขึ้นในรายการเลือก แต่เรื่องและผู้ใช้เดิมยังอยู่ครบ</span>
            </span>
          </label>
        )}
      </form>
    </Modal>
  )
}

/** The departments of the organisation: who the requester belongs to, and who heads each one. */
export default function DepartmentsPage() {
  const [rows, setRows] = useState<DepartmentRow[] | null>(null)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState<DepartmentRow | null | 'new'>(null)

  const load = useCallback(() => orgService.departments()
    .then((list) => { setRows(list); setError('') })
    .catch((err) => setError(apiError(err, 'โหลดรายการแผนกไม่สำเร็จ'))), [])

  useEffect(() => { load() }, [load])

  return (
    <Page wide>
      <PageHeader title="แผนก" subtitle={rows ? `${rows.filter((d) => d.isActive).length} แผนกที่ใช้งานอยู่` : undefined}
        actions={<Button variant="primary" icon="plus" onClick={() => setEditing('new')}>เพิ่มแผนก</Button>} />

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      <Card>
        {!rows ? (
          <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)}</div>
        ) : rows.length === 0 ? (
          <EmptyState icon="building" title="ยังไม่มีแผนก"
            action={<Button variant="primary" icon="plus" onClick={() => setEditing('new')}>เพิ่มแผนกแรก</Button>}>
            ต้องมีแผนกก่อน ผู้ใช้จึงเลือกแผนกของตัวเองได้ และหน้าแจ้งปัญหาสำหรับคนที่เข้าสู่ระบบไม่ได้จะมีรายการให้เลือก
          </EmptyState>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-left">
              <thead>
                <tr className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                  <th scope="col" className="px-5 py-3">แผนก</th>
                  <th scope="col" className="px-3 py-3">รหัส</th>
                  <th scope="col" className="px-3 py-3">หัวหน้าแผนก</th>
                  <th scope="col" className="px-3 py-3 text-right">ผู้ใช้</th>
                  <th scope="col" className="px-3 py-3">สถานะ</th>
                  <th scope="col" className="px-5 py-3 text-right">จัดการ</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {rows.map((d) => (
                  <tr key={d.id} className={d.isActive ? '' : 'bg-surface'}>
                    <td className="px-5 py-3.5 text-sm font-medium text-ink">{d.name}</td>
                    <td className="px-3 py-3.5 text-[13px] text-ink-2">{d.code || <span className="text-ink-3">–</span>}</td>
                    <td className="px-3 py-3.5 text-[13px] text-ink-2">{d.managerName ?? <span className="text-ink-3">ยังไม่กำหนด</span>}</td>
                    <td className="tabular px-3 py-3.5 text-right text-[13px] text-ink-2">{d.userCount}</td>
                    <td className="px-3 py-3.5"><Badge tone={d.isActive ? 'green' : 'gray'} dot>{d.isActive ? 'ใช้งานอยู่' : 'ปิดใช้งาน'}</Badge></td>
                    <td className="px-5 py-3.5 text-right">
                      <Button size="sm" variant="ghost" icon="pencil" onClick={() => setEditing(d)}>แก้ไข</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {editing !== null && (
        <DepartmentModal department={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={load} />
      )}
    </Page>
  )
}
