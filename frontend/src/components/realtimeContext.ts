import { createContext, useContext, useEffect, useRef } from 'react'
import type { KpiChangedEvent } from '../services/realtime'

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline'
export type KpiHandler = (event: KpiChangedEvent) => void

export interface RealtimeContextValue {
  status: RealtimeStatus
  subscribe: (handler: KpiHandler) => () => void
}

export const RealtimeContext = createContext<RealtimeContextValue>({
  status: 'offline',
  subscribe: () => () => {},
})

export function useRealtimeStatus(): RealtimeStatus {
  return useContext(RealtimeContext).status
}

/** Run `handler` whenever the server reports that a KPI the user can see has changed. */
export function useKpiEvents(handler: KpiHandler) {
  const { subscribe } = useContext(RealtimeContext)
  const latest = useRef(handler)
  useEffect(() => { latest.current = handler })

  useEffect(() => subscribe((event) => latest.current(event)), [subscribe])
}
