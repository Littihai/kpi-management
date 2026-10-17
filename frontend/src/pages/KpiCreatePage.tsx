import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import Button from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { SelectInput, TextArea, TextInput } from '../components/ui/Field'
import { Alert, Page, PageHeader, PageSkeleton } from '../components/ui/Layout'
import { kpiService } from '../services/kpi.service'
import { useAuthStore } from '../stores/authStore'
import { apiError } from '../utils/errors'
import type { DepartmentOption } from '../types/kpi'

const emptyForm = {
  name: '',
  description: '',
  year: new Date().getFullYear(),
  target: 100,
  startDate: '',
  dueDate: '',
  departmentId: '',
}

/** Create a KPI, or edit a Draft when the route has an :id. */
export default function KpiCreatePage() {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const isEdit = Boolean(id)
  const user = useAuthStore((s) => s.user)

  const [saving, setSaving] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [departments, setDepartments] = useState<DepartmentOption[]>([])
  const [form, setForm] = useState(emptyForm)

  useEffect(() => {
    const seesAll = user?.role === 'SuperAdmin' || user?.role === 'Director'

    Promise.all([
      kpiService.departments(),
      isEdit ? kpiService.getById(id!) : Promise.resolve(null),
    ])
      .then(([all, kpi]) => {
        // Managers can only work in their own department; the server enforces this too.
        const options = seesAll ? all : all.filter((d) => d.id === user?.departmentId)
        setDepartments(options)

        if (kpi) {
          setForm({
            name: kpi.name,
            description: kpi.description,
            year: kpi.year,
            target: kpi.target,
            startDate: kpi.startDate.slice(0, 10),
            dueDate: kpi.dueDate.slice(0, 10),
            departmentId: kpi.departmentId,
          })
        } else if (options.length === 1) {
          setForm((prev) => ({ ...prev, departmentId: options[0].id }))
        }
      })
      .catch((err) => setError(apiError(err, 'โหลดฟอร์มไม่สำเร็จ')))
      .finally(() => setLoading(false))
  }, [id, isEdit, user?.role, user?.departmentId])

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
    const { name, value } = e.target
    setForm((prev) => ({ ...prev, [name]: value }))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      const payload = { ...form, year: Number(form.year), target: Number(form.target) }
      const kpi = isEdit
        ? await kpiService.update(id!, payload)
        : await kpiService.create(payload)
      navigate(`/kpi/${kpi.id}`)
    } catch (err) {
      setError(apiError(err))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Page><PageSkeleton /></Page>

  const back = isEdit ? `/kpi/${id}` : '/kpi'

  return (
    <Page>
      <PageHeader
        back={<Link to={back} className="inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink"><i className="ti ti-arrow-left" aria-hidden="true" />กลับ</Link>}
        title={isEdit ? 'แก้ไข KPI' : 'สร้าง KPI'}
        subtitle={isEdit ? 'แก้ไขได้เฉพาะแบบร่าง' : 'สร้างเป็นแบบร่างก่อน แล้วค่อยส่งขออนุมัติ'} />

      <form onSubmit={handleSubmit} className="mx-auto max-w-2xl">
        <Card className="space-y-5 p-5 sm:p-6">
          {error && <Alert tone="error">{error}</Alert>}

          {departments.length === 0 && !error && (
            <Alert tone="warning">บัญชีของคุณยังไม่ได้ระบุแผนก จึงยังสร้าง KPI ไม่ได้ กรุณาแจ้งผู้ดูแลระบบให้กำหนดแผนกให้</Alert>
          )}

          <TextInput label="ชื่อ KPI" name="name" required maxLength={200} value={form.name} onChange={handleChange}
            placeholder="เช่น ลดเวลาระบบหยุดทำงาน 30%" />

          <SelectInput label="แผนก" name="departmentId" required value={form.departmentId} onChange={handleChange}
            disabled={departments.length <= 1} placeholder="เลือกแผนก"
            options={departments.map((d) => ({ value: d.id, label: d.name }))} />

          <TextArea label="รายละเอียด" name="description" rows={3} value={form.description} onChange={handleChange}
            placeholder="เป้าหมายและวิธีวัดผล" />

          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput label="ปี (ค.ศ.)" name="year" type="number" value={form.year} onChange={handleChange} />
            <TextInput label="เป้าหมาย (%)" name="target" type="number" min={1} max={100} value={form.target} onChange={handleChange}
              hint="ตั้งได้ 1–100" />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput label="วันที่เริ่ม" name="startDate" type="date" required value={form.startDate} onChange={handleChange} />
            <TextInput label="วันครบกำหนด" name="dueDate" type="date" required value={form.dueDate}
              min={form.startDate || undefined} onChange={handleChange} />
          </div>

          <div className="flex justify-end gap-2 border-t border-line pt-5">
            <Button onClick={() => navigate(back)}>ยกเลิก</Button>
            <Button type="submit" variant="primary" icon="device-floppy" loading={saving} disabled={departments.length === 0}>
              {isEdit ? 'บันทึกการแก้ไข' : 'สร้าง KPI'}
            </Button>
          </div>
        </Card>
      </form>
    </Page>
  )
}
