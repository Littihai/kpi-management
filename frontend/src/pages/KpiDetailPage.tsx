import { useCallback, useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { kpiService } from '../services/kpi.service'
import TrafficLight from '../components/TrafficLight'
import { useKpiEvents } from '../components/realtimeContext'
import { apiError } from '../utils/errors'
import type { KpiActionName, KpiDto } from '../types/kpi'

export default function KpiDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [kpi, setKpi] = useState<KpiDto | null>(null)
  const [progress, setProgress] = useState('')
  const [notes, setNotes] = useState('')
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notFound, setNotFound] = useState(false)

  const load = useCallback(() => {
    if (!id) return Promise.resolve()
    return kpiService.getById(id)
      .then((data) => { setKpi(data); setNotFound(false) })
      .catch((err) => {
        if (err?.response?.status === 404) setNotFound(true)
        else setError(apiError(err, 'Could not load this KPI.'))
      })
  }, [id])

  useEffect(() => {
    load().finally(() => setLoading(false))
  }, [load])

  // Someone else changed this KPI (or deleted it): show the latest state.
  useKpiEvents((event) => {
    if (event.kpiId !== id) return
    if (event.action === 'deleted') navigate('/kpi')
    else load()
  })

  const can = (action: KpiActionName) => kpi?.allowedActions.includes(action) ?? false

  const run = async (action: () => Promise<KpiDto | void>) => {
    setSaving(true)
    setError('')
    try {
      const updated = await action()
      if (updated) setKpi(updated)
    } catch (err) {
      setError(apiError(err))
      load() // the KPI may have changed under us; show what is true now
    } finally {
      setSaving(false)
    }
  }

  const handleReject = () =>
    run(async () => {
      const updated = await kpiService.reject(kpi!.id, rejectReason)
      setRejecting(false)
      setRejectReason('')
      return updated
    })

  const handleUpdateProgress = () =>
    run(async () => {
      const updated = await kpiService.updateProgress({
        kpiId: kpi!.id,
        progress: Number(progress),
        notes,
      })
      setProgress('')
      setNotes('')
      return updated
    })

  const handleDelete = () => {
    if (!confirm('Delete this draft KPI? This cannot be undone.')) return
    run(async () => {
      await kpiService.remove(kpi!.id)
      navigate('/kpi')
    })
  }

  if (loading) return (
    <div className="flex items-center justify-center h-64 text-gray-400">Loading...</div>
  )

  if (notFound || !kpi) return (
    <div className="flex flex-col items-center justify-center h-64 text-gray-400 gap-3">
      <span>{error || 'KPI not found'}</span>
      <button onClick={() => navigate('/kpi')} className="text-sm text-indigo-600 hover:underline">
        Back to KPIs
      </button>
    </div>
  )

  const hasActions = ['submit', 'approve', 'reject', 'close', 'edit', 'delete'].some(a => can(a as KpiActionName))

  return (
    <div className="p-8 max-w-3xl">
      <button
        onClick={() => navigate('/kpi')}
        className="text-sm text-gray-400 hover:text-gray-600 mb-6 block"
      >
        ← Back
      </button>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-600 text-sm rounded-lg">{error}</div>
      )}

      {kpi.statusLabel === 'Draft' && kpi.rejectReason && (
        <div className="mb-4 p-3 bg-orange-50 border border-orange-100 text-orange-800 text-sm rounded-lg">
          <span className="font-medium">Returned for changes:</span> {kpi.rejectReason}
        </div>
      )}

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
            style={{ width: `${Math.min(100, (kpi.progress / (kpi.target || 100)) * 100)}%` }}
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

      {/* Actions: shown only when the server says the signed-in user may take them */}
      {hasActions && (
        <div className="bg-white rounded-xl border border-gray-100 p-6 mb-4">
          <h2 className="font-medium text-gray-700 mb-4">Actions</h2>
          <div className="flex gap-3 flex-wrap">
            {can('submit') && (
              <button
                onClick={() => run(() => kpiService.submit(kpi.id))}
                disabled={saving}
                className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-blue-700 disabled:opacity-50"
              >
                Submit for Approval
              </button>
            )}
            {can('edit') && (
              <button
                onClick={() => navigate(`/kpi/${kpi.id}/edit`)}
                disabled={saving}
                className="border border-gray-200 text-gray-600 px-4 py-2 rounded-lg text-sm hover:bg-gray-50 disabled:opacity-50"
              >
                Edit
              </button>
            )}
            {can('approve') && (
              <button
                onClick={() => run(() => kpiService.approve(kpi.id))}
                disabled={saving}
                className="bg-green-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-green-700 disabled:opacity-50"
              >
                Approve KPI
              </button>
            )}
            {can('reject') && !rejecting && (
              <button
                onClick={() => setRejecting(true)}
                disabled={saving}
                className="border border-orange-200 text-orange-700 px-4 py-2 rounded-lg text-sm hover:bg-orange-50 disabled:opacity-50"
              >
                Reject…
              </button>
            )}
            {can('close') && (
              <button
                onClick={() => {
                  if (confirm('Close this KPI? It can no longer be changed afterwards.')) run(() => kpiService.close(kpi.id))
                }}
                disabled={saving}
                className="bg-gray-800 text-white px-4 py-2 rounded-lg text-sm hover:bg-gray-900 disabled:opacity-50"
              >
                Close KPI
              </button>
            )}
            {can('delete') && (
              <button
                onClick={handleDelete}
                disabled={saving}
                className="border border-red-200 text-red-600 px-4 py-2 rounded-lg text-sm hover:bg-red-50 disabled:opacity-50"
              >
                Delete
              </button>
            )}
          </div>

          {can('reject') && rejecting && (
            <div className="mt-4 space-y-3">
              <label className="text-sm text-gray-500 block">Reason (required)</label>
              <textarea
                value={rejectReason}
                onChange={e => setRejectReason(e.target.value)}
                rows={3}
                maxLength={1000}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-orange-400 resize-none"
                placeholder="What needs to change before this can be approved?"
              />
              <div className="flex gap-2">
                <button
                  onClick={handleReject}
                  disabled={saving || !rejectReason.trim()}
                  className="bg-orange-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-orange-700 disabled:opacity-50"
                >
                  Send back to draft
                </button>
                <button
                  onClick={() => { setRejecting(false); setRejectReason('') }}
                  className="px-4 py-2 rounded-lg text-sm text-gray-500 hover:bg-gray-50"
                >
                  Cancel
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Update Progress: Active, AtRisk, Delayed (and Completed, to correct a mistake) */}
      {can('updateProgress') && (
        <div className="bg-white rounded-xl border border-gray-100 p-6">
          <h2 className="font-medium text-gray-700 mb-4">Update Progress</h2>
          <div className="space-y-3">
            <div>
              <label className="text-sm text-gray-500 block mb-1">Progress (max {kpi.target})</label>
              <input
                type="number"
                min="0"
                max={kpi.target}
                value={progress}
                onChange={e => setProgress(e.target.value)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder={`0-${kpi.target}`}
              />
            </div>
            <div>
              <label className="text-sm text-gray-500 block mb-1">Notes</label>
              <textarea
                value={notes}
                onChange={e => setNotes(e.target.value)}
                rows={3}
                maxLength={1000}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
                placeholder="What was accomplished this month?"
              />
            </div>
            <button
              onClick={handleUpdateProgress}
              disabled={saving || progress === ''}
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
