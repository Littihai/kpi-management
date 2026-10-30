import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { attentionStatus } from '../lib/attention'
import { restoreTabIcon, setTabIcon } from '../lib/tabIcon'
import { issueService } from '../services/issue.service'
import { useAttentionCountsStore } from '../stores/attentionCountsStore'
import { useAttentionStore } from '../stores/attentionStore'
import { useAuthStore } from '../stores/authStore'
import { useIssueEvents, useNotificationEvents, useRealtimeStatus } from './realtimeContext'

/** An event can be missed (sleeping laptop, dropped connection); that must not leave a stale number for long. */
const POLL_MS = 2 * 60 * 1000
/** A burst of events (one action changes an issue and notifies several people) is one request. */
const QUIET_MS = 800

/**
 * Draws nothing: keeps the person's status current. It asks the server how many issues wait for them (on load, when the
 * connection comes back, after every change or notification, when the window gets focus, and every couple of minutes),
 * combines that with the connection state, and shows the result in the status dot, the tab (icon) and, in the Windows
 * app, the tray icon and taskbar overlay. Must sit inside RealtimeProvider.
 */
export default function AttentionTracker() {
  const connection = useRealtimeStatus()
  const [waiting, setWaiting] = useState(0)
  const latest = useRef(0)
  const quiet = useRef(0)
  const isIt = useAuthStore((s) => s.user?.permissions?.includes('issue.manage')) ?? false

  const refresh = useCallback(() => {
    const ticket = ++latest.current
    issueService.attention()
      .then((attention) => { // an older answer must not win
        if (ticket !== latest.current) return
        setWaiting(attention.total)
        useAttentionCountsStore.setState({ pending: attention.total, assigned: attention.assignedToMe })
      })
      .catch(() => { /* keep the last number; the status already says when the server cannot be reached */ })
    // The "near SLA" view is for the people who work the queue; nobody else has the entry, so nobody else asks.
    if (isIt) {
      issueService.summary()
        .then((summary) => { if (ticket === latest.current) useAttentionCountsStore.setState({ nearSla: summary.nearSla }) })
        .catch(() => { /* as above */ })
    }
  }, [isIt])

  const refreshSoon = useCallback(() => {
    window.clearTimeout(quiet.current)
    quiet.current = window.setTimeout(refresh, QUIET_MS)
  }, [refresh])
  useEffect(() => () => window.clearTimeout(quiet.current), [])

  useEffect(() => { if (connection === 'connected') refresh() }, [connection, refresh])
  useIssueEvents(refreshSoon)
  useNotificationEvents(refreshSoon)

  useEffect(() => {
    const poll = window.setInterval(refresh, POLL_MS)
    window.addEventListener('focus', refresh)
    return () => {
      window.clearInterval(poll)
      window.removeEventListener('focus', refresh)
    }
  }, [refresh])

  const status = useMemo(() => attentionStatus(connection, waiting), [connection, waiting])
  useEffect(() => {
    useAttentionStore.setState(status)
    setTabIcon(status)
    window.desktop?.setStatus(status)
  }, [status])

  // Leaving the shell: the page goes back to its plain look and the tray to grey. Signing out unmounts this through React;
  // a forced sign-in (expired session) or a change of server loads a new page, where only "pagehide" is left to say it.
  useEffect(() => {
    const grey = () => window.desktop?.setStatus(attentionStatus('offline', 0))
    window.addEventListener('pagehide', grey)
    return () => {
      window.removeEventListener('pagehide', grey)
      useAttentionStore.setState(useAttentionStore.getInitialState())
      useAttentionCountsStore.setState(useAttentionCountsStore.getInitialState())
      restoreTabIcon()
      grey()
    }
  }, [])

  return null
}
