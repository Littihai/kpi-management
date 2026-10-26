import type { RealtimeStatus } from '../components/realtimeContext'

/** The Teams-like status: green check, red number, amber while connecting, grey when not connected. The Windows app shows the same. */
export type AttentionState = 'ok' | 'attention' | 'connecting' | 'offline'

export interface AttentionStatus {
  state: AttentionState
  /** how many issues are waiting for the person; 0 unless the state is 'attention' */
  count: number
  /** the words for hover hints, screen readers and the tray */
  tooltip: string
}

/** Not connected beats everything: a number nobody can trust is not shown. */
export function attentionStatus(connection: RealtimeStatus, waiting: number): AttentionStatus {
  if (connection === 'offline') return { state: 'offline', count: 0, tooltip: 'ไม่ได้เชื่อมต่อกับเซิร์ฟเวอร์' }
  if (connection !== 'connected') return { state: 'connecting', count: 0, tooltip: 'กำลังเชื่อมต่อ…' }
  if (waiting > 0) return { state: 'attention', count: waiting, tooltip: `มีงานรอคุณ ${waiting} รายการ` }
  return { state: 'ok', count: 0, tooltip: 'ไม่มีงานรอคุณ' }
}

/** The number as it fits in a small badge. */
export const badgeText = (count: number) => (count > 9 ? '9+' : String(count))
