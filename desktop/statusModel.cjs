// What the tray icon, the taskbar overlay and the tooltip show for the status the page reports (no Electron in here, so it can be tested).
//
//   offline    grey      not connected to the server
//   connecting amber     connecting or reconnecting
//   attention  red       work is waiting for the person; the number is shown, "9+" above 9
//   ok         green     connected and nothing is waiting
const STATES = ['ok', 'attention', 'connecting', 'offline']
const MAX_COUNT = 9999
const MAX_TOOLTIP = 100

const WORDING = {
  ok: () => 'ไม่มีงานรอคุณ',
  attention: (count) => `มีงานรอคุณ ${count} รายการ`,
  connecting: () => 'กำลังเชื่อมต่อ…',
  offline: () => 'ไม่ได้เชื่อมต่อกับเซิร์ฟเวอร์',
}

function iconFile(state, count) {
  if (state === 'attention') return count > 9 ? 'attention-9plus.png' : `attention-${count}.png`
  return `${state}.png`
}

/**
 * input: { state, count, tooltip? } as sent by the page. Returns { trayIcon, overlayIcon, description, tooltip }, the icons being file
 * names inside build/status, or null when the state is not one of the four. Anything else is tidied: the count becomes a whole number
 * from 0 to 9999 (and counts only for "attention"), the tooltip is trimmed and shortened (empty means the standard wording).
 */
function describeStatus(input) {
  if (!input || typeof input !== 'object' || !STATES.includes(input.state)) return null
  const number = typeof input.count === 'number' && Number.isFinite(input.count) ? Math.trunc(input.count) : 0
  const count = Math.min(Math.max(number, 0), MAX_COUNT)
  const state = input.state === 'attention' && count === 0 ? 'ok' : input.state // "waiting" with nothing waiting is "ok"
  const description = WORDING[state](count)
  const custom = typeof input.tooltip === 'string' ? input.tooltip.trim().slice(0, MAX_TOOLTIP) : ''
  const icon = iconFile(state, count)
  return { trayIcon: icon, overlayIcon: icon, description, tooltip: custom || description }
}

module.exports = { describeStatus, STATES, MAX_COUNT }
