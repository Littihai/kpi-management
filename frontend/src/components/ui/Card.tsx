import type { HTMLAttributes, ReactNode } from 'react'

export function Card({ className = '', children, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={`bg-white border border-line rounded-card shadow-card ${className}`} {...rest}>
      {children}
    </div>
  )
}

/** Small label above a title or a group (Lotline "eyebrow"). */
export function Eyebrow({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <p className={`text-xs font-semibold text-ink-3 ${className}`}>{children}</p>
  )
}

export function CardHeader({ title, action, eyebrow }: { title: ReactNode; action?: ReactNode; eyebrow?: string }) {
  return (
    <div className="flex items-start justify-between gap-3 px-5 pt-4">
      <div className="min-w-0">
        {eyebrow && <Eyebrow className="mb-0.5">{eyebrow}</Eyebrow>}
        <h2 className="text-base font-semibold text-ink leading-snug">{title}</h2>
      </div>
      {action}
    </div>
  )
}

interface StatProps {
  label: string
  value: ReactNode
  hint?: ReactNode
  icon?: string
  /** The one featured card per row: black, like the reference. */
  dark?: boolean
  tone?: 'default' | 'accent' | 'red' | 'amber' | 'green'
  onClick?: () => void
}

const toneText: Record<NonNullable<StatProps['tone']>, string> = {
  default: 'text-ink', accent: 'text-accent', red: 'text-red-ink', amber: 'text-amber-ink', green: 'text-green-ink',
}

export function Stat({ label, value, hint, icon, dark, tone = 'default', onClick }: StatProps) {
  const body = (
    <>
      <div className="flex items-center justify-between">
        <Eyebrow className={dark ? '!text-white/60' : ''}>{label}</Eyebrow>
        {icon && (
          <i className={`ti ti-${icon} text-lg ${dark ? 'text-accent' : 'text-ink-3'}`} aria-hidden="true" />
        )}
      </div>
      <p className={`mt-2.5 text-[28px] leading-none font-semibold tabular ${dark ? 'text-white' : toneText[tone]}`}>
        {value}
      </p>
      {hint && <div className={`mt-2 text-xs ${dark ? 'text-white/60' : 'text-ink-2'}`}>{hint}</div>}
    </>
  )
  const base = `block w-full text-left rounded-card px-5 py-4 border ${
    dark ? 'bg-ink border-ink' : 'bg-white border-line shadow-card'
  }`
  return onClick
    ? <button type="button" onClick={onClick} className={`${base} transition-colors ${dark ? 'hover:bg-black' : 'hover:border-line-strong'}`}>{body}</button>
    : <div className={base}>{body}</div>
}
