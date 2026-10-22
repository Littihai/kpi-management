import { useEffect, useRef, useState } from 'react'
import DatabaseConnectionDialog from './DatabaseConnectionDialog'
import ServerAddressDialog from './ServerAddressDialog'
import { currentServerUrl, describeServer, serverChoiceAllowed } from '../lib/server'
import { useSetupStore } from '../stores/setupStore'

/**
 * The hamburger button at the top left of the sign-in page: where the app is connected to. The server the app talks to (when the app
 * may choose) and the database that server uses. A dot on the button says the database is not connected yet.
 */
export default function ConnectionMenu({ openDatabaseAtStart = false }: { openDatabaseAtStart?: boolean }) {
  const setup = useSetupStore((s) => s.status)
  const [menuOpen, setMenuOpen] = useState(false)
  const [serverOpen, setServerOpen] = useState(false)
  const [databaseOpen, setDatabaseOpen] = useState(false)
  const [autoOpened, setAutoOpened] = useState(false)
  const box = useRef<HTMLDivElement>(null)

  // a server with no database at all: take the person straight to connecting it, once
  const shouldAutoOpen = openDatabaseAtStart && setup?.reason === 'NotConfigured' && !autoOpened
  useEffect(() => {
    if (!shouldAutoOpen) return
    const timer = window.setTimeout(() => { setAutoOpened(true); setDatabaseOpen(true) }, 0)
    return () => window.clearTimeout(timer)
  }, [shouldAutoOpen])

  useEffect(() => {
    if (!menuOpen) return
    const close = (e: MouseEvent | KeyboardEvent) => {
      if (e instanceof KeyboardEvent) { if (e.key === 'Escape') setMenuOpen(false); return }
      if (box.current && !box.current.contains(e.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', close) }
  }, [menuOpen])

  const choose = (open: () => void) => { setMenuOpen(false); open() }

  return (
    <div ref={box} className="fixed left-3 top-3 z-30">
      <button type="button" aria-label="ตั้งค่าการเชื่อมต่อ" aria-haspopup="menu" aria-expanded={menuOpen} onClick={() => setMenuOpen((v) => !v)}
        className="relative flex h-10 w-10 items-center justify-center rounded-control border border-line bg-card text-ink-2 shadow-card hover:bg-surface">
        <i className="ti ti-menu-2 text-xl" aria-hidden="true" />
        {setup && <span className="absolute -right-1 -top-1 h-3 w-3 rounded-full border-2 border-card bg-amber-dot" aria-hidden="true" />}
      </button>

      {menuOpen && (
        <div role="menu" aria-label="ตั้งค่าการเชื่อมต่อ" className="mt-2 w-72 overflow-hidden rounded-card border border-line bg-card shadow-pop">
          <p className="border-b border-line px-4 py-2.5 text-xs font-semibold text-ink-3">ตั้งค่าการเชื่อมต่อ</p>
          {serverChoiceAllowed() && (
            <button type="button" role="menuitem" onClick={() => choose(() => setServerOpen(true))}
              className="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-surface">
              <i className="ti ti-server mt-0.5 text-lg text-accent" aria-hidden="true" />
              <span className="min-w-0">
                <span className="block text-sm font-medium text-ink">เซิร์ฟเวอร์ที่แอปเชื่อมต่อ</span>
                <span className="block truncate text-xs text-ink-3">{describeServer(currentServerUrl())}</span>
              </span>
            </button>
          )}
          <button type="button" role="menuitem" onClick={() => choose(() => setDatabaseOpen(true))}
            className="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-surface">
            <i className="ti ti-database mt-0.5 text-lg text-accent" aria-hidden="true" />
            <span className="min-w-0">
              <span className="block text-sm font-medium text-ink">ฐานข้อมูล (SQL Server)</span>
              <span className={`block text-xs ${setup ? 'text-amber-ink' : 'text-ink-3'}`}>{setup ? 'ยังไม่ได้เชื่อมต่อ กดเพื่อตั้งค่า' : 'เชื่อมต่อแล้ว'}</span>
            </span>
          </button>
        </div>
      )}

      {serverOpen && <ServerAddressDialog open onClose={() => setServerOpen(false)} />}
      {databaseOpen && <DatabaseConnectionDialog open onClose={() => setDatabaseOpen(false)} />}
    </div>
  )
}
