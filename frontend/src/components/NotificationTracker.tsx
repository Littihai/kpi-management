import { useCallback, useEffect, useRef } from 'react'
import { useAuthStore } from '../stores/authStore'
import { refreshUnread, useNotificationStore } from '../stores/notificationStore'
import { useNotificationEvents, useRealtimeStatus } from './realtimeContext'

/** An event can be missed (sleeping laptop, dropped connection); that must not leave a stale number for long. */
const POLL_MS = 2 * 60 * 1000
/** A burst of notifications is one request. */
const QUIET_MS = 800

/**
 * Draws nothing: keeps the bell's unread number current. It asks the server on load, when the connection comes back, after
 * every notification that arrives, when the window gets focus, and every couple of minutes. Must sit inside RealtimeProvider.
 */
export default function NotificationTracker() {
  const connection = useRealtimeStatus()
  const quiet = useRef(0)
  // The centre belongs to the issue screens; someone without them would only collect refusals in the request log.
  const canView = useAuthStore((s) => s.user?.permissions?.includes('issue.view')) ?? false

  const refreshSoon = useCallback(() => {
    window.clearTimeout(quiet.current)
    quiet.current = window.setTimeout(refreshUnread, QUIET_MS)
  }, [])
  useEffect(() => () => window.clearTimeout(quiet.current), [])

  useEffect(() => { if (canView && connection === 'connected') refreshUnread() }, [canView, connection])
  useNotificationEvents(refreshSoon)

  useEffect(() => {
    if (!canView) return
    const poll = window.setInterval(refreshUnread, POLL_MS)
    window.addEventListener('focus', refreshUnread)
    return () => {
      window.clearInterval(poll)
      window.removeEventListener('focus', refreshUnread)
      useNotificationStore.setState(useNotificationStore.getInitialState())
    }
  }, [canView])

  return null
}
