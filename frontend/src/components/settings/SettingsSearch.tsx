import { useId, useMemo, useState, type RefObject } from 'react'
import type { SettingsSectionId } from './settingsNav'
import { searchSettings, type SettingsHit } from './settingsFind'

/**
 * The search box at the top of the settings menu. While there is text it also draws the list of matches (the menu
 * hides behind it); ↑ ↓ move, Enter opens, Esc clears. `onPick` opens the section and scrolls to the row.
 */
export default function SettingsSearch({ visible, onPick, inputRef, query, setQuery }: {
  visible: readonly SettingsSectionId[]
  onPick: (hit: SettingsHit) => void
  inputRef: RefObject<HTMLInputElement | null>
  query: string
  setQuery: (q: string) => void
}) {
  const listId = useId()
  const [active, setActive] = useState(0)
  const hits = useMemo(() => searchSettings(query, visible), [query, visible])
  const searching = query.trim() !== ''
  const current = Math.min(active, Math.max(hits.length - 1, 0))

  const pick = (h: SettingsHit | undefined) => {
    if (!h) return
    onPick(h)
    setQuery('')
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive((current + 1) % Math.max(hits.length, 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((current - 1 + hits.length) % Math.max(hits.length, 1)) }
    else if (e.key === 'Enter') { e.preventDefault(); pick(hits[current]) }
    else if (e.key === 'Escape') { e.preventDefault(); if (searching) setQuery(''); else e.currentTarget.blur() }
  }

  return (
    <div>
      <div className="relative">
        <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-base text-ink-3" aria-hidden="true" />
        <input ref={inputRef} type="search" value={query} placeholder="ค้นหาการตั้งค่า" aria-label="ค้นหาการตั้งค่า" autoComplete="off"
          role="combobox" aria-expanded={searching} aria-controls={listId} aria-autocomplete="list"
          aria-activedescendant={searching && hits.length > 0 ? `${listId}-${current}` : undefined}
          onChange={(e) => { setQuery(e.target.value); setActive(0) }} onKeyDown={onKeyDown}
          className="h-9 w-full rounded-control border border-line-strong bg-card pl-9 pr-3 text-[13px] text-ink placeholder:text-ink-3 hover:border-ink-4 [&::-webkit-search-cancel-button]:hidden" />
      </div>

      {searching && (
        <ul id={listId} role="listbox" aria-label="ผลการค้นหา" className="mt-2 max-h-56 space-y-0.5 overflow-y-auto lg:max-h-none">
          {hits.length === 0 ? (
            <li role="presentation" className="px-3 py-2 text-[13px] text-ink-3">ไม่พบการตั้งค่าที่ตรงกับ "{query.trim()}"</li>
          ) : hits.map((h, i) => (
            <li key={`${h.section}-${h.anchor ?? h.label}`} id={`${listId}-${i}`} role="option" aria-selected={i === current}
              onMouseEnter={() => setActive(i)} onMouseDown={(e) => e.preventDefault()} onClick={() => pick(h)}
              className={`cursor-pointer rounded-control px-3 py-2 text-[13px] ${i === current ? 'bg-card text-ink shadow-card' : 'text-ink-2'}`}>
              <span className="block font-semibold">{h.label}</span>
              {h.label !== h.sectionLabel && <span className="block text-xs text-ink-3">{h.sectionLabel}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
