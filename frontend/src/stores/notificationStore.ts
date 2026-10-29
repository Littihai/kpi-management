import { create } from 'zustand'
import { notificationService } from '../services/notification.service'

/**
 * How many entries in the notification centre are unread (the server counts up to 100). NotificationTracker keeps it
 * current; anything that reads or clears entries asks for a fresh number through `refreshUnread`.
 */
export const useNotificationStore = create<{ unread: number; loaded: boolean }>(() => ({ unread: 0, loaded: false }))

let latest = 0

export function refreshUnread(): Promise<void> {
  const ticket = ++latest
  return notificationService.unreadCount()
    .then((count) => { if (ticket === latest) useNotificationStore.setState({ unread: count, loaded: true }) }) // an older answer must not win
    .catch(() => { /* keep the last number; the bell is a convenience */ })
}

/** The person is looking at this issue: its entries are read. Quiet when the badge already says there is nothing to clear. */
export function markIssueSeen(issueId: string, force = false): void {
  const { unread, loaded } = useNotificationStore.getState()
  if (!force && loaded && unread === 0) return
  notificationService.markIssueRead(issueId)
    .then((marked) => { if (marked > 0) return refreshUnread() })
    .catch(() => { /* the entries stay unread; the bell still shows them */ })
}
