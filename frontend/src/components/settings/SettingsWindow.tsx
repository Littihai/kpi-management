import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { EmbeddedContext } from '../ui/embedded'
import { EmptyState, Skeleton } from '../ui/Layout'
import { useAuthStore } from '../../stores/authStore'
import AboutSection from './AboutSection'
import AccountSection from './AccountSection'
import AppearanceControls from './AppearanceControls'
import SettingsSearch from './SettingsSearch'
import type { SettingsHit } from './settingsFind'
import {
  SETTINGS_GROUPS, SETTINGS_SECTIONS, visibleSettingsSections,
  type SettingsLocationState, type SettingsSectionDef, type SettingsSectionId,
} from './settingsNav'

// The admin screens are the same pages as before, drawn inside the window; they load when first opened.
const UsersPage = lazy(() => import('../../pages/admin/UsersPage'))
const SystemSection = lazy(() => import('./SystemSection'))
const SecuritySection = lazy(() => import('./SecuritySection'))
const DatabaseSection = lazy(() => import('./DatabaseSection'))
const EmailSection = lazy(() => import('./EmailSection'))
const RolesSection = lazy(() => import('./RolesSection'))
const DepartmentsPage = lazy(() => import('../../pages/admin/DepartmentsPage'))
const AuditPage = lazy(() => import('../../pages/admin/AuditPage'))

function SectionBody({ id }: { id: SettingsSectionId }) {
  switch (id) {
    case 'general': return <AppearanceControls />
    case 'account': return <AccountSection />
    case 'about': return <AboutSection />
    case 'users': return <UsersPage />
    case 'system': return <SystemSection />
    case 'security': return <SecuritySection />
    case 'email': return <EmailSection />
    case 'database': return <DatabaseSection />
    case 'roles': return <RolesSection />
    case 'departments': return <DepartmentsPage />
    case 'audit': return <AuditPage />
  }
}

/**
 * The settings window (the Lotline pattern): a large window over the page with a grouped menu on the left and one
 * section on the right. It is a real route, /settings/<section>, so it can be linked and survives a refresh. On a
 * phone or a narrow window it fills the screen and the menu becomes a strip of tabs along the top.
 */
