import type { ReactNode } from 'react'
import { PAGE_SIZES } from '../../utils/paging'
import { CompactSelect } from './Field'

/** Page frame: consistent padding and a readable maximum width. */
export function Page({ children, wide }: { children: ReactNode; wide?: boolean }) {
  return (
    // Room at the bottom so the floating "report a problem" button never covers the last row or the pager.
    <div className={`mx-auto w-full px-4 pb-24 pt-5 sm:px-6 lg:pt-7 ${wide ? 'max-w-[1480px]' : 'max-w-[1320px]'}`}>
      {children}
    </div>
  )
}

export function PageHeader({
  title, subtitle, actions, back,
}: {
  title: ReactNode
  subtitle?: ReactNode
  actions?: ReactNode
  back?: ReactNode
}) {
  return (
    <header className="mb-6">
      {back && <div className="mb-3">{back}</div>}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-[22px] font-semibold leading-snug text-ink">{title}</h1>
          {subtitle && <p className="mt-0.5 text-[13px] text-ink-3">{subtitle}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </header>
  )
}

export function EmptyState({
  icon = 'inbox', title, children, action,
}: { icon?: string; title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-10 text-center">
      <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-full bg-surface-2 text-ink-3">
        <i className={`ti ti-${icon} text-xl`} aria-hidden="true" />
      </div>
      <p className="text-[13.5px] font-semibold text-ink-2">{title}</p>
      {children && <p className="mt-1 max-w-sm text-sm text-ink-2">{children}</p>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  )
}

export function Skeleton({ className = '' }: { className?: string }) {
  return <div className={`animate-pulse rounded-control bg-surface-2 ${className}`} aria-hidden="true" />
}

export function PageSkeleton() {
  return (
    <div role="status" aria-label="กำลังโหลด" className="space-y-4">
      <Skeleton className="h-8 w-56" />
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-28" />)}
      </div>
      <Skeleton className="h-64" />
    </div>
  )
}

const alertTone = {
  error: 'bg-red-soft text-red-ink border-red-soft',
  warning: 'bg-amber-soft text-amber-ink border-amber-soft',
  success: 'bg-green-soft text-green-ink border-green-soft',
  info: 'bg-blue-soft text-blue-ink border-blue-soft',
} as const
const alertIcon = { error: 'alert-circle', warning: 'alert-triangle', success: 'circle-check', info: 'info-circle' } as const

export function Alert({
  tone = 'info', children, onClose,
}: { tone?: keyof typeof alertTone; children: ReactNode; onClose?: () => void }) {
  return (
    <div role={tone === 'error' ? 'alert' : 'status'}
      className={`flex items-start gap-2.5 rounded-control border px-3.5 py-3 text-sm ${alertTone[tone]}`}>
      <i className={`ti ti-${alertIcon[tone]} mt-0.5 text-base`} aria-hidden="true" />
      <div className="min-w-0 flex-1">{children}</div>
      {onClose && (
        <button type="button" onClick={onClose} aria-label="ปิด" className="opacity-60 hover:opacity-100">
          <i className="ti ti-x" aria-hidden="true" />
        </button>
      )}
    </div>
  )
}

export function Avatar({ name, size = 32 }: { name: string; size?: number }) {
  const letters = (() => {
    const parts = name.trim().split(/\s+/).filter(Boolean)
    if (parts.length === 0) return '?'
    const first = [...parts[0]][0] ?? ''
    const last = parts.length > 1 ? ([...parts[parts.length - 1]][0] ?? '') : ''
    return (first + last).toUpperCase()
  })()
  return (
    <span aria-hidden="true"
      className="inline-flex shrink-0 items-center justify-center rounded-full bg-accent-200 font-bold text-accent-deep"
      style={{ width: size, height: size, fontSize: size * 0.38 }}>
      {letters}
    </span>
  )
}

/** Pill-shaped switch between a few views ("ของฉัน / ทั้งหมด"). */
export function Segmented<T extends string | number>({
  value, onChange, options, label,
}: {
  value: T
  onChange: (value: T) => void
  options: { value: T; label: string; count?: number }[]
  label: string
}) {
  return (
    // Lotline's wrapping pill tabs: hairline border, solid accent when selected.
    <div role="tablist" aria-label={label} className="flex flex-wrap gap-1.5">
      {options.map((o) => {
        const active = o.value === value
        return (
          <button key={String(o.value)} role="tab" aria-selected={active} type="button" onClick={() => onChange(o.value)}
            className={`inline-flex min-h-8 items-center gap-1.5 whitespace-nowrap rounded-full border px-3.5 text-[13px] font-semibold transition-colors
              ${active
                ? 'border-accent bg-accent text-white'
                : 'border-line bg-white text-ink-2 hover:border-line-strong hover:text-ink'}`}>
            {o.label}
            {o.count !== undefined && (
              <span className={`tabular min-w-[18px] rounded-full px-1.5 text-[10.5px] font-bold leading-[1.5]
                ${active ? 'bg-white/25 text-white' : 'bg-surface-2 text-ink-2'}`}>{o.count}</span>
            )}
          </button>
        )
      })}
    </div>
  )
}

/**
 * Range, rows-per-page choice and previous / next. Hidden while everything fits on the smallest page
 * (nothing to choose); otherwise it stays so the size can still be changed on the last page.
 */
export function Pagination({
  page, pageSize, total, onChange, onPageSizeChange,
}: {
  page: number
  pageSize: number
  total: number
  onChange: (page: number) => void
  onPageSizeChange: (size: number) => void
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  if (total <= PAGE_SIZES[0]) return null
  const from = Math.min((page - 1) * pageSize + 1, total)
  const to = Math.min(total, page * pageSize)
  return (
    <nav aria-label="เปลี่ยนหน้า"
      className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-line px-5 py-3 text-[13px] text-ink-2">
      <span className="tabular">{from}–{to} จาก {total}</span>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <label className="flex items-center gap-2 text-ink-3">
          แสดงต่อหน้า
          <CompactSelect value={pageSize} onChange={(e) => onPageSizeChange(Number(e.target.value))} className="!w-auto text-ink">
            {PAGE_SIZES.map((size) => <option key={size} value={size}>{size}</option>)}
          </CompactSelect>
        </label>
        <div className="flex items-center gap-1">
          <button type="button" disabled={page <= 1} onClick={() => onChange(page - 1)} aria-label="หน้าก่อนหน้า"
            className="flex h-8 w-8 items-center justify-center rounded-control hover:bg-surface-2 disabled:opacity-40">
            <i className="ti ti-chevron-left" aria-hidden="true" />
          </button>
          <span className="px-2 tabular">{page} / {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => onChange(page + 1)} aria-label="หน้าถัดไป"
            className="flex h-8 w-8 items-center justify-center rounded-control hover:bg-surface-2 disabled:opacity-40">
            <i className="ti ti-chevron-right" aria-hidden="true" />
          </button>
        </div>
      </div>
    </nav>
  )
}
