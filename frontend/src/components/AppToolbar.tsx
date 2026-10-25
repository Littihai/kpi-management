import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import BrandName from './BrandName'
import DatabaseConnectionDialog from './DatabaseConnectionDialog'
import DisconnectDialog from './DisconnectDialog'
import ServerAddressDialog from './ServerAddressDialog'
import SidebarToggle from './SidebarToggle'
import { useOpenSettings } from './settings/settingsNav'
import { describeEntry, useConnectionHistory, type ConnectionEntry } from '../lib/connectionHistory'
import { currentServerUrl, describeServer, isLocalServer, serverChoiceAllowed } from '../lib/server'
import { apiIsSameOrigin, setupService } from '../services/setup.service'
import { useAuthStore, useCan } from '../stores/authStore'
import { useSetupStore } from '../stores/setupStore'

/** What the "Connect" window is opened with: which tab, and an entry from the history to fill the form with. */
type DialogRequest = { tab: 'history' | 'connect'; prefill?: ConnectionEntry }

const button = 'flex h-8 w-8 flex-none items-center justify-center rounded-control text-ink-2 transition-colors hover:bg-surface hover:text-ink disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent'
const divider = <span className="mx-1 h-5 w-px flex-none bg-line" aria-hidden="true" />

/** Can the browser step back / forward? The Navigation API knows (Chromium, so the Windows app too); without it only "back" is guessed. */
function canGo(direction: 'back' | 'forward', locationKey: string): boolean {
  const navigation = (window as unknown as { navigation?: { canGoBack: boolean; canGoForward: boolean } }).navigation
  if (navigation) return direction === 'back' ? navigation.canGoBack : navigation.canGoForward
  return direction === 'back' ? locationKey !== 'default' : true
}

/**
 * The bar across the top of every page: the app's own buttons on the left (hide the side menu, back, forward, the
 * organisation's name), then the database buttons after SSMS (Connect ▾, connect, check the status, check the database)
 * and where the database stands. It replaces the hamburger button the sign-in page used to have.
 */
