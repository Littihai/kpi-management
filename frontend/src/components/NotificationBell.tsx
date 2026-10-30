import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { notificationService } from '../services/notification.service'
import { useAuthStore } from '../stores/authStore'
import { refreshUnread, useNotificationStore } from '../stores/notificationStore'
import type { AppNotification } from '../types/notification'
import NotificationItem from './NotificationItem'
import { unreadText } from './useNavGroups'

const PREVIEW = 8

/** The bell in the top bar: the unread number, and the latest entries in a panel. Must sit under the router. */
export default function NotificationBell({ className }: { className: string }) {
  const navigate = useNavigate()
  const canView = useAuthStore((s) => s.user?.permissions?.includes('issue.view')) ?? false
  const unread = useNotificationStore((s) => s.unread)
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<AppNotification[]>([])
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)

  const load = useCallback(() => {
    notificationService.list({ pageSize: PREVIEW })
      .then((page) => { setItems(page.items); setFailed(false) })
      .catch(() => setFailed(true))
      .finally(() => setLoading(false))
  }, [])

  // Opening shows what is there now; a new notification while it is open (the count moves) refreshes it.
  useEffect(() => {
    if (open) load()
  }, [open, unread, load])

  useEffect(() => {
    if (!open) return
    const onPointer = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false) }
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      setOpen(false)
      button.current?.focus()
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!canView) return null

  const toggle = () => {
    if (!open) { setLoading(true); setFailed(false) }
    setOpen(!open)
  }

  const openItem = (item: AppNotification) => {
    setOpen(false)
    if (!item.isRead) {
      setItems((current) => current.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)))
      notificationService.markRead(item.id).then(refreshUnread).catch(() => {})
    }
    if (item.issueId) navigate(`/issues/${item.issueId}`)
  }

  const readAll = () => {
    setItems((current) => current.map((n) => ({ ...n, isRead: true })))
    notificationService.markAllRead().then(refreshUnread).catch(() => refreshUnread())
  }

  return (
    <div ref={box} className="relative">
      <button ref={button} type="button" className={`relative ${className}`} onClick={toggle}
        aria-label={unread > 0 ? `การแจ้งเตือน ยังไม่อ่าน ${unreadText(unread)} รายการ` : 'การแจ้งเตือน'}
        aria-haspopup="dialog" aria-expanded={open} title="การแจ้งเตือน">
        <i className="ti ti-bell text-lg" aria-hidden="true" />
        {unread > 0 && (
          <span className="tabular absolute right-0 top-0 min-w-4 rounded-full bg-red-dot px-1 text-[10px] font-bold leading-4 text-white" aria-hidden="true">
            {unreadText(unread)}
          </span>
        )}
      </button>

      {open && (
        <div role="dialog" aria-label="การแจ้งเตือน"
          className="fixed inset-x-3 top-[calc(var(--toolbar-h)+3.5rem)] z-50 overflow-hidden rounded-card border border-line bg-card shadow-pop sm:absolute sm:inset-x-auto sm:right-0 sm:top-full sm:mt-2 sm:w-[24rem]">
          <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
            <h2 className="text-sm font-semibold text-ink">การแจ้งเตือน</h2>
            <button type="button" onClick={readAll} disabled={unread === 0}
              className="text-[13px] font-medium text-accent hover:underline disabled:text-ink-3 disabled:no-underline">
              ทำเครื่องหมายว่าอ่านทั้งหมด
            </button>
          </div>
          <div className="max-h-[min(26rem,60vh)] overflow-y-auto">
            {loading && items.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-ink-3" role="status">กำลังโหลด…</p>
            ) : failed && items.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-ink-3" role="alert">โหลดการแจ้งเตือนไม่สำเร็จ</p>
            ) : items.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-ink-3">ยังไม่มีการแจ้งเตือน</p>
            ) : (
              <ul className="divide-y divide-line">
                {items.map((item) => <li key={item.id}><NotificationItem item={item} onOpen={openItem} /></li>)}
              </ul>
            )}
          </div>
          <Link to="/notifications" onClick={() => setOpen(false)}
            className="block border-t border-line px-4 py-2.5 text-center text-[13px] font-medium text-accent hover:bg-surface">
            ดูการแจ้งเตือนทั้งหมด
          </Link>
        </div>
      )}
    </div>
  )
}
