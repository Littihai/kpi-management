import { useCallback, useEffect, useState } from 'react'
import { issueViews } from '../lib/issueViews'
import { useAttentionCountsStore } from '../stores/attentionCountsStore'
import { useAuthStore } from '../stores/authStore'
import { useNotificationStore } from '../stores/notificationStore'
import { issueService } from '../services/issue.service'
import { useIssueEvents } from './realtimeContext'

export interface NavItem {
  /** A path, optionally with a query (`/issues?view=near`): the query is part of what makes the item the current one. */
  to: string
  icon: string
  label: string
  badge?: number
  /** What the badge counts, for screen readers ("5 เรื่อง"); issues unless said otherwise. */
  unit?: string
  /** Something here is late: the badge turns red. */
  hot?: boolean
}

/** The server counts unread notifications up to 100, so the top of the scale is shown as "99+". */
export function unreadText(count: number): string {
  return count > 99 ? '99+' : String(count)
}

/**
 * Whether `item` is the page on screen. A router link only compares paths, so two entries for the same list with
 * different `?view=` would both light up (and the plain list would light up on a view); this also compares the query,
 * and keeps the plain list lit on an issue's own page, as a link would.
 */
export function isItemActive(item: Pick<NavItem, 'to'>, pathname: string, search: string): boolean {
  const [path, query = ''] = item.to.split('?')
  const wanted = new URLSearchParams(query)
  const actual = new URLSearchParams(search)
  if (wanted.has('view')) return pathname === path && actual.get('view') === wanted.get('view')
  if (pathname === path) return !(path === '/issues' && issueViews.some((v) => v.key === actual.get('view')))
  return pathname.startsWith(`${path}/`)
}

export interface NavGroup {
  title: string
  items: NavItem[]
}

/**
 * The menu, shared by every layout (sidebar, icon rail, top bar): only what the signed-in person may open, with the
 * open-issue count (and whether any are overdue) that the support entry shows.
 */
export function useNavGroups(): { groups: NavGroup[]; overdue: number } {
  const permissions = useAuthStore((s) => s.user?.permissions) ?? []
  const [openIssues, setOpenIssues] = useState(0)
  const [overdue, setOverdue] = useState(0)
  const isIt = permissions.includes('issue.manage')
  const counts = useAttentionCountsStore()
  const unread = useNotificationStore((s) => s.unread)

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

  const all: { title: string; items: (NavItem & { permission?: string })[] }[] = [
    {
      title: 'หน้าหลัก',
      items: [
        { to: '/dashboard', icon: 'layout-dashboard', label: 'แดชบอร์ด', permission: 'issue.view' },
        { to: '/notifications', icon: 'bell', label: 'การแจ้งเตือน', permission: 'issue.view', badge: unread, unit: 'รายการที่ยังไม่อ่าน' },
      ],
    },
    {
      title: 'ขอความช่วยเหลือ',
      items: [
        { to: '/issues', icon: 'lifebuoy', label: isIt ? 'งานแจ้งปัญหา' : 'เรื่องที่ฉันแจ้ง', permission: 'issue.view', badge: openIssues, hot: overdue > 0 },
        ...issueViews.map((view) => ({
          to: view.to,
          icon: view.icon,
          label: view.label,
          permission: view.itOnly ? 'issue.manage' : 'issue.view',
          badge: { pending: counts.pending, assigned: counts.assigned, near: counts.nearSla }[view.key],
          hot: view.key === 'near',
        })),
      ],
    },
    {
      title: 'ผู้ดูแลระบบ',
      items: [
        { to: '/admin/overview', icon: 'activity-heartbeat', label: 'ภาพรวมระบบ', permission: 'overview.view' },
      ],
    },
  ]

  const groups = all
    .map((g) => ({ title: g.title, items: g.items.filter((i) => !i.permission || permissions.includes(i.permission)) }))
    .filter((g) => g.items.length > 0)

  return { groups, overdue }
}
