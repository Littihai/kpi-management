import { createContext, useContext } from 'react'
import type { AppearanceState } from '../lib/appearance'

export interface AppearanceValue extends AppearanceState {
  /** the theme on screen right now ("system" resolved against the operating system) */
  resolvedTheme: 'light' | 'dark'
  /** change one choice; it is applied to the page and remembered on this device at once */
  set: <K extends keyof AppearanceState>(key: K, value: AppearanceState[K]) => void
  /** every choice back to its default */
  reset: () => void
  /** true while anything differs from the defaults */
  changed: boolean
}

export const AppearanceContext = createContext<AppearanceValue | null>(null)

export function useAppearance(): AppearanceValue {
  const ctx = useContext(AppearanceContext)
  if (!ctx) throw new Error('useAppearance must be used inside <AppearanceProvider>')
  return ctx
}