export default function AppToolbar({ sidebarToggle = false, autoOpenDatabase = false }: { sidebarToggle?: boolean; autoOpenDatabase?: boolean }) {
  const navigate = useNavigate()
  const location = useLocation()
  const openSettings = useOpenSettings()
  const signedIn = useAuthStore((s) => s.isAuthenticated)
  const canCheckDatabase = useCan('settings.view')
  const setup = useSetupStore((s) => s.status)
  const server = useSetupStore((s) => s.server)
  const setStatus = useSetupStore((s) => s.setStatus)
  const setRunning = useSetupStore((s) => s.setRunning)
  const history = useConnectionHistory()

  const [menuOpen, setMenuOpen] = useState(false)
  const [dialog, setDialog] = useState<DialogRequest | null>(null)
  const [serverOpen, setServerOpen] = useState(false)
  const [disconnectOpen, setDisconnectOpen] = useState(false)
  const [checking, setChecking] = useState(false)
  const [autoOpened, setAutoOpened] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)
  const firstItem = useRef<HTMLButtonElement>(null)

  // A server with no database at all: take the person straight to connecting it, once (not from a page that cannot do it:
  // another origin, such as the desktop app).
  const shouldAutoOpen = autoOpenDatabase && setup?.reason === 'NotConfigured' && !autoOpened && apiIsSameOrigin()
  useEffect(() => {
    if (!shouldAutoOpen) return
    const timer = window.setTimeout(() => { setAutoOpened(true); setDialog({ tab: 'connect' }) }, 0)
    return () => window.clearTimeout(timer)
  }, [shouldAutoOpen])

  useEffect(() => {
    if (!menuOpen) return
    firstItem.current?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setMenuOpen(false); toggle.current?.focus() } }
    const onDown = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setMenuOpen(false) }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onDown); document.removeEventListener('keydown', onKey) }
  }, [menuOpen])

  const choose = (open: () => void) => { setMenuOpen(false); open() }

  // Ask the server again where its database stands (it may have been fixed, or woken up, meanwhile).
  const refresh = async () => {
    setChecking(true)
    try {
      const probe = await setupService.probe()
      if (probe.kind === 'setup') setStatus(probe.status)
      else if (probe.kind === 'normal') setRunning()
    } finally {
      setChecking(false)
    }
  }

  const state = setup
    ? { text: setup.reason === 'NotConfigured' ? 'ยังไม่ได้เชื่อมต่อฐานข้อมูล' : 'ต่อฐานข้อมูลไม่ได้', dot: 'bg-amber-dot' }
    : server === 'running'
      ? { text: 'เชื่อมต่อฐานข้อมูลแล้ว', dot: 'bg-green-dot' }
      : { text: 'ยังตรวจสถานะไม่ได้', dot: 'bg-ink-4' }

  return (
    <div role="toolbar" aria-label="แถบเครื่องมือ"
      className="sticky top-0 z-40 flex h-[var(--toolbar-h)] items-center gap-0.5 border-b border-line bg-card px-2">
      {sidebarToggle && <span className="hidden lg:contents"><SidebarToggle /></span>}
      <button type="button" className={button} onClick={() => navigate(-1)} disabled={!canGo('back', location.key)} aria-label="ย้อนกลับ" title="ย้อนกลับ">
        <i className="ti ti-arrow-left text-lg" aria-hidden="true" />
      </button>
      <button type="button" className={button} onClick={() => navigate(1)} disabled={!canGo('forward', location.key)} aria-label="ไปข้างหน้า" title="ไปข้างหน้า">
        <i className="ti ti-arrow-right text-lg" aria-hidden="true" />
      </button>
      <BrandName className="ml-2 hidden min-w-0 max-w-[14rem] truncate text-[13px] font-semibold text-ink sm:block" />

      {divider}

      <div ref={box} className="relative">
        <button ref={toggle} type="button" aria-haspopup="menu" aria-expanded={menuOpen} aria-controls="connect-menu" onClick={() => setMenuOpen((v) => !v)}
          className="flex h-8 items-center gap-1 rounded-control px-2.5 text-[13px] font-medium text-ink transition-colors hover:bg-surface">
          เชื่อมต่อ<i className="ti ti-chevron-down text-xs text-ink-3" aria-hidden="true" />
        </button>

        {menuOpen && (
          <div id="connect-menu" role="menu" className="absolute left-0 top-full z-50 mt-1 w-72 overflow-hidden rounded-card border border-line bg-card shadow-pop">
            <button ref={firstItem} type="button" role="menuitem" onClick={() => choose(() => setDialog({ tab: 'connect' }))}
              className="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-surface">
              <i className="ti ti-plug-connected mt-0.5 text-lg text-accent" aria-hidden="true" />
              <span className="min-w-0">
                <span className="block text-sm font-medium text-ink">เชื่อมต่อฐานข้อมูล…</span>
                <span className={`block text-xs ${setup ? 'text-amber-ink' : 'text-ink-3'}`}>{state.text}</span>
              </span>
            </button>
            {serverChoiceAllowed() && (
              <button type="button" role="menuitem" onClick={() => choose(() => setServerOpen(true))}
                className="flex w-full items-start gap-3 border-t border-line px-4 py-3 text-left hover:bg-surface">
                <i className="ti ti-server mt-0.5 text-lg text-accent" aria-hidden="true" />
                <span className="min-w-0">
                  <span className="block text-sm font-medium text-ink">เซิร์ฟเวอร์ที่แอปเชื่อมต่อ…</span>
                  <span className="block truncate text-xs text-ink-3">{describeServer(currentServerUrl())}</span>
                </span>
              </button>
            )}
            {setup && history.length > 0 && (
              <div className="border-t border-line py-1">
                <p className="px-4 pb-1 pt-2 text-[11px] font-bold text-ink-3">ล่าสุด</p>
                {history.slice(0, 5).map((entry) => (
                  <button key={`${entry.server}|${entry.database}|${entry.auth}|${entry.user ?? ''}`} type="button" role="menuitem"
                    onClick={() => choose(() => setDialog({ tab: 'connect', prefill: entry }))}
                    className="flex w-full items-center gap-3 px-4 py-2 text-left hover:bg-surface">
                    <i className="ti ti-history text-base text-ink-3" aria-hidden="true" />
                    <span className="min-w-0">
                      <span className="block truncate text-[13px] font-medium text-ink">{entry.server}</span>
                      <span className="block truncate text-xs text-ink-3">{describeEntry(entry)}</span>
                    </span>
                  </button>
                ))}
                <button type="button" role="menuitem" onClick={() => choose(() => setDialog({ tab: 'history' }))}
                  className="w-full px-4 py-2 text-left text-xs font-medium text-accent hover:bg-surface">ดูประวัติทั้งหมด…</button>
              </div>
            )}
          </div>
        )}
      </div>

      <button type="button" className={button} onClick={() => setDialog({ tab: 'connect' })} aria-label="เชื่อมต่อฐานข้อมูล" title="เชื่อมต่อฐานข้อมูล">
        <i className="ti ti-plug-connected text-lg" aria-hidden="true" />
      </button>
      <button type="button" className={button} onClick={refresh} disabled={checking} aria-label="ตรวจสถานะการเชื่อมต่ออีกครั้ง" title="ตรวจสถานะการเชื่อมต่ออีกครั้ง">
        <i className={`ti ti-refresh text-lg ${checking ? 'animate-spin' : ''}`} aria-hidden="true" />
      </button>
      {server === 'running' && isLocalServer(currentServerUrl()) && (
        <button type="button" className={button} onClick={() => setDisconnectOpen(true)} aria-label="ยกเลิกการเชื่อมต่อฐานข้อมูล" title="ยกเลิกการเชื่อมต่อฐานข้อมูล (เพื่อต่อเซิร์ฟเวอร์อื่น)">
          <i className="ti ti-plug-x text-lg" aria-hidden="true" />
        </button>
      )}
      {signedIn && canCheckDatabase && (
        <button type="button" className={button} onClick={() => openSettings('database')} aria-label="ตรวจฐานข้อมูล" title="ตรวจฐานข้อมูล (ตั้งค่า → ฐานข้อมูล)">
          <i className="ti ti-activity text-lg" aria-hidden="true" />
        </button>
      )}

      <p className="ml-2 flex min-w-0 items-center gap-2 text-xs text-ink-3" role="status">
        <span className={`h-1.5 w-1.5 flex-none rounded-full ${state.dot}`} aria-hidden="true" />
        <span className="hidden truncate md:inline">{state.text}</span>
        <span className="sr-only md:hidden">{state.text}</span>
      </p>

      {dialog && <DatabaseConnectionDialog open tab={dialog.tab} prefill={dialog.prefill} onClose={() => { setDialog(null); toggle.current?.focus() }} />}
      {serverOpen && <ServerAddressDialog open onClose={() => setServerOpen(false)} />}
      {disconnectOpen && <DisconnectDialog open onClose={() => setDisconnectOpen(false)} />}
    </div>
  )
}
