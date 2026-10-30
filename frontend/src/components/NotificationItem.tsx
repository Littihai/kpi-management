import { levelOf } from '../lib/notifyLevel'
import { formatRelative } from '../utils/format'
import type { AppNotification } from '../types/notification'

/** One entry of the notification centre, used by the bell and by the full list. Unread entries are marked by a dot and a heavier title. */
export default function NotificationItem({ item, onOpen }: { item: AppNotification; onOpen: (item: AppNotification) => void }) {
  const style = levelOf(item.level)
  return (
    <button type="button" onClick={() => onOpen(item)}
      className={`relative flex w-full gap-3 px-4 py-3 pl-5 text-left transition-colors hover:bg-surface ${item.isRead ? '' : 'bg-accent-soft/40'}`}>
      <span className={`absolute inset-y-0 left-0 w-1 ${style.bar}`} aria-hidden="true" />
      <i className={`ti ti-${style.icon} mt-0.5 text-lg text-ink-2`} aria-hidden="true" />
      <span className="min-w-0 flex-1">
        <span className={`block text-sm text-ink ${item.isRead ? 'font-medium' : 'font-semibold'}`}>
          {!item.isRead && <span className="sr-only">ยังไม่อ่าน: </span>}
          {item.title}
        </span>
        <span className="mt-0.5 block break-words text-[13px] text-ink-2">{item.message}</span>
        <span className="mt-1 block text-xs text-ink-3">{formatRelative(item.createdAt)}</span>
      </span>
      {!item.isRead && <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-accent" aria-hidden="true" />}
    </button>
  )
}
