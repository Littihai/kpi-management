import { useEffect, useState, type ReactNode } from 'react'
import SetupPage from '../pages/SetupPage'
import { setupService, type SetupStatus } from '../services/setup.service'

/**
 * A server that does not know where its database is answers with a setup page instead of the application.
 * The check runs beside the normal start: a server that is asleep or slow just means the application shows as usual.
 */
export default function SetupGate({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SetupStatus | null>(null)

  useEffect(() => {
    let alive = true
    setupService.probe().then((probe) => { if (alive && probe.kind === 'setup') setStatus(probe.status) })
    return () => { alive = false }
  }, [])

  // While the page is up, notice when the server carries on by itself (the database came back) and go to the application.
  const waiting = status !== null
  useEffect(() => {
    if (!waiting) return
    const timer = window.setInterval(() => {
      setupService.probe().then((probe) => { if (probe.kind === 'normal') window.location.reload() })
    }, 8000)
    return () => window.clearInterval(timer)
  }, [waiting])

  return status ? <SetupPage status={status} /> : <>{children}</>
}
