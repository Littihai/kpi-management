import { useRealtimeStatus } from './realtimeContext'

const statusText = {
  connected: { label: 'เชื่อมต่อแบบสด', dot: 'bg-green-dot' },
  connecting: { label: 'กำลังเชื่อมต่อ…', dot: 'bg-amber-dot' },
  reconnecting: { label: 'กำลังเชื่อมต่อใหม่…', dot: 'bg-amber-dot' },
  offline: { label: 'ไม่ได้เชื่อมต่อแบบสด', dot: 'bg-ink-4' },
} as const

/** Whether live updates are flowing: a dot plus words, so the colour is never the only signal. */
export default function ConnectionStatus({ className = '' }: { className?: string }) {
  const connection = statusText[useRealtimeStatus()]
  return (
    <p className={`flex items-center gap-2 text-xs text-ink-3 ${className}`} role="status">
      <span className={`h-1.5 w-1.5 rounded-full ${connection.dot}`} aria-hidden="true" />
      {connection.label}
    </p>
  )
}
