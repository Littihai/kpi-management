import { useAuthStore } from '../stores/authStore'
import { usePaletteStore } from '../stores/paletteStore'

const isApple = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/i.test(navigator.platform)

/**
 * The visible way into the Ctrl+K search box, so phones and tablets (which have no keyboard shortcut) can open it too.
 * The key hint only shows where a pointer and keyboard are likely (`pointer-fine`).
 */
export default function SearchButton({ variant, onClick }: { variant: 'full' | 'rail' | 'bar'; onClick?: () => void }) {
  const canSearch = useAuthStore((s) => s.user?.permissions?.includes('issue.view')) ?? false
  const setOpen = usePaletteStore((s) => s.setOpen)
  if (!canSearch) return null

  const open = () => { onClick?.(); setOpen(true) }
  const hint = <kbd className="ml-auto hidden rounded border border-line px-1.5 py-0.5 text-[11px] font-medium text-ink-3 pointer-fine:inline-block">{isApple ? '⌘ K' : 'Ctrl K'}</kbd>

  if (variant === 'rail') {
    return (
      <button type="button" onClick={open} aria-label="ค้นหา" aria-haspopup="dialog" title="ค้นหา"
        className="flex h-9 w-full items-center justify-center rounded-control text-ink-3 hover:bg-surface hover:text-ink">
        <i className="ti ti-search text-lg" aria-hidden="true" />
      </button>
    )
  }

  if (variant === 'bar') {
    return (
      <button type="button" onClick={open} aria-haspopup="dialog"
        className="my-1 flex h-8 shrink-0 items-center gap-2 rounded-control border border-line bg-surface px-3 text-[13px] text-ink-3 hover:border-line-strong hover:text-ink">
        <i className="ti ti-search text-base" aria-hidden="true" />
        ค้นหา
        {hint}
      </button>
    )
  }

  return (
    <button type="button" onClick={open} aria-haspopup="dialog"
      className="flex h-9 w-full items-center gap-2.5 rounded-control border border-line bg-surface px-2.5 text-sm text-ink-3 hover:border-line-strong hover:text-ink">
      <i className="ti ti-search text-lg" aria-hidden="true" />
      <span>ค้นหา</span>
      {hint}
    </button>
  )
}
