import { useEffect, useState } from 'react'

/** Live update state from the desktop app; null in a normal browser. */
export function useDesktopUpdate(): DesktopUpdateState | null {
  const [state, setState] = useState<DesktopUpdateState | null>(null)
  useEffect(() => {
    const desktop = window.desktop
    if (!desktop) return
    return desktop.onUpdateState(setState)
  }, [])
  return state
}
