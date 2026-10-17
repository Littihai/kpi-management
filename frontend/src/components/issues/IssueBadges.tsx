import Badge from '../ui/Badge'
import { formatCountdown } from '../../utils/format'
import { clockRuns } from '../../utils/issueStatus'
import { issueStatusLabel, issueStatusTone, priorityLabel, priorityTone } from '../../utils/labels'
import { IssueStatus } from '../../types/issue'
import type { IssueListItem } from '../../types/issue'

export function StatusBadge({ status }: { status: number }) {
  return <Badge tone={issueStatusTone[status] ?? 'gray'} dot>{issueStatusLabel[status] ?? status}</Badge>
}

export function PriorityBadge({ priority }: { priority: number }) {
  const icon = priority >= 3 ? 'flame' : priority === 2 ? 'arrow-up' : undefined
  return <Badge tone={priorityTone[priority] ?? 'gray'} icon={icon}>{priorityLabel[priority] ?? priority}</Badge>
}

type DueSource = Pick<IssueListItem,
  'status' | 'responseDueAt' | 'resolveDueAt' | 'responseOverdue' | 'resolveOverdue'> & { firstResponseAt?: string | null }

/** The deadline that matters right now: reply first, then fix. Finished issues show nothing. */
export function DueLabel({ issue, now }: { issue: DueSource; now?: Date }) {
  if (issue.status === IssueStatus.WaitingForReporter) {
    return <span className="text-[13px] text-amber-ink">หยุดนับ · รอผู้แจ้ง</span>
  }
  if (!clockRuns(issue.status)) return <span className="text-[13px] text-ink-3">–</span>

  const needsFirstResponse = issue.status === IssueStatus.New
    || (issue.status === IssueStatus.Assigned && issue.responseOverdue)
  const target = needsFirstResponse ? issue.responseDueAt : issue.resolveDueAt
  const { text, late } = formatCountdown(target, now)

  return (
    <span className={`inline-flex items-center gap-1 text-[13px] tabular ${late ? 'font-medium text-red-ink' : 'text-ink-2'}`}>
      <i className={`ti ti-${late ? 'alarm' : 'clock'} text-sm`} aria-hidden="true" />
      <span className="sr-only">{needsFirstResponse ? 'กำหนดตอบรับ: ' : 'กำหนดแก้ไข: '}</span>
      {text}
    </span>
  )
}
