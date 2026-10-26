import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from 'react'
import { cssColorRgb, cssVariable } from '../../lib/cssColor'
import { LOADER_H, LOADER_W, loadingAnimation } from '../../lib/loadingAnimation'

// Fetched after the first page has appeared; the still picture below stands in until it is ready.
const LottiePlayer = lazy(() => import('./LottiePlayer'))

/** Changes whenever the person switches theme, accent colour or motion setting (they are attributes of <html>), so the loader is redrawn in the new colours. */
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

/** True when the person (or their system) asked for less movement: the loader then stays still instead of moving. */
function useStill(appearance: string): boolean {
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

/** The same picture without movement: the arc over a glass that is half full. Used before the animation is ready, when it cannot start, and for less motion. */
function Poster() {
  return (
    <svg viewBox={`0 0 ${LOADER_W} ${LOADER_H}`} className="absolute inset-0 h-full w-full" aria-hidden="true">
      <path d="M8 52 A44 44 0 0 1 96 52" fill="none" stroke="var(--color-ink)" strokeWidth="5" />
      <path d="M14 55 C26 49 34 49 46 55 S68 61 90 53 A38 38 0 0 1 14 55 Z" fill="var(--color-accent)" />
    </svg>
  )
}

interface LoadingIndicatorProps {
  /** Width in pixels (the height follows). */
  width?: number
  /** What a screen reader says; pass null when the text next to the loader already says it. */
  label?: string | null
  className?: string
}

/**
 * The loader: an arc over a glass of water whose waves keep rolling (a Lottie animation drawn in the page's colours: the water takes the accent colour
 * the person chose, the arc the colour of text). If the animation cannot start, or less motion was asked for, a still picture of it is shown.
 */
export default function LoadingIndicator({ width = 96, label = 'กำลังโหลด', className = '' }: LoadingIndicatorProps) {
  const appearance = useAppearanceKey()
  const still = useStill(appearance)
  const [ready, setReady] = useState(false)
  const [failed, setFailed] = useState(false)

  const data = useMemo(() => {
    void appearance // redraw in the new colours when the appearance changes
    const water = cssColorRgb(cssVariable('--color-accent'), '#2563eb')
    const ink = cssColorRgb(cssVariable('--color-ink'), '#111827')
    return loadingAnimation(water, ink)
  }, [appearance])

  const onReady = useCallback(() => setReady(true), [])
  const onFail = useCallback(() => setFailed(true), [])

  const height = Math.round((width * LOADER_H) / LOADER_W)
  const lottie = !failed && !still

  return (
    <div role={label ? 'status' : undefined} aria-label={label ?? undefined} aria-hidden={label ? undefined : true}
      className={`relative shrink-0 ${className}`} style={{ width, height }}>
      {(!lottie || !ready) && <Poster />}
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

/** The loader with a heading and a sentence under it: what to show instead of a spinner while a whole page or panel waits. */
export function LoadingState({ title, hint, className = '' }: { title: string; hint?: string; className?: string }) {
  return (
    <div role="status" className={`flex flex-col items-center gap-2 text-center ${className}`}>
      <LoadingIndicator label={null} />
      <p className="mt-1 text-base font-medium text-ink">{title}</p>
      {hint && <p className="text-sm text-ink-3">{hint}</p>}
    </div>
  )
}
