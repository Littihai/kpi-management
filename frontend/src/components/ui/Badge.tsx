import type { ReactNode } from 'react'
import type { Tone } from '../../types/issue'
import { toneClasses } from '../../utils/labels'

interface BadgeProps {
  tone?: Tone
  children: ReactNode
  dot?: boolean
  icon?: string
  className?: string
}

/** Small tinted chip. Colour is never the only signal: there is always text (and often a dot or icon). */
export default function Badge({ tone = 'gray', children, dot, icon, className = '' }: BadgeProps) {
  const t = toneClasses[tone]
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[11.5px] font-semibold whitespace-nowrap ${t.chip} ${className}`}>
      {dot && <span className={`h-1.5 w-1.5 rounded-full ${t.dot}`} aria-hidden="true" />}
      {icon && <i className={`ti ti-${icon} text-sm`} aria-hidden="true" />}
      {children}
    </span>
  )
}

/** Traffic light with a text label so it still reads for people who cannot tell the colours apart. */
export function TrafficBadge({ light, label }: { light: string; label: string }) {
  const tone: Tone = light === 'green' ? 'green' : light === 'yellow' ? 'amber' : light === 'red' ? 'red' : 'gray'
  const icon = light === 'green' ? 'circle-check' : light === 'yellow' ? 'alert-triangle' : light === 'red' ? 'clock-exclamation' : 'circle-dashed'
  return <Badge tone={tone} icon={icon}>{label}</Badge>
}

export function Dot({ tone }: { tone: Tone }) {
  return <span className={`inline-block h-2 w-2 rounded-full ${toneClasses[tone].dot}`} aria-hidden="true" />
}
