import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import NotificationItem from '../components/NotificationItem'
import Button from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { Alert, EmptyState, Page, PageHeader, PageSkeleton, Pagination, Segmented } from '../components/ui/Layout'
import { notificationService } from '../services/notification.service'
import { refreshUnread, useNotificationStore } from '../stores/notificationStore'
import type { Paged } from '../types/issue'
import type { AppNotification } from '../types/notification'
import { apiError } from '../utils/errors'
import { usePageSize } from '../utils/paging'

type Filter = 'all' | 'unread'

/** Everything the person was told, newest first. Opening an entry marks it read and goes to its issue. */
export default function NotificationsPage() {
  const navigate = useNavigate()
  const unread = useNotificationStore((s) => s.unread)
  const [filter, setFilter] = useState<Filter>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = usePageSize()
  const [data, setData] = useState<Paged<AppNotification> | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    notificationService.list({ unreadOnly: filter === 'unread', page, pageSize })
      .then((result) => { setData(result); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดการแจ้งเตือนไม่สำเร็จ')))
  }, [filter, page, pageSize])

  // A change in the unread number (a new entry, or read elsewhere) updates the list in place; the tracker already follows the live events.
  useEffect(() => { load() }, [unread, load])

  const open = (item: AppNotification) => {
    if (!item.isRead) {
      setData((current) => current && { ...current, items: current.items.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)) })
      notificationService.markRead(item.id).then(refreshUnread).catch(() => {})
    }
    if (item.issueId) navigate(`/issues/${item.issueId}`)
  }

  const readAll = () => {
    setData((current) => current && { ...current, items: current.items.map((n) => ({ ...n, isRead: true })) })
    notificationService.markAllRead().then(refreshUnread).catch(() => refreshUnread())
  }

  const choose = (next: Filter) => { setFilter(next); setPage(1) }

  return (
    <Page>
      <PageHeader title="การแจ้งเตือน" subtitle="สิ่งที่ระบบแจ้งคุณ เช่น มีการตอบกลับ เปลี่ยนสถานะ หรือมอบหมายงาน"
        actions={<Button icon="checks" onClick={readAll} disabled={unread === 0}>ทำเครื่องหมายว่าอ่านทั้งหมด</Button>} />

      <div className="mb-4">
        <Segmented value={filter} onChange={choose} label="กรองการแจ้งเตือน"
          options={[{ value: 'all', label: 'ทั้งหมด' }, { value: 'unread', label: 'ยังไม่อ่าน', count: unread }]} />
      </div>

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      {!data ? (
        error ? null : <PageSkeleton />
      ) : (
        <Card className="overflow-hidden">
          {data.items.length === 0 ? (
            <EmptyState icon="bell-off" title={filter === 'unread' ? 'ไม่มีรายการที่ยังไม่อ่าน' : 'ยังไม่มีการแจ้งเตือน'}>
              {filter === 'unread' ? 'อ่านครบทุกรายการแล้ว' : 'เมื่อมีการตอบกลับหรือเปลี่ยนสถานะเรื่องของคุณ จะแสดงที่นี่'}
            </EmptyState>
          ) : (
            <ul className="divide-y divide-line">
              {data.items.map((item) => <li key={item.id}><NotificationItem item={item} onOpen={open} /></li>)}
            </ul>
          )}
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total}
            onChange={setPage} onPageSizeChange={(size) => { setPageSize(size); setPage(1) }} />
        </Card>
      )}
    </Page>
  )
}
