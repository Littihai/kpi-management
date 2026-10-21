import { useEffect } from 'react'
import { useAppearance } from './appearanceContext'

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent)
const shortcutLabel = isMac ? '⌘B' : 'Ctrl+B'

/** True while the person is typing somewhere, so Ctrl/Cmd+B (bold in editors) is left alone. */
function typingTarget(t: EventTarget | null): boolean {
  if (!(t instanceof HTMLElement)) return false
  return t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)
}

/**
 * Desktop top-bar button (and Ctrl/Cmd+B) that hides / shows the side menu. It keeps the chosen layout (sidebar or icon
 * rail), so showing it again returns to what Settings > General says. Not shown for the top-bar layout.
 */
export default function SidebarToggle() {
  const { nav, sidebar, set } = useAppearance()
  const available = nav !== 'top'
  const hidden = sidebar === 'hidden'

  useEffect(() => {
    if (!available) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key.toLowerCase() !== 'b' || e.altKey || e.shiftKey || e.defaultPrevented || e.repeat) return
      if (!(isMac ? e.metaKey && !e.ctrlKey : e.ctrlKey && !e.metaKey)) return
      if (typingTarget(e.target)) return
      if (document.querySelector('[role="dialog"][aria-modal="true"]')) return
      if (!window.matchMedia('(min-width: 1024px)').matches) return
      e.preventDefault()
      set('sidebar', hidden ? 'shown' : 'hidden')
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [available, hidden, set])

  if (!available) return null
  const label = hidden ? 'แสดงแถบด้านข้าง' : 'ซ่อนแถบด้านข้าง'
  return (
    <button type="button" id="sidebar-toggle" onClick={() => set('sidebar', hidden ? 'shown' : 'hidden')}
      aria-label={label} aria-expanded={!hidden} aria-controls="app-sidebar" aria-keyshortcuts={isMac ? 'Meta+B' : 'Control+B'}
      title={`${label} (${shortcutLabel})`}
      className="flex h-9 w-9 flex-none items-center justify-center rounded-control text-ink-2 transition-colors hover:bg-surface hover:text-ink">
      <i className={`ti ti-layout-sidebar-left-${hidden ? 'expand' : 'collapse'} text-xl`} aria-hidden="true" />
    </button>
  )
}
