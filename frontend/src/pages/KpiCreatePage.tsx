import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
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
      .catch((err) => setError(apiError(err, 'Could not load the form.')))
      .finally(() => setLoading(false))
  }, [id, isEdit, user?.role, user?.departmentId])

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
    const { name, value } = e.target
    setForm(prev => ({ ...prev, [name]: value }))
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

  const inputClass =
    'w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

  if (loading) return (
    <div className="flex items-center justify-center h-64 text-gray-400">Loading...</div>
  )

  return (
    <div className="p-8 max-w-2xl">
      <button
        onClick={() => navigate(isEdit ? `/kpi/${id}` : '/kpi')}
        className="text-sm text-gray-400 hover:text-gray-600 mb-6 block"
      >
        ← Back
      </button>

      <h1 className="text-xl font-medium text-gray-800 mb-6">{isEdit ? 'Edit KPI' : 'Create KPI'}</h1>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-600 text-sm rounded-lg">{error}</div>
      )}

      {departments.length === 0 && !error && (
        <div className="mb-4 p-3 bg-orange-50 text-orange-800 text-sm rounded-lg">
          Your account has no department yet, so you cannot create KPIs. Ask an administrator to assign one.
        </div>
      )}

      <form onSubmit={handleSubmit} className="bg-white rounded-xl border border-gray-100 p-6 space-y-4">
        <div>
          <label className="text-sm text-gray-500 block mb-1">KPI Name</label>
          <input
            name="name"
            value={form.name}
            onChange={handleChange}
            required
            maxLength={200}
            className={inputClass}
            placeholder="e.g. IT Automation 2026"
          />
        </div>

        <div>
          <label className="text-sm text-gray-500 block mb-1">Department</label>
          <select
            name="departmentId"
            value={form.departmentId}
            onChange={handleChange}
            required
            disabled={departments.length <= 1}
            className={`${inputClass} bg-white disabled:bg-gray-50`}
          >
            <option value="" disabled>Select a department</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id}>{d.name}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="text-sm text-gray-500 block mb-1">Description</label>
          <textarea
            name="description"
            value={form.description}
            onChange={handleChange}
            rows={3}
            className={`${inputClass} resize-none`}
            placeholder="Describe the KPI objective"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="text-sm text-gray-500 block mb-1">Year</label>
            <input name="year" type="number" value={form.year} onChange={handleChange} className={inputClass} />
          </div>
          <div>
            <label className="text-sm text-gray-500 block mb-1">Target (%)</label>
            <input
              name="target"
              type="number"
              min="1"
              max="100"
              value={form.target}
              onChange={handleChange}
              className={inputClass}
            />
          </div>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="text-sm text-gray-500 block mb-1">Start Date</label>
            <input name="startDate" type="date" value={form.startDate} onChange={handleChange} required className={inputClass} />
          </div>
          <div>
            <label className="text-sm text-gray-500 block mb-1">Due Date</label>
            <input
              name="dueDate"
              type="date"
              value={form.dueDate}
              min={form.startDate || undefined}
              onChange={handleChange}
              required
              className={inputClass}
            />
          </div>
        </div>

        <div className="flex gap-3 pt-2">
          <button
            type="button"
            onClick={() => navigate(isEdit ? `/kpi/${id}` : '/kpi')}
            className="px-4 py-2 rounded-lg text-sm border border-gray-200 text-gray-600 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving || departments.length === 0}
            className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {saving ? 'Saving...' : isEdit ? 'Save changes' : 'Create KPI'}
          </button>
        </div>
      </form>
    </div>
  )
}
