import type { IssueStatusValue, Tone } from './issue'

export type NotificationLevel = 'info' | 'success' | 'warning' | 'error'

/** One entry of the notification centre: what a toast said, kept so it can be read later. */
export interface AppNotification {
  id: string
  issueId: string | null
  kind: string
  title: string
  message: string
  level: NotificationLevel
  createdAt: string
  isRead: boolean
}

export interface IssueHit {
  id: string
  number: string
  title: string
  status: IssueStatusValue
  tone: Tone
  reporterName: string
}

export interface UserHit {
  id: string
  name: string
  email: string
  department: string | null
}

export interface QuickSearchResult {
  issues: IssueHit[]
  /** Empty unless the person may open the user list. */
  users: UserHit[]
}
