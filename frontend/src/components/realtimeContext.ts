import { createContext, useContext, useEffect, useRef } from 'react'
import type { IssueChangedEvent, KpiChangedEvent } from '../services/realtime'

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline'
export type KpiHandler = (event: KpiChangedEvent) => void
export type IssueHandler = (event: IssueChangedEvent) => void

export interface RealtimeContextValue {
  status: RealtimeStatus
  subscribe: (handler: KpiHandler) => () => void
  subscribeIssues: (handler: IssueHandler) => () => void
}

export const RealtimeContext = createContext<RealtimeContextValue>({
  status: 'offline',
  subscribe: () => () => {},
  subscribeIssues: () => () => {},
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

/** Run `handler` whenever an issue the user can see changes (new report, status change, reply). */
export function useIssueEvents(handler: IssueHandler) {
  const { subscribeIssues } = useContext(RealtimeContext)
  const latest = useRef(handler)
  useEffect(() => { latest.current = handler })

  useEffect(() => subscribeIssues((event) => latest.current(event)), [subscribeIssues])
}
