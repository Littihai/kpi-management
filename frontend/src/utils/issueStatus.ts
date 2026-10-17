import { IssueStatus } from '../types/issue'

/** Resolved, closed or rejected: nothing more for IT to do (mirrors the API's IssueStateMachine). */
export function isFinished(status: number): boolean {
  return status === IssueStatus.Resolved || status === IssueStatus.Closed || status === IssueStatus.Rejected
}

/** The resolve clock runs only while the next move is IT's, not while waiting for the reporter. */
export function clockRuns(status: number): boolean {
  return status === IssueStatus.New || status === IssueStatus.Assigned
    || status === IssueStatus.InProgress || status === IssueStatus.Reopened
}
