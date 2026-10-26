import { createContext, useContext, useEffect, useRef } from 'react'
import type { IssueChangedEvent, RealtimeNotification } from '../services/realtime'

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline'
export type IssueHandler = (event: IssueChangedEvent) => void
export type NotificationHandler = (notification: RealtimeNotification) => void

export interface RealtimeContextValue {
  status: RealtimeStatus
  subscribeIssues: (handler: IssueHandler) => () => void
  subscribeNotifications: (handler: NotificationHandler) => () => void
}

export const RealtimeContext = createContext<RealtimeContextValue>({
  status: 'offline',
  subscribeIssues: () => () => {},
  subscribeNotifications: () => () => {},
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

/** Run `handler` whenever a message addressed to this person arrives (the same ones shown as toasts). */
export function useNotificationEvents(handler: NotificationHandler) {
  const { subscribeNotifications } = useContext(RealtimeContext)
  const latest = useRef(handler)
  useEffect(() => { latest.current = handler })

  useEffect(() => subscribeNotifications((notification) => latest.current(notification)), [subscribeNotifications])
}
