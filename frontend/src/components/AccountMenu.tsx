import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { Avatar } from './ui/Layout'
import { roleLabel } from '../utils/labels'

/** The signed-in person in the top bar; opens a small menu with who they are and the way out (as in Lotline). */
export default function AccountMenu({ onOpenSettings }: { onOpenSettings?: () => void }) {
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!user) return null
  const role = roleLabel[user.role ?? ''] ?? user.role

  return (
    <div ref={root} className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen((v) => !v)}
        className={`flex items-center gap-2.5 rounded-lg px-2 py-1 text-left transition-colors hover:bg-surface ${open ? 'bg-surface' : ''}`}>
        <Avatar name={user.fullName} size={32} />
        <span className="flex max-w-[170px] flex-col leading-tight">
          <span className="truncate text-[13.5px] font-semibold text-ink">{user.fullName}</span>
          <span className="truncate text-xs text-ink-3">{role}</span>
        </span>
        <i className={`ti ti-chevron-down text-sm text-ink-3 transition-transform ${open ? 'rotate-180' : ''}`} aria-hidden="true" />
      </button>

      {open && (
        <div role="menu" className="absolute right-0 top-full z-40 mt-2 w-64 overflow-hidden rounded-card border border-line bg-card shadow-pop">
          <div className="border-b border-line px-4 py-3">
            <p className="truncate text-sm font-semibold text-ink">{user.fullName}</p>
            <p className="truncate text-xs text-ink-3">{role}{user.departmentName ? ` · ${user.departmentName}` : ''}</p>
          </div>
          <div className="p-1.5">
            {onOpenSettings && (
              <button type="button" role="menuitem" onClick={() => { setOpen(false); onOpenSettings() }}
                className="flex w-full items-center gap-2.5 rounded-control px-2.5 py-2 text-left text-sm font-medium text-ink hover:bg-surface">
                <i className="ti ti-adjustments-horizontal text-lg text-ink-3" aria-hidden="true" />
                ตั้งค่า
              </button>
            )}
            <button type="button" role="menuitem"
              onClick={() => { setOpen(false); logout(); navigate('/login') }}
              className="flex w-full items-center gap-2.5 rounded-control px-2.5 py-2 text-left text-sm font-medium text-ink hover:bg-surface">
              <i className="ti ti-logout text-lg text-ink-3" aria-hidden="true" />
              ออกจากระบบ
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
