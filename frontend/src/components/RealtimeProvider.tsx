import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import * as signalR from '@microsoft/signalr'
import {
  createKpiConnection,
  type IssueChangedEvent,
  type KpiChangedEvent,
  type RealtimeNotification,
} from '../services/realtime'
import {
  RealtimeContext,
  type IssueHandler,
  type KpiHandler,
  type RealtimeStatus,
} from './realtimeContext'

interface Toast extends RealtimeNotification {
  id: number
}

const levelStyle: Record<RealtimeNotification['level'], { icon: string; bar: string }> = {
  info: { icon: 'info-circle', bar: 'bg-blue-dot' },
  success: { icon: 'circle-check', bar: 'bg-green-dot' },
  warning: { icon: 'alert-triangle', bar: 'bg-amber-dot' },
  error: { icon: 'alert-circle', bar: 'bg-red-dot' },
}

/**
 * Keeps one SignalR connection open while the user is signed in, shows incoming
 * notifications as toasts, and lets pages refresh themselves when a KPI or issue changes.
 */
export function RealtimeProvider({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate()
  const [status, setStatus] = useState<RealtimeStatus>('connecting')
  const [toasts, setToasts] = useState<Toast[]>([])
  const kpiHandlers = useRef(new Set<KpiHandler>())
  const issueHandlers = useRef(new Set<IssueHandler>())
  const nextId = useRef(1)

  const subscribe = useCallback((handler: KpiHandler) => {
    kpiHandlers.current.add(handler)
    return () => { kpiHandlers.current.delete(handler) }
  }, [])

  const subscribeIssues = useCallback((handler: IssueHandler) => {
    issueHandlers.current.add(handler)
    return () => { issueHandlers.current.delete(handler) }
  }, [])

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id))
  }, [])

  useEffect(() => {
    const connection = createKpiConnection()
    let disposed = false
    const kpis = kpiHandlers.current
    const issues = issueHandlers.current

    connection.on('KpiChanged', (event: KpiChangedEvent) => {
      kpis.forEach((handler) => handler(event))
    })

    connection.on('IssueChanged', (event: IssueChangedEvent) => {
      issues.forEach((handler) => handler(event))
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
      kpis.clear()
      issues.clear()
      // Stopping while negotiation is in flight (React StrictMode) rejects harmlessly.
      if (connection.state !== signalR.HubConnectionState.Disconnected) {
        connection.stop().catch(() => {})
      }
    }
  }, [dismiss])

  const value = useMemo(() => ({ status, subscribe, subscribeIssues }), [status, subscribe, subscribeIssues])

  const open = (toast: Toast) => {
    if (toast.issueId) navigate(`/issues/${toast.issueId}`)
    else if (toast.kpiId) navigate(`/kpi/${toast.kpiId}`)
    dismiss(toast.id)
  }

  return (
    <RealtimeContext.Provider value={value}>
      {children}

      <div className="pointer-events-none fixed inset-x-3 top-3 z-[60] flex flex-col gap-2 sm:left-auto sm:right-4 sm:top-4 sm:w-[22rem]"
        aria-live="polite">
        {toasts.map((toast) => {
          const style = levelStyle[toast.level]
          const clickable = Boolean(toast.issueId || toast.kpiId)
          return (
            <div key={toast.id} role="status"
              className="pointer-events-auto relative flex gap-3 overflow-hidden rounded-card border border-line bg-white p-3.5 pl-5 shadow-pop">
              <span className={`absolute inset-y-0 left-0 w-1 ${style.bar}`} aria-hidden="true" />
              <i className={`ti ti-${style.icon} mt-0.5 text-lg text-ink-2`} aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <p className="text-sm font-semibold text-ink">{toast.title}</p>
                <p className="mt-0.5 break-words text-[13px] text-ink-2">{toast.message}</p>
                {clickable && (
                  <button type="button" onClick={() => open(toast)}
                    className="mt-1.5 text-[13px] font-medium text-accent hover:underline">
                    เปิดดู
                  </button>
                )}
              </div>
              <button type="button" aria-label="ปิดการแจ้งเตือน" onClick={() => dismiss(toast.id)}
                className="h-6 w-6 shrink-0 rounded-full text-ink-3 hover:bg-surface-2 hover:text-ink">
                <i className="ti ti-x text-sm" aria-hidden="true" />
              </button>
            </div>
          )
        })}
      </div>
    </RealtimeContext.Provider>
  )
}
