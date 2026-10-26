import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from 'react'
import { cssColorRgb, cssVariable } from '../../lib/cssColor'
import { BAR_H, BAR_W, loadingBarAnimation } from '../../lib/loadingBarAnimation'

// Fetched after the first page has appeared; the plain bar below moves until it is ready.
const LottiePlayer = lazy(() => import('./LottiePlayer'))

/** Changes whenever the person switches theme, accent colour or motion setting (they are attributes of <html>), so the bar is redrawn in the new colours. */
function useAppearanceKey(): string {
  const read = () => Array.from(document.documentElement.attributes).filter((a) => a.name.startsWith('data-')).map((a) => `${a.name}=${a.value}`).join(';')
  const [key, setKey] = useState(read)
  useEffect(() => {
    const observer = new MutationObserver(() => setKey(read()))
    observer.observe(document.documentElement, { attributes: true })
    return () => observer.disconnect()
  }, [])
  return key
}

/** True when the person (or their system) asked for less movement: the bar then stays still instead of running. */
function useStillBar(appearance: string): boolean {
  const [system, setSystem] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches)
  useEffect(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)')
    const listener = () => setSystem(query.matches)
    query.addEventListener('change', listener)
    return () => query.removeEventListener('change', listener)
  }, [])
  if (appearance.includes('data-motion=reduce')) return true
  if (appearance.includes('data-motion=full')) return false
  return system
}

interface LoadingBarProps {
  /** Width in pixels (the height follows). */
  width?: number
  /** What a screen reader says; pass null when the text next to the bar already says it. */
  label?: string | null
  className?: string
}

/**
 * An indeterminate loading bar (a Lottie animation drawn in the page's accent colour). If the animation cannot start, a plain CSS bar
 * with the same look takes its place, so something always moves.
 */
export default function LoadingBar({ width = 220, label = 'กำลังโหลด', className = '' }: LoadingBarProps) {
  const appearance = useAppearanceKey()
  const still = useStillBar(appearance)
  const [ready, setReady] = useState(false)
  const [failed, setFailed] = useState(false)

  const data = useMemo(() => {
    void appearance // redraw in the new colours when the appearance changes
    const accent = cssColorRgb(cssVariable('--color-accent'), '#2563eb')
    const track = cssColorRgb(cssVariable('--color-line'), '#d9dee7')
    return loadingBarAnimation(accent, track)
  }, [appearance])

  const onReady = useCallback(() => setReady(true), [])
  const onFail = useCallback(() => setFailed(true), [])

  const height = Math.round((width * BAR_H) / BAR_W)
  const lottie = !failed && !still

  return (
    <div role={label ? 'status' : undefined} aria-label={label ?? undefined} aria-hidden={label ? undefined : true}
      className={`relative shrink-0 ${className}`} style={{ width, height }}>
      {(!lottie || !ready) && (
        <div className="absolute inset-x-0 top-1/2 h-1.5 -translate-y-1/2 overflow-hidden rounded-full bg-line">
          <div className={`h-full rounded-full bg-accent ${still ? 'w-2/5' : 'loading-bar-run w-2/5'}`} />
        </div>
      )}
      {lottie && (
        <Suspense fallback={null}>
          <div className="absolute inset-0 transition-opacity" style={{ opacity: ready ? 1 : 0 }}>
            <LottiePlayer data={data} width={width} height={height} onReady={onReady} onFail={onFail} />
          </div>
        </Suspense>
      )}
    </div>
  )
}

/** A bar with a heading and a sentence under it: what to show instead of a spinner while a whole page or panel waits. */
export function LoadingState({ title, hint, className = '' }: { title: string; hint?: string; className?: string }) {
  return (
    <div role="status" className={`flex flex-col items-center gap-2 text-center ${className}`}>
      <LoadingBar label={null} />
      <p className="mt-1 text-base font-medium text-ink">{title}</p>
      {hint && <p className="text-sm text-ink-3">{hint}</p>}
    </div>
  )
}
