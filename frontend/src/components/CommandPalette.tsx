import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { searchService } from '../services/notification.service'
import { useAuthStore } from '../stores/authStore'
import { usePaletteStore } from '../stores/paletteStore'
import type { QuickSearchResult } from '../types/notification'
import { StatusBadge } from './issues/IssueBadges'
import { useOpenSettings } from './settings/settingsNav'
import { Avatar } from './ui/Layout'

const MIN_LENGTH = 2
const TYPING_PAUSE_MS = 200

type Option =
  | { key: string; kind: 'issue'; hit: QuickSearchResult['issues'][number] }
  | { key: string; kind: 'user'; hit: QuickSearchResult['users'][number] }

/**
 * Ctrl+K (or Cmd+K) opens a search box over any page: issues by number, title or reporter, and people for whoever may open
 * the user list. The server decides what each person may find; this only asks and shows. Mounted once in the app shell.
 */
export default function CommandPalette() {
  const open = usePaletteStore((s) => s.open)
  const setOpen = usePaletteStore((s) => s.setOpen)
  const canSearch = useAuthStore((s) => s.user?.permissions?.includes('issue.view')) ?? false

  // preventDefault also keeps Firefox from moving focus to its own search bar.
  useEffect(() => {
    if (!canSearch) return
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey || e.key.toLowerCase() !== 'k') return
      e.preventDefault()
      setOpen(!usePaletteStore.getState().open)
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [canSearch, setOpen])

  return open && canSearch ? <Palette onClose={() => setOpen(false)} /> : null
}

