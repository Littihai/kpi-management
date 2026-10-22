import { useEffect, type ReactNode } from 'react'
import { setupService } from '../services/setup.service'
import { useSetupStore } from '../stores/setupStore'

/**
 * Asks the server, beside the normal start, whether it is waiting to be told where its database is. The answer is kept in a store: the
 * sign-in page then offers to connect it. A server that is asleep or starting gives no answer yet, so it is asked again until it does.
 */
export default function SetupGate({ children }: { children: ReactNode }) {
  const server = useSetupStore((s) => s.server)
  const setStatus = useSetupStore((s) => s.setStatus)
  const setRunning = useSetupStore((s) => s.setRunning)

  // until the server has answered: ask now and every few seconds
  useEffect(() => {
    if (server !== 'unknown') return
    let alive = true
    const ask = () => setupService.probe().then((probe) => {
      if (!alive) return
      if (probe.kind === 'setup') setStatus(probe.status)
      else if (probe.kind === 'normal') setRunning()
    })
    void ask()
    const timer = window.setInterval(ask, 6000)
    return () => { alive = false; window.clearInterval(timer) }
  }, [server, setStatus, setRunning])

  // While it waits, notice when the server carries on by itself (the database came back) and load the application.
  useEffect(() => {
    if (server !== 'setup') return
    const timer = window.setInterval(() => {
      setupService.probe().then((probe) => { if (probe.kind === 'normal') window.location.reload() })
    }, 8000)
    return () => window.clearInterval(timer)
  }, [server])

  return <>{children}</>
}
