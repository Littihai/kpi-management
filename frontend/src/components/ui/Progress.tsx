const colors: Record<string, string> = {
  green: 'bg-green-dot',
  yellow: 'bg-amber-dot',
  red: 'bg-red-dot',
  gray: 'bg-ink-4',
  accent: 'bg-accent',
}

/** Thin progress bar. `value` and `max` use the same unit (the KPI target can be below 100). */
export default function Progress({
  value, max = 100, color = 'accent', label, className = '',
}: { value: number; max?: number; color?: string; label?: string; className?: string }) {
  const percent = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0
  return (
    <div role="progressbar" aria-valuemin={0} aria-valuemax={max} aria-valuenow={value} aria-label={label}
      className={`h-1.5 w-full overflow-hidden rounded-full bg-surface-2 ${className}`}>
      <div className={`h-full rounded-full transition-[width] duration-500 ${colors[color] ?? colors.accent}`}
        style={{ width: `${percent}%` }} />
    </div>
  )
}
