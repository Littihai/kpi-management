import { useEffect, useState } from 'react'
import { Link, Outlet, useLocation } from 'react-router-dom'
import Sidebar from './Sidebar'
import TopNav from './TopNav'
import AccountMenu from './AccountMenu'
import ConnectionStatus from './ConnectionStatus'
import SettingsPanel from './SettingsPanel'
import { useAppearance } from './appearanceContext'
import { RealtimeProvider } from './RealtimeProvider'
import { authService } from '../services/auth.service'
import { useAuthStore } from '../stores/authStore'

/** Round icon buttons in the top bar: light/dark switch and the settings panel. */
function BarButtons({ onOpenSettings, settingsOpen }: { onOpenSettings: () => void; settingsOpen: boolean }) {
  const { resolvedTheme, set } = useAppearance()
  const dark = resolvedTheme === 'dark'
  const cls = 'flex h-9 w-9 items-center justify-center rounded-control text-ink-2 transition-colors hover:bg-surface hover:text-ink'
  return (
    <>
      <button type="button" className={cls} onClick={() => set('theme', dark ? 'light' : 'dark')}
        aria-label={dark ? 'เปลี่ยนเป็นธีมสว่าง' : 'เปลี่ยนเป็นธีมมืด'} title={dark ? 'ธีมสว่าง' : 'ธีมมืด'}>
        <i className={`ti ti-${dark ? 'sun' : 'moon'} text-lg`} aria-hidden="true" />
      </button>
      <button type="button" id="open-settings" className={`${cls} ${settingsOpen ? 'bg-surface' : ''}`} onClick={onOpenSettings}
        aria-label="ตั้งค่า" aria-expanded={settingsOpen} aria-controls="settings-panel" title="ตั้งค่า">
        <i className="ti ti-adjustments-horizontal text-lg" aria-hidden="true" />
      </button>
    </>
  )
}

/** Signed-in area: menu (sidebar, icon rail or top bar; a drawer on phones), live updates, settings, and the "report a problem" button. */
export default function AppShell() {
  const updateUser = useAuthStore((s) => s.updateUser)
  const canReport = useAuthStore((s) => s.user?.permissions?.includes('issue.create') ?? false)
  const { nav } = useAppearance()
  const location = useLocation()
  // The drawer is tied to the page it was opened on, so navigating closes it without an effect.
  const [menuPath, setMenuPath] = useState<string | null>(null)
  const menuOpen = menuPath === location.pathname
  const setMenuOpen = (open: boolean) => setMenuPath(open ? location.pathname : null)
  const [settingsOpen, setSettingsOpen] = useState(false)

  // Permissions are decided by the server and can change between sign-ins, so refresh them on load.
  useEffect(() => {
    authService.me()
      .then((profile) => updateUser({
        fullName: profile.fullName,
        role: profile.role,
        departmentId: profile.departmentId,
        departmentName: profile.departmentName,
        permissions: profile.permissions,
      }))
      .catch(() => { /* 401 is handled by the API client; other errors keep the cached profile */ })
  }, [updateUser])

  // Escape closes the drawer.
  useEffect(() => {
    if (!menuOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuPath(null) }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [menuOpen])

  const onIssuePages = location.pathname.startsWith('/issues/new')
  const sideLayout = nav !== 'top'

  return (
    <RealtimeProvider>
      <div className="min-h-[var(--vh100)] lg:flex">
        {/* Desktop sidebar / icon rail */}
        {sideLayout && (
          <aside className={`sticky top-0 hidden h-[var(--vh100)] shrink-0 lg:block ${nav === 'rail' ? 'w-[68px]' : 'w-[252px]'}`}>
            <Sidebar rail={nav === 'rail'} />
          </aside>
        )}

        {/* Phone top bar + drawer */}
        <header className="sticky top-0 z-30 flex h-14 items-center gap-1 border-b border-line bg-card px-3 lg:hidden">
          <button type="button" aria-label="เปิดเมนู" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)}
            className="flex h-10 w-10 items-center justify-center rounded-control text-ink-2 hover:bg-surface">
            <i className="ti ti-menu-2 text-xl" aria-hidden="true" />
          </button>
          <span className="ml-1 flex h-[30px] w-[30px] items-center justify-center rounded-lg bg-accent text-on-accent" aria-hidden="true">
            <i className="ti ti-chart-donut-3" />
          </span>
          <span className="ml-1.5 min-w-0 flex-1 truncate text-base font-bold tracking-tight text-ink">KPI และแจ้งปัญหา</span>
          <BarButtons onOpenSettings={() => setSettingsOpen((v) => !v)} settingsOpen={settingsOpen} />
        </header>

        {menuOpen && (
          <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="เมนู">
            <div className="absolute inset-0 bg-scrim" onClick={() => setMenuOpen(false)} />
            <div className="absolute inset-y-0 left-0 w-72 max-w-[85vw] shadow-pop">
              <Sidebar showAccount onNavigate={() => setMenuOpen(false)} />
            </div>
          </div>
        )}

        <div className="flex min-w-0 flex-1 flex-col">
          {/* Desktop top bar (as in Lotline): live-update status on the left, switches and the account menu on the right */}
          <header className="sticky top-0 z-30 hidden h-14 flex-none items-center justify-between gap-4 border-b border-line bg-card px-6 lg:flex">
            <div className="flex min-w-0 items-center gap-4">
              {!sideLayout && (
                <span className="flex items-center gap-2.5">
                  <span className="flex h-[30px] w-[30px] items-center justify-center rounded-lg bg-accent text-on-accent" aria-hidden="true">
                    <i className="ti ti-chart-donut-3 text-lg" />
                  </span>
                  <span className="text-base font-bold tracking-tight text-ink">KPI และแจ้งปัญหา</span>
                </span>
              )}
              <ConnectionStatus />
            </div>
            <div className="flex items-center gap-1">
              <BarButtons onOpenSettings={() => setSettingsOpen((v) => !v)} settingsOpen={settingsOpen} />
              <AccountMenu onOpenSettings={() => setSettingsOpen(true)} />
            </div>
          </header>
          {!sideLayout && <TopNav />}
          <main className="min-w-0 flex-1" id="main">
            <Outlet />
          </main>
        </div>

        <SettingsPanel open={settingsOpen} onClose={() => setSettingsOpen(false)} returnFocusId="open-settings" />

        {canReport && !onIssuePages && (
          <Link to="/issues/new" state={{ from: location.pathname }}
            className="fixed bottom-4 right-4 z-30 flex h-11 items-center gap-2 rounded-full bg-accent px-5 text-[13px] font-semibold text-on-accent shadow-pop hover:bg-accent-hover sm:bottom-6 sm:right-6">
            <i className="ti ti-lifebuoy text-lg" aria-hidden="true" />
            <span className="hidden sm:inline">แจ้งปัญหา</span>
            <span className="sr-only sm:hidden">แจ้งปัญหา</span>
          </Link>
        )}
      </div>
    </RealtimeProvider>
  )
}
