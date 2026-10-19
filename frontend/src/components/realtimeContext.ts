import { createContext, useContext, useEffect, useRef } from 'react'
import type { IssueChangedEvent } from '../services/realtime'

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline'
export type IssueHandler = (event: IssueChangedEvent) => void

export interface RealtimeContextValue {
  status: RealtimeStatus
  subscribeIssues: (handler: IssueHandler) => () => void
}

export const RealtimeContext = createContext<RealtimeContextValue>({
  status: 'offline',
  subscribeIssues: () => () => {},
})

export function useRealtimeStatus(): RealtimeStatus {
  return useContext(RealtimeContext).status
}

/** Run `handler` whenever an issue the user can see changes (new report, status change, reply). */
export function useIssueEvents(handler: IssueHandler) {
  const { subscribeIssues } = useContext(RealtimeContext)
  const latest = useRef(handler)
  useEffect(() => { latest.current = handler })

  useEffect(() => subscribeIssues((event) => latest.current(event)), [subscribeIssues])
}
