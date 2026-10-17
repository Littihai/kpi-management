const TZ = 'Asia/Bangkok'

/** The API sends UTC timestamps; a value with no zone is UTC too (older rows), so read it as such. */
export function toDate(value: string | Date | null | undefined): Date | null {
  if (!value) return null
  if (value instanceof Date) return value
  const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(value)
  const date = new Date(hasZone ? value : `${value}Z`)
  return Number.isNaN(date.getTime()) ? null : date
}

const dateFmt = new Intl.DateTimeFormat('th-TH', {
  day: '2-digit', month: '2-digit', year: 'numeric', timeZone: TZ,
})
const dateTimeFmt = new Intl.DateTimeFormat('th-TH', {
  day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false, timeZone: TZ,
})
const shortDateFmt = new Intl.DateTimeFormat('th-TH', { day: 'numeric', month: 'short', timeZone: TZ })

/** 17/10/2569 (Buddhist year, Thai time zone). */
export function formatDate(value: string | Date | null | undefined): string {
  const d = toDate(value)
  return d ? dateFmt.format(d) : '–'
}

/** 17/10/2569 14:32 */
export function formatDateTime(value: string | Date | null | undefined): string {
  const d = toDate(value)
  return d ? dateTimeFmt.format(d).replace(',', '') : '–'
}

export function formatShortDate(value: string | Date | null | undefined): string {
  const d = toDate(value)
  return d ? shortDateFmt.format(d) : '–'
}

/** "5 นาทีที่แล้ว", "เมื่อ 2 ชม.ที่แล้ว", or the date once it is more than a week old. */
export function formatRelative(value: string | Date | null | undefined, now = new Date()): string {
  const d = toDate(value)
  if (!d) return '–'
  const seconds = Math.round((now.getTime() - d.getTime()) / 1000)
  if (seconds < 45) return 'เมื่อสักครู่'
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} นาทีที่แล้ว`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} ชม.ที่แล้ว`
  const days = Math.round(hours / 24)
  if (days <= 7) return `${days} วันที่แล้ว`
  return formatDate(d)
}

/** Time left until a deadline ("เหลือ 1 ชม. 20 นาที") or how late it is ("เกินกำหนด 3 ชม."). */
export function formatCountdown(due: string | Date | null | undefined, now = new Date()): { text: string; late: boolean } {
  const d = toDate(due)
  if (!d) return { text: '–', late: false }
  const diff = d.getTime() - now.getTime()
  const late = diff < 0
  const mins = Math.floor(Math.abs(diff) / 60000)
  const days = Math.floor(mins / 1440)
  const hours = Math.floor((mins % 1440) / 60)
  const rest = mins % 60

  let span: string
  if (days >= 2) span = `${days} วัน`
  else if (days === 1) span = `1 วัน ${hours} ชม.`
  else if (hours >= 1) span = rest ? `${hours} ชม. ${rest} นาที` : `${hours} ชม.`
  else span = `${Math.max(1, rest)} นาที`

  return { text: late ? `เกินกำหนด ${span}` : `เหลือ ${span}`, late }
}

export function formatNumber(value: number, digits = 0): string {
  return new Intl.NumberFormat('th-TH', { maximumFractionDigits: digits }).format(value)
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

export function initials(name: string | null | undefined): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  const first = [...parts[0]][0] ?? ''
  const second = parts.length > 1 ? ([...parts[parts.length - 1]][0] ?? '') : ''
  return (first + second).toUpperCase()
}

/** Start of the given day in Thai time, as an ISO string the API understands (for date filters). */
export function dayStartIso(yyyyMmDd: string): string {
  return new Date(`${yyyyMmDd}T00:00:00+07:00`).toISOString()
}

/** The day after the given date in Thai time, so a filter "to 17/10" includes all of the 17th. */
export function dayEndIso(yyyyMmDd: string): string {
  const d = new Date(`${yyyyMmDd}T00:00:00+07:00`)
  d.setDate(d.getDate() + 1)
  return d.toISOString()
}
