// Enum values mirror the API (it sends numbers and accepts numbers or names).

export const IssueStatus = {
  New: 0, Assigned: 1, InProgress: 2, WaitingForReporter: 3, Resolved: 4, Closed: 5, Rejected: 6, Reopened: 7, OnHold: 8,
} as const
export type IssueStatusValue = (typeof IssueStatus)[keyof typeof IssueStatus]

export const IssuePriority = { Low: 0, Medium: 1, High: 2, Critical: 3 } as const
export type IssuePriorityValue = (typeof IssuePriority)[keyof typeof IssuePriority]

export const IssueImpact = { Minor: 0, Degraded: 1, Blocking: 2 } as const
export type IssueImpactValue = (typeof IssueImpact)[keyof typeof IssueImpact]

export const IssueCategory = { Bug: 0, WrongData: 1, Slow: 2, Improvement: 3, HowTo: 4 } as const
export type IssueCategoryValue = (typeof IssueCategory)[keyof typeof IssueCategory]

export const IssueModule = {
  Application: 0, Login: 1, Dashboard: 2, Export: 3, Users: 4, Issues: 5, ItGeneral: 6, Other: 7,
} as const
export type IssueModuleValue = (typeof IssueModule)[keyof typeof IssueModule]

export const IssueRootCause = {
  None: 0, CodeBug: 1, DataIssue: 2, UserError: 3, Configuration: 4, Infrastructure: 5, CannotReproduce: 6, Other: 7,
} as const
export type IssueRootCauseValue = (typeof IssueRootCause)[keyof typeof IssueRootCause]

export type Tone = 'orange' | 'blue' | 'green' | 'amber' | 'red' | 'gray'

export type IssueActionName =
  | 'assign' | 'start' | 'requestInfo' | 'resolve' | 'confirm' | 'reopen' | 'reject' | 'hold' | 'resume'
  | 'setPriority' | 'comment' | 'internalNote' | 'attach'

export interface IssueListItem {
  id: string
  number: string
  title: string
  category: IssueCategoryValue
  module: IssueModuleValue
  impact: IssueImpactValue
  priority: IssuePriorityValue
  status: IssueStatusValue
  tone: Tone
  reporterName: string
  departmentId: string | null
  departmentName: string | null
  assigneeId: string | null
  assigneeName: string | null
  createdAt: string
  updatedAt: string
  responseDueAt: string
  resolveDueAt: string
  responseOverdue: boolean
  resolveOverdue: boolean
  commentCount: number
  attachmentCount: number
  isPublicSubmission: boolean
}

export interface IssueComment {
  id: string
  authorName: string
  isStaff: boolean
  isInternal: boolean
  body: string
  createdAt: string
}

export interface IssueAttachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  createdAt: string
}

export interface IssueEvent {
  action: string
  from: string | null
  to: string | null
  actorName: string | null
  actorRole: string | null
  at: string
}

export interface IssueDetail extends Omit<IssueListItem, 'commentCount' | 'attachmentCount'> {
  description: string
  reporterUserId: string | null
  reporterEmail: string | null
  reporterPhone: string | null
  firstResponseAt: string | null
  waitingSince: string | null
  resolvedAt: string | null
  closedAt: string | null
  reopenCount: number
  rootCause: IssueRootCauseValue
  resolution: string | null
  prevention: string | null
  fixReference: string | null
  timeSpentMinutes: number | null
  rejectReason: string | null
  duplicateOfId: string | null
  pageUrl: string | null
  userAgent: string | null
  clientInfo: string | null
  errorRequestId: string | null
  reporterIp: string | null
  comments: IssueComment[]
  attachments: IssueAttachment[]
  timeline: IssueEvent[]
  allowedActions: IssueActionName[]
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface IssueSummary {
  open: number
  new: number
  inProgress: number
  waitingForReporter: number
  overdue: number
  resolvedLast7Days: number
  mine: number
}

export interface Assignee {
  id: string
  name: string
  role: string
  openIssues: number
}

export interface IssueQuery {
  search?: string
  /** Also look inside the description (slower on a large system). */
  inDescription?: boolean
  status?: number
  openOnly?: boolean
  priority?: number
  module?: number
  assigneeId?: string
  mine?: boolean
  overdue?: boolean
  from?: string
  to?: string
  sort?: string
  page?: number
  pageSize?: number
}

export interface IssueFormValues {
  title: string
  description: string
  category: IssueCategoryValue
  module: IssueModuleValue
  impact: IssueImpactValue
}

export interface PublicReceipt {
  number: string
  createdAt: string
  responseDueAt: string
}
