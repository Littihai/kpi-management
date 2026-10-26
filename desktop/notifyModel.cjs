// Rules for the Windows toasts (no Electron in here, so it can be tested).
const MAX_TITLE = 100
const MAX_MESSAGE = 250
const ISSUE_ID = /^[A-Za-z0-9-]{1,64}$/ // the Helpdesk ids are GUIDs; anything else is not an issue the page can open

const text = (value, max) => (typeof value === 'string' ? value.trim().slice(0, max) : '')

/** input: { title, message, issueId } from the page. Returns the same tidied, or null when there is nothing to say. */
function normalizeNotification(input) {
  if (!input || typeof input !== 'object') return null
  const message = text(input.message, MAX_MESSAGE)
  const title = text(input.title, MAX_TITLE) || (message ? 'Helpdesk' : '')
  if (!title) return null
  return { title, message, issueId: typeof input.issueId === 'string' && ISSUE_ID.test(input.issueId.trim()) ? input.issueId.trim() : null }
}

/**
 * A toast is only for someone who is not looking at the app: the window is hidden to the tray, minimised or in the background.
 * window: { exists, visible, minimized, focused }
 */
function shouldShowToast({ enabled, window }) {
  if (!enabled) return false
  return !window.exists || !window.visible || window.minimized || !window.focused
}

module.exports = { normalizeNotification, shouldShowToast }
