import { useEffect, useState } from 'react'

/**
 * A made-up progress for things that cannot be measured (a page being fetched, a server starting): it climbs quickly at first and slows down,
 * never reaching 100 by itself. The thing that waits disappears when the work is done, which is the real 100.
 */
function useEstimatedPercent(active: boolean): number {
  const [percent, setPercent] = useState(0)
  useEffect(() => {
    if (!active) return
    const started = performance.now()
    const timer = window.setInterval(() => {
      setPercent(Math.round(95 * (1 - Math.exp(-(performance.now() - started) / 2500))))
    }, 120)
    return () => window.clearInterval(timer)
  }, [active])
  return percent
}

interface LoadingProgressProps {
  /** 0..100 when the real progress is known (a download); leave out to show an estimate. */
  percent?: number
  /** The line above the bar; null for none. */
  title?: string | null
  /** A smaller line under the percentage. */
  hint?: string
  /** Width of the bar in pixels. */
  width?: number
  className?: string
}

/** A loading bar with the words above it and the percentage under it. It takes its colours from the page: the accent colour for the fill, the text colours for the words. */
export default function LoadingProgress({ percent, title = 'กำลังโหลด…', hint, width = 220, className = '' }: LoadingProgressProps) {
  const estimated = useEstimatedPercent(percent === undefined)
  const shown = Math.max(0, Math.min(100, Math.round(percent ?? estimated)))

  return (
    <div role="progressbar" aria-label={title ?? 'กำลังโหลด'} aria-valuemin={0} aria-valuemax={100} aria-valuenow={shown}
      className={`flex flex-col items-center gap-2 text-center ${className}`}>
      {title && <p className="text-base font-semibold text-ink">{title}</p>}
      <div className="h-2.5 max-w-full overflow-hidden rounded-full bg-line" style={{ width }}>
        <div className="h-full rounded-full bg-accent transition-[width] duration-300 ease-out" style={{ width: `${shown}%` }} />
      </div>
      <p className="text-sm tabular-nums text-ink-2">{shown}%</p>
      {hint && <p className="text-sm text-ink-3">{hint}</p>}
    </div>
  )
}
