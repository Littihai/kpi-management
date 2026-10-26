import { useRealtimeStatus } from './realtimeContext'

const statusText = {
  connected: { label: 'เชื่อมต่อแล้ว', dot: 'bg-green-dot' },
  connecting: { label: 'กำลังเชื่อมต่อ…', dot: 'bg-amber-dot' },
  reconnecting: { label: 'กำลังเชื่อมต่อใหม่…', dot: 'bg-amber-dot' },
  offline: { label: 'ไม่ได้เชื่อมต่อ', dot: 'bg-ink-4' },
} as const

/** Whether live updates are flowing, as a single dot. The words are only for screen readers and the hover hint. */
export default function ConnectionStatus({ className = '' }: { className?: string }) {
  const connection = statusText[useRealtimeStatus()]
  return (
    <p className={`flex items-center ${className}`} role="status" aria-label={connection.label} title={connection.label}>
      <span className={`h-2 w-2 rounded-full ${connection.dot}`} aria-hidden="true" />
    </p>
  )
}
