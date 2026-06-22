import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { kpiService } from '../services/kpi.service'

interface Department {
  id: string
  name: string
}

export default function KpiCreatePage() {
  const navigate = useNavigate()
  const [saving, setSaving] = useState(false)
  const [departments, setDepartments] = useState<Department[]>([])
  const [loadingDepts, setLoadingDepts] = useState(true)
  const [error, setError] = useState('')

  const [form, setForm] = useState({
    name: '',
    description: '',
    year: 2026,
    target: 100,
    startDate: '',
    dueDate: '',
    departmentId: ''   // ← ไม่ hardcode แล้ว รอให้ user เลือก
  })

  // โหลด department list จาก API
  useEffect(() => {
    const fetchDepartments = async () => {
      try {
        const token = localStorage.getItem('token')
        const res = await fetch('/api/department', {
          headers: { Authorization: `Bearer ${token}` }
        })
        if (!res.ok) throw new Error('Failed to load departments')
        const data = await res.json()
        setDepartments(data)

        // ถ้ามีแค่แผนกเดียว ให้เลือกอัตโนมัติ
        if (data.length === 1) {
          setForm(prev => ({ ...prev, departmentId: data[0].id }))
        }
      } catch (err) {
        setError('ไม่สามารถโหลดรายชื่อแผนกได้')
      } finally {
        setLoadingDepts(false)
      }
    }
    fetchDepartments()
  }, [])

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>
  ) => {
    const { name, value } = e.target
    setForm(prev => ({ ...prev, [name]: value }))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')

    // Validate department
    if (!form.departmentId) {
      setError('กรุณาเลือกแผนก')
      return
    }

    // Validate dates
    if (new Date(form.startDate) > new Date(form.dueDate)) {
      setError('วันเริ่มต้องน้อยกว่าวันสิ้นสุด')
      return
    }

    setSaving(true)
    try {
      const kpi = await kpiService.create({
        ...form,
        year: Number(form.year),
        target: Number(form.target)
      })
      navigate(`/kpi/${kpi.id}`)
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'เกิดข้อผิดพลาด กรุณาลองใหม่')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="p-8 max-w-2xl">
      <button
        onClick={() => navigate('/kpi')}
        className="text-sm text-gray-400 hover:text-gray-600 mb-6 block"
      >
        ← Back
      </button>

      <h1 className="text-xl font-medium text-gray-800 mb-6">Create KPI</h1>

      {error && (
        <div className="mb-4 px-4 py-3 bg-red-50 border border-red-200 text-red-600 text-sm rounded-lg">
          {error}
        </div>
      )}

      <form onSubmit={handleSubmit} className="bg-white rounded-xl border border-gray-100 p-6 space-y-4">

        {/* Department Dropdown ← เพิ่มใหม่ */}
        <div>
          <label className="text-sm text-gray-500 block mb-1">
            Department <span className="text-red-400">*</span>
          </label>
          <select
            name="departmentId"
            value={form.departmentId}
            onChange={handleChange}
            required
            disabled={loadingDepts}
            className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white disabled:bg-gray-50 disabled:text-gray-400"
          >
            <option value="">
              {loadingDepts ? 'กำลังโหลด...' : '— เลือกแผนก —'}
            </option>
            {departments.map(dept => (
              <option key={dept.id} value={dept.id}>
                {dept.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label className="text-sm text-gray-500 block mb-1">KPI Name</label>
          <input
            name="name"
            value={form.name}
            onChange={handleChange}
            required
            className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="e.g. IT Automation 2026"
          />
        </div>

        <div>
          <label className="text-sm text-gray-500 block mb-1">Description</label>
          <textarea
            name="description"
            value={form.description}
            onChange={handleChange}
            rows={3}
            className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
            placeholder="Describe the KPI objective"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="text-sm text-gray-500 block mb-1">Year</label>
            <input
              name="year"
              type="number"
              value={form.year}
              onChange={handleChange}
              className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="text-sm text-gray-500 block mb-1">Target (%)</label>
            <input
              name="target"
              type="number"
              value={form.target}
              onChange={handleChange}
              className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="text-sm text-gray-500 block mb-1">Start Date</label>
            <input
              name="startDate"
              type="date"
              value={form.startDate}
              onChange={handleChange}
              required
              className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="text-sm text-gray-500 block mb-1">Due Date</label>
            <input
              name="dueDate"
              type="date"
              value={form.dueDate}
              onChange={handleChange}
              required
              className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
        </div>

        <div className="flex gap-3 pt-2">
          <button
            type="button"
            onClick={() => navigate('/kpi')}
            className="px-4 py-2 rounded-lg text-sm border border-gray-200 text-gray-600 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving || loadingDepts}
            className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {saving ? 'Creating...' : 'Create KPI'}
          </button>
        </div>
      </form>
    </div>
  )
}