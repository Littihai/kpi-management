import { useCallback, useEffect, useState } from 'react'
import { useAuthStore } from '../stores/authStore'
import { issueService } from '../services/issue.service'
import { useIssueEvents } from './realtimeContext'

export interface NavItem {
  to: string
  icon: string
  label: string
  badge?: number
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
      ],
    },
    {
      title: 'ขอความช่วยเหลือ',
      items: [
        { to: '/issues', icon: 'lifebuoy', label: isIt ? 'งานแจ้งปัญหา' : 'เรื่องที่ฉันแจ้ง', permission: 'issue.view', badge: openIssues },
      ],
    },
    {
      title: 'ผู้ดูแลระบบ',
      items: [
        { to: '/admin/users', icon: 'users', label: 'ผู้ใช้งาน', permission: 'user.view' },
        { to: '/admin/departments', icon: 'building', label: 'แผนก', permission: 'department.manage' },
        { to: '/admin/overview', icon: 'activity-heartbeat', label: 'ภาพรวมระบบ', permission: 'overview.view' },
        { to: '/admin/audit', icon: 'history', label: 'บันทึกการใช้งาน', permission: 'overview.view' },
      ],
    },
  ]

  const groups = all
    .map((g) => ({ title: g.title, items: g.items.filter((i) => !i.permission || permissions.includes(i.permission)) }))
    .filter((g) => g.items.length > 0)

  return { groups, overdue }
}
