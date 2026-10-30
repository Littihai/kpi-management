import type { IssueQuery } from '../types/issue'

export type IssueViewKey = 'pending' | 'assigned' | 'near'

export interface IssueView {
  key: IssueViewKey
  label: string
  icon: string
  /** Only people who work the IT queue get it (the server decides what is in the list either way). */
  itOnly: boolean
  /** What the view asks the server for. A view replaces the open/done tab: it is defined by its rule, not by a status. */
  query: Pick<IssueQuery, 'attention' | 'sla'>
  /** The order the view is most useful in, until the person picks another. */
  sort?: string
  /** The menu entry that opens it. */
  to: string
}

export const issueViews: IssueView[] = [
  { key: 'pending', label: 'รอดำเนินการ', icon: 'inbox', itOnly: false, query: { attention: 'Any' }, to: '/issues?view=pending' },
  { key: 'assigned', label: 'มอบหมายให้ฉัน', icon: 'user-check', itOnly: true, query: { attention: 'AssignedToMe' }, sort: 'updated', to: '/issues?view=assigned' },
  { key: 'near', label: 'ใกล้เกิน SLA', icon: 'alarm', itOnly: true, query: { sla: 'Near' }, sort: 'due', to: '/issues?view=near' },
]

/** The view named by `?view=`, or null for an unknown name or one the person may not use. */
export function findIssueView(key: string | null, isIt: boolean): IssueView | null {
  const view = issueViews.find((v) => v.key === key)
  return view && (isIt || !view.itOnly) ? view : null
}