export default function SettingsWindow() {
  const location = useLocation()
  const navigate = useNavigate()
  const permissions = useAuthStore((s) => s.user?.permissions)
  const dialog = useRef<HTMLDivElement>(null)
  const body = useRef<HTMLDivElement>(null)
  const searchBox = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')

  const state = location.state as SettingsLocationState | null
  const visible = useMemo(() => visibleSettingsSections(permissions), [permissions])
  const visibleIds = useMemo(() => visible.map((s) => s.id), [visible])
  const def = SETTINGS_SECTIONS.find((s) => s.id === location.pathname.split('/')[2])
  const allowed = !!def && visible.some((s) => s.id === def.id)

  const close = () => {
    // opened over a page: step back to it; opened cold from a link: go home
    if (state?.backgroundLocation) navigate(-1)
    else navigate('/dashboard', { replace: true })
  }
  const closeRef = useRef(close)
  useEffect(() => { closeRef.current = close })

  // The page behind must not scroll; focus moves into the window so the keyboard works at once.
  useEffect(() => {
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    dialog.current?.focus()
    return () => { document.body.style.overflow = previous }
  }, [])

  // Esc closes the window, unless a dialog opened on top of it (it closes itself) or the person is typing.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      const el = e.target as HTMLElement | null
      if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable)) return
      if (document.querySelector('[role="dialog"][aria-modal="true"]:not([data-settings-window])')) return
      closeRef.current()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])

  // "/" jumps to the search box (as in most apps), unless the person is typing somewhere.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return
      const el = e.target as HTMLElement | null
      if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable)) return
      e.preventDefault()
      searchBox.current?.focus()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])

  // A search result with an anchor opens its section scrolled to that row, which flashes once so the eye finds it.
  const anchor = location.hash.slice(1)
  const sectionId = location.pathname.split('/')[2]
  useEffect(() => {
    if (!anchor) return
    const row = body.current?.querySelector<HTMLElement>(`[id="setting-${anchor}"]`)
    if (!row) return
    row.scrollIntoView({ block: 'start', behavior: 'smooth' })
    row.classList.remove('setting-flash')
    void row.offsetWidth // restart the animation if the same row is picked twice
    row.classList.add('setting-flash')
    const done = () => row.classList.remove('setting-flash')
    row.addEventListener('animationend', done, { once: true })
    return () => row.removeEventListener('animationend', done)
  }, [anchor, sectionId])

  if (!def) return <Navigate to="/settings/general" replace state={state} />

  const go = (id: SettingsSectionId) => navigate('/settings/' + id, { replace: true, state })
  const pick = (hit: SettingsHit) => navigate({ pathname: '/settings/' + hit.section, hash: hit.anchor ?? '' }, { replace: true, state })
  const searching = query.trim() !== ''

  const item = (s: SettingsSectionDef) => {
    const on = s.id === def.id
    return (
      <button key={s.id} type="button" onClick={() => go(s.id)} aria-current={on ? 'page' : undefined}
        className={`flex shrink-0 items-center gap-2.5 rounded-control px-3 py-2 text-left text-[13.5px] transition-colors lg:w-full
          ${on ? 'bg-card font-semibold text-ink shadow-card' : 'text-ink-2 hover:bg-card/70 hover:text-ink'}`}>
        <i className={`ti ti-${s.icon} text-lg ${on ? 'text-accent' : 'text-ink-3'}`} aria-hidden="true" />
        <span className="whitespace-nowrap">{s.label}</span>
      </button>
    )
  }

  return (
    <div className="fixed inset-0 z-[45] grid place-items-center bg-scrim lg:p-6" role="presentation">
      <div ref={dialog} data-settings-window role="dialog" aria-modal="true" aria-labelledby="settings-title" tabIndex={-1}
        className="relative flex h-full w-full flex-col overflow-hidden bg-card text-ink shadow-pop outline-none lg:h-[min(860px,100%)] lg:w-[min(1180px,100%)] lg:flex-row lg:rounded-2xl lg:border lg:border-line">
        <button type="button" onClick={close} aria-label="ปิดการตั้งค่า" title="ปิด (Esc)"
          className="absolute right-3 top-3 z-10 flex h-9 w-9 items-center justify-center rounded-control text-ink-2 hover:bg-surface hover:text-ink">
          <i className="ti ti-x text-lg" aria-hidden="true" />
        </button>

        <nav aria-label="หมวดการตั้งค่า"
          className="shrink-0 border-b border-line bg-surface px-3 pb-2 pt-3 lg:w-64 lg:overflow-y-auto lg:border-b-0 lg:border-r lg:px-3.5 lg:py-6">
          <h2 id="settings-title" className="px-2 pb-2 text-lg font-semibold lg:pb-3 lg:text-2xl">ตั้งค่า</h2>
          <div className="pb-2 lg:pb-4">
            <SettingsSearch visible={visibleIds} onPick={pick} inputRef={searchBox} query={query} setQuery={setQuery} />
          </div>
          {!searching && <div className="flex gap-1 overflow-x-auto pb-1 lg:hidden">{visible.map(item)}</div>}
          {!searching && (
            <div className="hidden space-y-5 lg:block">
              {SETTINGS_GROUPS.map((g) => {
                const items = visible.filter((s) => s.group === g.id)
                if (items.length === 0) return null
                return (
                  <div key={g.id} className="space-y-0.5">
                    <p className="px-3 text-xs font-bold text-ink-3">{g.label}</p>
                    <p className="px-3 pb-1 text-[11px] text-ink-3">{g.note}</p>
                    {items.map(item)}
                  </div>
                )
              })}
            </div>
          )}
        </nav>

        <div className="flex min-h-0 min-w-0 flex-1 flex-col">
          <div className="px-5 pb-3 pt-4 pr-14 lg:px-9 lg:pt-7">
            <h3 className="flex items-center gap-2.5 text-xl font-semibold lg:text-[22px]">
              <i className={`ti ti-${def.icon} text-xl text-ink-3`} aria-hidden="true" />{def.label}
            </h3>
            <p className="mt-0.5 text-[13px] text-ink-3">{def.desc}</p>
          </div>
          <div ref={body} className="min-h-0 flex-1 overflow-y-auto px-5 pb-12 pt-2 lg:px-9">
            {allowed ? (
              <EmbeddedContext.Provider value>
                <Suspense fallback={<div className="space-y-3" role="status" aria-label="กำลังโหลด">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-14" />)}</div>}>
                  <SectionBody key={def.id} id={def.id} />
                </Suspense>
              </EmbeddedContext.Provider>
            ) : (
              <EmptyState icon="lock" title="ไม่มีสิทธิ์เข้าหน้านี้">ติดต่อผู้ดูแลระบบหากต้องการใช้งานส่วนนี้</EmptyState>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