function Palette({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate()
  const openSettings = useOpenSettings()
  const input = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')
  const [found, setFound] = useState<{ q: string; result: QuickSearchResult } | null>(null)
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')
  const [active, setActive] = useState(0)

  const term = query.trim()
  const searchable = term.length >= MIN_LENGTH

  // Take the keyboard while open, and hand it back (and the page's scrolling) on close.
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    document.body.style.overflow = 'hidden'
    input.current?.focus()
    return () => {
      document.body.style.overflow = ''
      previous?.focus()
    }
  }, [])

  // Search after a short pause in typing; a newer question cancels the older one so a slow answer cannot replace a fresh one.
  useEffect(() => {
    if (!searchable) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setState('loading')
      searchService.quick(term, controller.signal)
        .then((result) => { setFound({ q: term, result }); setState('idle') })
        .catch(() => { if (!controller.signal.aborted) setState('error') })
    }, TYPING_PAUSE_MS)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [term, searchable])

  const result = searchable ? found?.result : undefined
  const options = useMemo<Option[]>(() => [
    ...(result?.issues ?? []).map((hit) => ({ key: `issue-${hit.id}`, kind: 'issue' as const, hit })),
    ...(result?.users ?? []).map((hit) => ({ key: `user-${hit.id}`, kind: 'user' as const, hit })),
  ], [result])
  const current = Math.min(active, Math.max(0, options.length - 1))
  const activeId = options.length > 0 ? `palette-${options[current].key}` : undefined

  useEffect(() => {
    if (activeId) document.getElementById(activeId)?.scrollIntoView({ block: 'nearest' })
  }, [activeId])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const choose = useCallback((option: Option) => {
    onClose()
    if (option.kind === 'issue') navigate(`/issues/${option.hit.id}`)
    else openSettings('users')
  }, [navigate, onClose, openSettings])

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.nativeEvent.isComposing) return // Enter that confirms a Thai or CJK composition is not a choice
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      if (options.length === 0) return
      setActive((current + (e.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length)
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (options.length > 0) choose(options[current])
    } else if (e.key === 'Tab') {
      e.preventDefault() // the box has one place for the keyboard; leaving it would drop focus onto the page behind
    }
  }

  const issueOptions = options.filter((o): o is Extract<Option, { kind: 'issue' }> => o.kind === 'issue')
  const userOptions = options.filter((o): o is Extract<Option, { kind: 'user' }> => o.kind === 'user')
  const position = (option: Option) => options.indexOf(option)

  const optionClass = (option: Option) =>
    `flex w-full cursor-pointer items-center gap-3 px-4 py-2.5 text-left ${position(option) === current ? 'bg-accent-soft' : 'hover:bg-surface'}`

  return (
    <div className="fixed inset-0 z-[70] flex items-start justify-center bg-scrim p-3 pt-[8vh] sm:pt-[12vh]" onMouseDown={onClose}>
      <div role="dialog" aria-modal="true" aria-label="ค้นหา" onMouseDown={(e) => e.stopPropagation()}
        className="w-full max-w-xl overflow-hidden rounded-card border border-line bg-card shadow-pop">
        <div className="flex items-center gap-3 border-b border-line px-4">
          <i className="ti ti-search text-lg text-ink-3" aria-hidden="true" />
          <input ref={input} type="text" role="combobox" aria-expanded={options.length > 0} aria-controls="palette-list"
            aria-activedescendant={activeId} aria-autocomplete="list" aria-label="ค้นหาเรื่องที่แจ้งหรือผู้ใช้งาน"
            autoComplete="off" autoCorrect="off" spellCheck={false} enterKeyHint="go"
            placeholder="ค้นหาเลขที่ หัวข้อ หรือชื่อผู้แจ้ง…"
            value={query} onChange={(e) => { setQuery(e.target.value); setActive(0) }} onKeyDown={onKeyDown}
            className="h-12 min-w-0 flex-1 bg-transparent text-base text-ink outline-none placeholder:text-ink-3" />
          <button type="button" onClick={onClose} aria-label="ปิดการค้นหา"
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-control text-ink-2 hover:bg-surface-2 pointer-fine:hidden">
            <i className="ti ti-x" aria-hidden="true" />
          </button>
          <kbd className="hidden shrink-0 rounded border border-line px-1.5 py-0.5 text-[11px] text-ink-3 pointer-fine:inline-block">Esc</kbd>
        </div>

        <div id="palette-list" role="listbox" aria-label="ผลการค้นหา" className="max-h-[60vh] overflow-y-auto py-1">
          {issueOptions.length > 0 && (
            <div role="group" aria-labelledby="palette-issues">
              <p id="palette-issues" className="px-4 pb-1 pt-2 text-xs font-semibold text-ink-3">เรื่องที่แจ้ง</p>
              {issueOptions.map((option) => (
                <div key={option.key} id={`palette-${option.key}`} role="option" tabIndex={-1} aria-selected={position(option) === current}
                  onClick={() => choose(option)} onMouseMove={() => setActive(position(option))} className={optionClass(option)}>
                  <i className="ti ti-lifebuoy text-lg text-ink-3" aria-hidden="true" />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-ink">{option.hit.title}</span>
                    <span className="block truncate text-xs text-ink-3"><span className="tabular">{option.hit.number}</span> · {option.hit.reporterName}</span>
                  </span>
                  <StatusBadge status={option.hit.status} />
                </div>
              ))}
            </div>
          )}
          {userOptions.length > 0 && (
            <div role="group" aria-labelledby="palette-users">
              <p id="palette-users" className="px-4 pb-1 pt-2 text-xs font-semibold text-ink-3">ผู้ใช้งาน</p>
              {userOptions.map((option) => (
                <div key={option.key} id={`palette-${option.key}`} role="option" tabIndex={-1} aria-selected={position(option) === current}
                  onClick={() => choose(option)} onMouseMove={() => setActive(position(option))} className={optionClass(option)}>
                  <Avatar name={option.hit.name} size={28} />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-ink">{option.hit.name}</span>
                    <span className="block truncate text-xs text-ink-3">{option.hit.email}{option.hit.department ? ` · ${option.hit.department}` : ''}</span>
                  </span>
                </div>
              ))}
            </div>
          )}

          {!searchable && (
            <p className="px-4 py-8 text-center text-sm text-ink-3">พิมพ์อย่างน้อย {MIN_LENGTH} ตัวอักษรเพื่อค้นหา</p>
          )}
          {searchable && options.length === 0 && state === 'idle' && found?.q === term && (
            <p className="px-4 py-8 text-center text-sm text-ink-3">ไม่พบผลลัพธ์สำหรับ “{term}”</p>
          )}
          {searchable && state === 'error' && (
            <p className="px-4 py-8 text-center text-sm text-red-ink" role="alert">ค้นหาไม่สำเร็จ ลองใหม่อีกครั้ง</p>
          )}
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-line px-4 py-2 text-xs text-ink-3">
          <span aria-live="polite">{state === 'loading' ? 'กำลังค้นหา…' : searchable && found?.q === term ? `พบ ${options.length} รายการ` : ''}</span>
          <span className="hidden pointer-fine:inline">↑↓ เลือก · Enter เปิด · Esc ปิด</span>
        </div>
      </div>
    </div>
  )
}
