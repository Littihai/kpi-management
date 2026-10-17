import { useCallback, useEffect, useState } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { issueService } from '../services/issue.service'
import { useIssueEvents, useRealtimeStatus } from './realtimeContext'
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

const statusText = {
  connected: { label: 'เชื่อมต่อแบบสด', dot: 'bg-green-dot' },
  connecting: { label: 'กำลังเชื่อมต่อ…', dot: 'bg-amber-dot' },
  reconnecting: { label: 'กำลังเชื่อมต่อใหม่…', dot: 'bg-amber-dot' },
  offline: { label: 'ไม่ได้เชื่อมต่อแบบสด', dot: 'bg-ink-4' },
} as const

export default function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()
  const status = useRealtimeStatus()
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

  const connection = statusText[status]

  return (
    <div className="flex h-full flex-col bg-surface">
      <div className="flex items-center gap-3 px-5 pb-4 pt-6">
        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-accent text-white" aria-hidden="true">
          <i className="ti ti-chart-donut-3 text-xl" />
        </span>
        <div className="min-w-0">
          <p className="text-[15px] font-semibold leading-tight text-ink">KPI และแจ้งปัญหา</p>
          <p className="text-xs text-ink-3">ระบบภายในองค์กร</p>
        </div>
      </div>

      <nav aria-label="เมนูหลัก" className="flex-1 overflow-y-auto px-3 pb-4">
        {groups.map((group) => {
          const items = group.items.filter((i) => !i.permission || permissions.includes(i.permission))
          if (items.length === 0) return null
          return (
            <div key={group.title} className="mt-5 first:mt-2">
              <p className="px-3 pb-1.5 text-[11px] font-medium uppercase tracking-[0.08em] text-ink-3">{group.title}</p>
              <ul className="space-y-0.5">
                {items.map((item) => (
                  <li key={item.to}>
                    <NavLink to={item.to} onClick={onNavigate}
                      className={({ isActive }) =>
                        `group relative flex items-center gap-3 rounded-control px-3 py-2.5 text-sm transition-colors
                        ${isActive ? 'bg-white font-medium text-ink shadow-card ring-1 ring-line' : 'text-ink-2 hover:bg-surface-2 hover:text-ink'}`}>
                      {({ isActive }) => (
                        <>
                          {isActive && <span className="absolute left-0 top-2.5 h-5 w-[3px] rounded-r bg-accent" aria-hidden="true" />}
                          <i className={`ti ti-${item.icon} text-lg ${isActive ? 'text-accent' : 'text-ink-3 group-hover:text-ink-2'}`} aria-hidden="true" />
                          <span className="flex-1">{item.label}</span>
                          {item.badge ? (
                            <span className={`tabular rounded-full px-2 py-0.5 text-xs font-medium
                              ${overdue > 0 && item.to === '/issues' ? 'bg-red-soft text-red-ink' : 'bg-surface-2 text-ink-2'}`}
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

      <div className="border-t border-line p-3">
        <p className="mb-2 flex items-center gap-2 px-2 text-xs text-ink-3" role="status">
          <span className={`h-1.5 w-1.5 rounded-full ${connection.dot}`} aria-hidden="true" />
          {connection.label}
        </p>
        <div className="flex items-center gap-3 rounded-control px-2 py-1.5">
          <Avatar name={user?.fullName ?? ''} size={36} />
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-ink">{user?.fullName}</p>
            <p className="truncate text-xs text-ink-3">{roleLabel[user?.role ?? ''] ?? user?.role}</p>
          </div>
          <button type="button" aria-label="ออกจากระบบ" title="ออกจากระบบ"
            onClick={() => { logout(); navigate('/login') }}
            className="flex h-9 w-9 items-center justify-center rounded-control text-ink-3 hover:bg-surface-2 hover:text-ink">
            <i className="ti ti-logout text-lg" aria-hidden="true" />
          </button>
        </div>
      </div>
    </div>
  )
}
