import { useEffect, useState } from 'react'
import { Link, Outlet, useLocation } from 'react-router-dom'
import Sidebar from './Sidebar'
import AccountMenu from './AccountMenu'
import ConnectionStatus from './ConnectionStatus'
import { RealtimeProvider } from './RealtimeProvider'
import { authService } from '../services/auth.service'
import { useAuthStore } from '../stores/authStore'

/** Signed-in area: sidebar (a drawer on phones), live updates, and the always-there "report a problem" button. */
export default function AppShell() {
  const updateUser = useAuthStore((s) => s.updateUser)
  const canReport = useAuthStore((s) => s.user?.permissions?.includes('issue.create') ?? false)
  const location = useLocation()
  // The drawer is tied to the page it was opened on, so navigating closes it without an effect.
  const [menuPath, setMenuPath] = useState<string | null>(null)
  const menuOpen = menuPath === location.pathname
  const setMenuOpen = (open: boolean) => setMenuPath(open ? location.pathname : null)

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

  return (
    <RealtimeProvider>
      <div className="min-h-screen lg:flex">
        {/* Desktop sidebar */}
        <aside className="sticky top-0 hidden h-screen w-[252px] shrink-0 lg:block">
          <Sidebar />
        </aside>

        {/* Phone top bar + drawer */}
        <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-line bg-white px-3 lg:hidden">
          <button type="button" aria-label="เปิดเมนู" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)}
            className="flex h-10 w-10 items-center justify-center rounded-control text-ink-2 hover:bg-surface">
            <i className="ti ti-menu-2 text-xl" aria-hidden="true" />
          </button>
          <span className="flex h-[30px] w-[30px] items-center justify-center rounded-lg bg-accent text-white" aria-hidden="true">
            <i className="ti ti-chart-donut-3" />
          </span>
          <span className="text-base font-bold tracking-tight text-ink">KPI และแจ้งปัญหา</span>
        </header>

        {menuOpen && (
          <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="เมนู">
            <div className="absolute inset-0 bg-ink/50" onClick={() => setMenuOpen(false)} />
            <div className="absolute inset-y-0 left-0 w-72 max-w-[85vw] shadow-pop">
              <Sidebar showAccount onNavigate={() => setMenuOpen(false)} />
            </div>
          </div>
        )}

        <div className="flex min-w-0 flex-1 flex-col">
          {/* Desktop top bar (as in Lotline): live-update status on the left, the account menu on the right */}
          <header className="sticky top-0 z-30 hidden h-14 flex-none items-center justify-between gap-4 border-b border-line bg-white px-6 lg:flex">
            <ConnectionStatus />
            <AccountMenu />
          </header>
          <main className="min-w-0 flex-1" id="main">
            <Outlet />
          </main>
        </div>

        {canReport && !onIssuePages && (
          <Link to="/issues/new" state={{ from: location.pathname }}
            className="fixed bottom-4 right-4 z-30 flex h-11 items-center gap-2 rounded-full bg-accent px-5 text-[13px] font-semibold text-white shadow-pop hover:bg-accent-hover sm:bottom-6 sm:right-6">
            <i className="ti ti-lifebuoy text-lg" aria-hidden="true" />
            <span className="hidden sm:inline">แจ้งปัญหา</span>
            <span className="sr-only sm:hidden">แจ้งปัญหา</span>
          </Link>
        )}
      </div>
    </RealtimeProvider>
  )
}
