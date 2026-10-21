import { NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import ConnectionStatus from './ConnectionStatus'
import { Avatar } from './ui/Layout'
import { roleLabel } from '../utils/labels'
import { useNavGroups } from './useNavGroups'

/**
 * Lotline-style menu: a 56px brand row that lines up with the top bar, a soft tinted selected item with an accent bar
 * on its left edge, and group captions. `rail` shows icons only (the label becomes the tooltip). `showAccount` puts the
 * signed-in person at the bottom (the phone drawer, which has no top bar account menu).
 */
export default function Sidebar({ onNavigate, showAccount = false, rail = false }: {
  onNavigate?: () => void
  showAccount?: boolean
  rail?: boolean
}) {
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()
  const { groups, overdue } = useNavGroups()

  return (
    <div className="flex h-full flex-col border-r border-line bg-card">
      <div className={`flex h-14 flex-none items-center gap-2.5 border-b border-line ${rail ? 'justify-center px-0' : 'px-4'}`}>
        <span className="flex h-[30px] w-[30px] items-center justify-center rounded-lg bg-accent text-on-accent" aria-hidden="true">
          <i className="ti ti-chart-donut-3 text-lg" />
        </span>
        {!rail && <p className="min-w-0 truncate text-base font-bold tracking-tight text-ink">Helpdesk</p>}
      </div>

      <nav aria-label="เมนูหลัก" className={`flex-1 overflow-y-auto py-2 ${rail ? 'px-2' : 'px-2.5'}`}>
        {groups.map((group) => (
          <div key={group.title} className="mt-3 first:mt-1">
            {!rail && <p className="px-2.5 pb-1 text-xs font-semibold text-ink-3">{group.title}</p>}
            {rail && <div className="mx-2 mb-1 border-t border-line first:hidden" aria-hidden="true" />}
            <ul className="space-y-0.5">
              {group.items.map((item) => {
                const hot = overdue > 0 && item.to === '/issues'
                return (
                  <li key={item.to}>
                    <NavLink to={item.to} onClick={onNavigate} title={rail ? item.label : undefined} aria-label={rail ? item.label : undefined}
                      className={({ isActive }) =>
                        `group relative flex items-center rounded-control py-2 text-sm font-medium transition-colors
                        ${rail ? 'justify-center px-0' : 'gap-2.5 px-2.5'}
                        ${isActive ? 'bg-accent-soft text-accent-deep' : 'text-ink hover:bg-surface'}`}>
                      {({ isActive }) => (
                        <>
                          {isActive && <span className={`absolute bottom-2 top-2 w-[3px] rounded-r bg-accent ${rail ? '-left-2' : '-left-2.5'}`} aria-hidden="true" />}
                          <i className={`ti ti-${item.icon} text-lg ${isActive ? 'text-accent' : 'text-ink-3'}`} aria-hidden="true" />
                          {!rail && <span className="min-w-0 flex-1 truncate">{item.label}</span>}
                          {item.badge ? (
                            rail ? (
                              <span className={`absolute right-1.5 top-1 h-2 w-2 rounded-full ${hot ? 'bg-red-dot' : 'bg-accent'}`} aria-hidden="true" />
                            ) : (
                              <span className={`tabular rounded-full px-1.5 text-[10.5px] font-bold leading-[1.6]
                                ${hot ? 'bg-red-dot text-white' : isActive ? 'bg-accent text-on-accent' : 'bg-surface-2 text-ink-2'}`}
                                aria-label={`${item.badge} เรื่อง${hot ? ` เกินกำหนด ${overdue}` : ''}`}>
                                {item.badge}
                              </span>
                            )
                          ) : null}
                        </>
                      )}
                    </NavLink>
                  </li>
                )
              })}
            </ul>
          </div>
        ))}
      </nav>

      <div className="flex-none border-t border-line p-2.5">
        {!rail && <ConnectionStatus className={showAccount ? 'mb-2 px-2' : 'px-2 py-1'} />}
        {showAccount && (
          <div className="flex items-center gap-3 rounded-control px-2 py-1.5">
            <Avatar name={user?.fullName ?? ''} size={34} />
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-semibold text-ink">{user?.fullName}</p>
              <p className="truncate text-xs text-ink-3">{roleLabel[user?.role ?? ''] ?? user?.role}</p>
            </div>
            <button type="button" aria-label="ออกจากระบบ" title="ออกจากระบบ"
              onClick={() => { logout(); navigate('/login') }}
              className="flex h-9 w-9 items-center justify-center rounded-control text-ink-3 hover:bg-surface hover:text-ink">
              <i className="ti ti-logout text-lg" aria-hidden="true" />
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
