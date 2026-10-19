import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  DEFAULTS, applyAppearance, isCustomised, resolveTheme, savedAll, store, type AppearanceState,
} from '../lib/appearance'
import { AppearanceContext, type AppearanceValue } from './appearanceContext'

/** Holds the look-and-layout choices, applies them to <html> and remembers them on this device. */
export function AppearanceProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AppearanceState>(savedAll)
  // bumps when the operating system flips light/dark, so "system" is re-resolved
  const [osTick, setOsTick] = useState(0)

  useEffect(() => { applyAppearance(state) }, [state, osTick])

  useEffect(() => {
    if (state.theme !== 'system') return
    let mq: MediaQueryList
    try { mq = window.matchMedia('(prefers-color-scheme: dark)') } catch { return }
    const onChange = () => setOsTick((n) => n + 1)
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [state.theme])

  const set = useCallback(<K extends keyof AppearanceState>(key: K, value: AppearanceState[K]) => {
    store(key, value)
    setState((s) => (s[key] === value ? s : { ...s, [key]: value }))
  }, [])

  const reset = useCallback(() => {
    ;(Object.keys(DEFAULTS) as (keyof AppearanceState)[]).forEach((k) => store(k, DEFAULTS[k] as never))
    setState(DEFAULTS)
  }, [])

  // osTick is a dependency so a change of the operating system's scheme re-resolves "system"
  const resolved = useMemo(() => { void osTick; return resolveTheme(state.theme) }, [osTick, state.theme])

  const value = useMemo<AppearanceValue>(() => ({
    ...state,
    resolvedTheme: resolved,
    set,
    reset,
    changed: isCustomised(state),
  }), [state, resolved, set, reset])

  return <AppearanceContext.Provider value={value}>{children}</AppearanceContext.Provider>
}
