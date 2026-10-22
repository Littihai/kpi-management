import { useEffect, type ReactNode } from 'react'
import { setupService } from '../services/setup.service'
import { useSetupStore } from '../stores/setupStore'

/**
 * Asks the server, beside the normal start, whether it is waiting to be told where its database is. The answer is kept in a store: the
 * sign-in page then offers to connect it. A server that is asleep or slow just means the application shows as usual.
 */
export default function SetupGate({ children }: { children: ReactNode }) {
  const waiting = useSetupStore((s) => s.status !== null)
  const setStatus = useSetupStore((s) => s.setStatus)

  useEffect(() => {
    let alive = true
    setupService.probe().then((probe) => { if (alive && probe.kind === 'setup') setStatus(probe.status) })
    return () => { alive = false }
  }, [setStatus])

  // While it waits, notice when the server carries on by itself (the database came back) and load the application.
  useEffect(() => {
    if (!waiting) return
    const timer = window.setInterval(() => {
      setupService.probe().then((probe) => { if (probe.kind === 'normal') window.location.reload() })
    }, 8000)
    return () => window.clearInterval(timer)
  }, [waiting])

  return <>{children}</>
}
