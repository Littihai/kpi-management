import type { RealtimeNotification } from '../services/realtime'

// Per device, like the look of the page; off until the person turns it on.
const CHOICE_KEY = 'kpi.notify'

/** Whether this browser can show notifications at all. */
export const webNotificationsSupported = () => typeof Notification !== 'undefined'

/** What the person chose on this device. It only takes effect while the browser's own permission is granted too. */
export function webNotificationsChosen(): boolean {
  try { return localStorage.getItem(CHOICE_KEY) === 'on' } catch { return false }
}

export function chooseWebNotifications(on: boolean) {
  try {
    if (on) localStorage.setItem(CHOICE_KEY, 'on')
    else localStorage.removeItem(CHOICE_KEY)
  } catch { /* storage blocked: the choice is not remembered */ }
}

/**
 * Tells the person about a realtime notification outside the page. The Windows app is handed every one (it decides whether
 * a Windows notification is due). A browser shows its own only if the person turned that on, the browser allows it and the
 * page is not in front. `open` gets the issue id when the person clicks it.
 */
export function announce({ title, message, issueId }: RealtimeNotification, open: (issueId: string) => void) {
  if (window.desktop) {
    window.desktop.notify({ title, message, issueId })
    return
  }
  if (!webNotificationsSupported() || Notification.permission !== 'granted' || !webNotificationsChosen()) return
  if (document.visibilityState === 'visible' && document.hasFocus()) return
  try {
    const shown = new Notification(title, { body: message, tag: issueId ?? undefined })
    shown.onclick = () => {
      window.focus()
      shown.close()
      if (issueId) open(issueId)
    }
  } catch { /* some browsers (phones) only allow notifications from a service worker */ }
}
