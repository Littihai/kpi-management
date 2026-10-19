import { useCallback, useEffect, useState } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { issueService } from '../services/issue.service'
import { useIssueEvents } from './realtimeContext'
import ConnectionStatus from './ConnectionStatus'
import { Avatar } from './ui/Layout'
import { roleLabel } from '../utils/labels'

interface NavItem {
  to: string
  icon: string
  label: string
  /** Hidden unless the signed-in user holds this permission. */
  permission?: string
  badge?: number
}

/**
 * Lotline-style menu: a 56px brand row that lines up with the top bar, a soft tinted selected item with an accent bar
 * on its left edge, and group captions. `showAccount` puts the signed-in person at the bottom (the phone drawer, which
 * has no top bar account menu); on desktop that lives in the top bar.
 */
export default function Sidebar({ onNavigate, showAccount = false }: { onNavigate?: () => void; showAccount?: boolean }) {
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()
  const permissions = user?.permissions ?? []
  const [openIssues, setOpenIssues] = useState(0)
  const [overdue, setOverdue] = useState(0)
  const isIt = permissions.includes('issue.manage')

  const refreshCounts = useCallback(() => {
    issueService.summary()
      .then((s) => {
        setOpenIssues(isIt ? s.open : s.mine)
        setOverdue(isIt ? s.overdue : 0)
      })
      .catch(() => { /* the badge is a convenience; the page shows the real numbers */ })
  }, [isIt])

  useEffect(() => { refreshCounts() }, [refreshCounts])
  useIssueEvents(refreshCounts)

  const groups: { title: string; items: NavItem[] }[] = [
    {
      title: 'ภาพรวม',
      items: [
        { to: '/dashboard', icon: 'layout-dashboard', label: 'แดชบอร์ด', permission: 'kpi.view' },
        { to: '/kpi', icon: 'target-arrow', label: 'KPI', permission: 'kpi.view' },
      ],
    },
    {
      title: 'ซัพพอร์ต',
      items: [
        { to: '/issues', icon: 'lifebuoy', label: isIt ? 'งานแจ้งปัญหา' : 'เรื่องที่ฉันแจ้ง', permission: 'issue.view', badge: openIssues },
      ],
    },
    {
      title: 'ผู้ดูแลระบบ',
      items: [
        { to: '/admin/users', icon: 'users', label: 'ผู้ใช้งาน', permission: 'user.view' },
        { to: '/admin/overview', icon: 'activity-heartbeat', label: 'ภาพรวมระบบ', permission: 'overview.view' },
        { to: '/admin/audit', icon: 'history', label: 'บันทึกการใช้งาน', permission: 'overview.view' },
      ],
    },
  ]


  return (
    <div className="flex h-full flex-col border-r border-line bg-white">
      <div className="flex h-14 flex-none items-center gap-2.5 border-b border-line px-4">
        <span className="flex h-[30px] w-[30px] items-center justify-center rounded-lg bg-accent text-white" aria-hidden="true">
          <i className="ti ti-chart-donut-3 text-lg" />
        </span>
        <p className="min-w-0 truncate text-base font-bold tracking-tight text-ink">KPI และแจ้งปัญหา</p>
      </div>

      <nav aria-label="เมนูหลัก" className="flex-1 overflow-y-auto px-2.5 py-2">
        {groups.map((group) => {
          const items = group.items.filter((i) => !i.permission || permissions.includes(i.permission))
          if (items.length === 0) return null
          return (
            <div key={group.title} className="mt-3 first:mt-1">
              <p className="px-2.5 pb-1 text-xs font-semibold text-ink-3">{group.title}</p>
              <ul className="space-y-0.5">
                {items.map((item) => (
                  <li key={item.to}>
                    <NavLink to={item.to} onClick={onNavigate}
                      className={({ isActive }) =>
                        `group relative flex items-center gap-2.5 rounded-control px-2.5 py-2 text-sm font-medium transition-colors
                        ${isActive ? 'bg-accent-soft text-accent-deep' : 'text-ink hover:bg-surface'}`}>
                      {({ isActive }) => (
                        <>
                          {isActive && <span className="absolute -left-2.5 bottom-2 top-2 w-[3px] rounded-r bg-accent" aria-hidden="true" />}
                          <i className={`ti ti-${item.icon} text-lg ${isActive ? 'text-accent' : 'text-ink-3'}`} aria-hidden="true" />
                          <span className="min-w-0 flex-1 truncate">{item.label}</span>
                          {item.badge ? (
                            <span className={`tabular rounded-full px-1.5 text-[10.5px] font-bold leading-[1.6]
                              ${overdue > 0 && item.to === '/issues' ? 'bg-red-dot text-white' : isActive ? 'bg-accent text-white' : 'bg-surface-2 text-ink-2'}`}
                              aria-label={`${item.badge} เรื่อง${overdue > 0 && item.to === '/issues' ? ` เกินกำหนด ${overdue}` : ''}`}>
                              {item.badge}
                            </span>
                          ) : null}
                        </>
                      )}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          )
        })}
      </nav>

      <div className="flex-none border-t border-line p-2.5">
        <ConnectionStatus className={showAccount ? 'mb-2 px-2' : 'px-2 py-1'} />
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
