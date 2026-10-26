import { badgeText } from '../lib/attention'
import { useAttentionStore } from '../stores/attentionStore'

/** Small round marker, as high as the pill so every state lines up. */
const marker = 'flex h-4 w-4 items-center justify-center rounded-full'

/**
 * The Teams-style status: a green check when nothing waits for the person, a red number when something does, an amber dot
 * while connecting and a grey one when not connected. The words are only for screen readers and the hover hint.
 */
export default function ConnectionStatus({ className = '' }: { className?: string }) {
  const { state, count, tooltip } = useAttentionStore()
  return (
    <p className={`flex items-center ${className}`} role="status" aria-label={tooltip} title={tooltip}>
      {state === 'attention' ? (
        <span className="tabular flex h-4 min-w-4 items-center justify-center rounded-full bg-red-dot px-1 text-[10.5px] font-bold leading-none text-white" aria-hidden="true">
          {badgeText(count)}
        </span>
      ) : state === 'ok' ? (
        <span className={`${marker} bg-green-dot text-white`} aria-hidden="true">
          <i className="ti ti-check text-xs" />
        </span>
      ) : (
        <span className={marker} aria-hidden="true">
          <span className={`h-2 w-2 rounded-full ${state === 'connecting' ? 'bg-amber-dot' : 'bg-ink-4'}`} />
        </span>
      )}
    </p>
  )
}
