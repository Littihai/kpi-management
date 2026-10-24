import { useId } from 'react'
import type { ReactNode } from 'react'
import Switch from './Switch'

/**
 * One setting as a row: the name and what it does on the left, the control on the right. `id` makes the row a target
 * for the settings search (it scrolls here and flashes).
 */
export function SettingLine({ id, title, desc, badges, children, className = '' }: {
  id?: string
  title: string
  desc?: ReactNode
  badges?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <div id={id} className={`flex flex-wrap items-start gap-x-6 gap-y-3 ${className}`}>
      <div className="min-w-0 flex-1 basis-64">
        <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-ink">{title}{badges}</p>
        {desc && <div className="mt-0.5 text-xs text-ink-3">{desc}</div>}
      </div>
      <div className="flex shrink-0 items-center gap-2">{children}</div>
    </div>
  )
}

/** A setting that is either on or off. */
export function SwitchLine({ id, title, desc, badges, checked, onChange, disabled, className }: {
  id?: string
  title: string
  desc?: ReactNode
  badges?: ReactNode
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  className?: string
}) {
  const uid = useId()
  const titleId = `${uid}-t`
  return (
    <div id={id} className={`flex items-start justify-between gap-6 ${className ?? ''}`}>
      <div className="min-w-0 flex-1">
        <p id={titleId} className="flex flex-wrap items-center gap-2 text-sm font-semibold text-ink">{title}{badges}</p>
        {desc && <div id={`${uid}-d`} className="mt-0.5 text-xs text-ink-3">{desc}</div>}
      </div>
      <Switch checked={checked} onChange={onChange} disabled={disabled} labelledBy={titleId} describedBy={desc ? `${uid}-d` : undefined} />
    </div>
  )
}
