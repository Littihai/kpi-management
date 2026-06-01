import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { kpiService } from '../services/kpi.service'
import TrafficLight from '../components/TrafficLight'
import type { KpiDto } from '../types/kpi'

export default function KpiDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [kpi, setKpi] = useState<KpiDto | null>(null)
  const [progress, setProgress] = useState('')
  const [notes, setNotes] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!id) return
    kpiService.getById(id)
      .then(setKpi)
      .finally(() => setLoading(false))
  }, [id])

  const handleSubmit = async () => {
    if (!kpi) return
    setSaving(true)
    try {
      const updated = await kpiService.submit(kpi.id)
      setKpi(updated)
    } finally {
      setSaving(false)
    }
  }

  const handleApprove = async () => {
    if (!kpi) return
    setSaving(true)
    try {
      const updated = await kpiService.approve(kpi.id)
      setKpi(updated)
    } finally {
      setSaving(false)
    }
  }

  const handleUpdateProgress = async () => {
    if (!kpi || !progress) return
    setSaving(true)
    try {
      const updated = await kpiService.updateProgress({
        kpiId: kpi.id,
        progress: Number(progress),
        notes
      })
      setKpi(updated)
      setProgress('')
      setNotes('')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return (
    <div className="flex items-center justify-center h-64 text-gray-400">Loading...</div>
  )

  if (!kpi) return (
    <div className="flex items-center justify-center h-64 text-gray-400">KPI not found</div>
  )

  return (
    <div className="p-8 max-w-3xl">
      <button
        onClick={() => navigate('/kpi')}
        className="text-sm text-gray-400 hover:text-gray-600 mb-6 block"
      >
        ← Back
      </button>

      <div className="bg-white rounded-xl border border-gray-100 p-6 mb-4">
        <div className="flex justify-between items-start mb-4">
          <div>
            <h1 className="text-xl font-medium text-gray-800">{kpi.name}</h1>
            <p className="text-sm text-gray-400 mt-1">
              {kpi.departmentName} · {kpi.year}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <TrafficLight color={kpi.trafficLight} />
            <span className="font-medium text-gray-700">{kpi.progress}%</span>
          </div>
        </div>

        <p className="text-sm text-gray-600 mb-4">{kpi.description}</p>

        <div className="bg-gray-100 rounded-full h-2 mb-4">
          <div
            className={`h-2 rounded-full ${
              kpi.trafficLight === 'green' ? 'bg-green-500' :
              kpi.trafficLight === 'yellow' ? 'bg-yellow-400' :
              kpi.trafficLight === 'red' ? 'bg-red-500' : 'bg-gray-300'
            }`}
            style={{ width: `${kpi.progress}%` }}
          />
        </div>

        <div className="grid grid-cols-3 gap-4 text-sm">
          <div>
            <p className="text-gray-400">Status</p>
            <p className="font-medium text-gray-700">{kpi.statusLabel}</p>
          </div>
          <div>
            <p className="text-gray-400">Target</p>
            <p className="font-medium text-gray-700">{kpi.target}%</p>
          </div>
          <div>
            <p className="text-gray-400">Owner</p>
            <p className="font-medium text-gray-700">{kpi.ownerName}</p>
          </div>
        </div>
      </div>

      {/* Actions */}
      <div className="bg-white rounded-xl border border-gray-100 p-6 mb-4">
        <h2 className="font-medium text-gray-700 mb-4">Actions</h2>
        <div className="flex gap-3 flex-wrap">
          {kpi.statusLabel === 'Draft' && (
            <button
              onClick={handleSubmit}
              disabled={saving}
              className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700 disabled:opacity-50"
            >
              Submit for Approval
            </button>
          )}
          {kpi.statusLabel === 'PendingApproval' && (
            <button
              onClick={handleApprove}
              disabled={saving}
              className="bg-green-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-green-700 disabled:opacity-50"
            >
              Approve KPI
            </button>
          )}
        </div>
      </div>

      {/* Update Progress */}
      {kpi.statusLabel === 'Active' && (
        <div className="bg-white rounded-xl border border-gray-100 p-6">
          <h2 className="font-medium text-gray-700 mb-4">Update Progress</h2>
          <div className="space-y-3">
            <div>
              <label className="text-sm text-gray-500 block mb-1">Progress (%)</label>
              <input
                type="number"
                min="0"
                max="100"
                value={progress}
                onChange={e => setProgress(e.target.value)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="0-100"
              />
            </div>
            <div>
              <label className="text-sm text-gray-500 block mb-1">Notes</label>
              <textarea
                value={notes}
                onChange={e => setNotes(e.target.value)}
                rows={3}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
                placeholder="What was accomplished this month?"
              />
            </div>
            <button
              onClick={handleUpdateProgress}
              disabled={saving || !progress}
              className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700 disabled:opacity-50"
            >
              {saving ? 'Saving...' : 'Update Progress'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}