import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import * as signalR from '@microsoft/signalr'
import {
  createKpiConnection,
  type KpiChangedEvent,
  type RealtimeNotification,
} from '../services/realtime'
import { RealtimeContext, type KpiHandler, type RealtimeStatus } from './realtimeContext'

interface Toast extends RealtimeNotification {
  id: number
}

const levelStyle: Record<RealtimeNotification['level'], string> = {
  info: 'border-blue-200 bg-blue-50 text-blue-800',
  success: 'border-green-200 bg-green-50 text-green-800',
  warning: 'border-orange-200 bg-orange-50 text-orange-800',
  error: 'border-red-200 bg-red-50 text-red-800',
}

/**
 * Keeps one SignalR connection open while the user is signed in, shows incoming
 * notifications as toasts, and lets pages refresh themselves when a KPI changes.
 */
export function RealtimeProvider({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate()
  const [status, setStatus] = useState<RealtimeStatus>('connecting')
  const [toasts, setToasts] = useState<Toast[]>([])
  const handlers = useRef(new Set<KpiHandler>())
  const nextId = useRef(1)

  const subscribe = useCallback((handler: KpiHandler) => {
    handlers.current.add(handler)
    return () => { handlers.current.delete(handler) }
  }, [])

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id))
  }, [])

  useEffect(() => {
    const connection = createKpiConnection()
    let disposed = false
    const registered = handlers.current

    connection.on('KpiChanged', (event: KpiChangedEvent) => {
      handlers.current.forEach((handler) => handler(event))
    })

    connection.on('Notification', (notification: RealtimeNotification) => {
      const id = nextId.current++
      setToasts((current) => [...current.slice(-3), { ...notification, id }])
      window.setTimeout(() => dismiss(id), 8000)
    })

    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => setStatus('connected'))
    connection.onclose(() => { if (!disposed) setStatus('offline') })

    connection.start()
      .then(() => { if (!disposed) setStatus('connected') })
      .catch(() => { if (!disposed) setStatus('offline') })

    return () => {
      disposed = true
      registered.clear()
      // Stopping while negotiation is in flight (React StrictMode) rejects harmlessly.
      if (connection.state !== signalR.HubConnectionState.Disconnected) {
        connection.stop().catch(() => {})
      }
    }
  }, [dismiss])

  const value = useMemo(() => ({ status, subscribe }), [status, subscribe])

  return (
    <RealtimeContext.Provider value={value}>
      {children}

      <div className="fixed top-4 right-4 z-50 flex flex-col gap-2 w-80" aria-live="polite">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            role="status"
            className={`border rounded-lg px-4 py-3 shadow-sm text-sm ${levelStyle[toast.level]} ${toast.kpiId ? 'cursor-pointer' : ''}`}
            onClick={() => {
              if (toast.kpiId) navigate(`/kpi/${toast.kpiId}`)
              dismiss(toast.id)
            }}
          >
            <div className="flex justify-between gap-2">
              <span className="font-medium">{toast.title}</span>
              <button
                aria-label="Dismiss"
                className="opacity-60 hover:opacity-100"
                onClick={(e) => { e.stopPropagation(); dismiss(toast.id) }}
              >
                ×
              </button>
            </div>
            <p className="mt-0.5 opacity-90">{toast.message}</p>
          </div>
        ))}
      </div>

      {(status === 'reconnecting' || status === 'offline') && (
        <div className="fixed bottom-4 right-4 z-50 text-xs bg-gray-800 text-white rounded-full px-3 py-1.5 opacity-90">
          {status === 'reconnecting' ? 'Reconnecting to live updates…' : 'Live updates unavailable'}
        </div>
      )}
    </RealtimeContext.Provider>
  )
}
