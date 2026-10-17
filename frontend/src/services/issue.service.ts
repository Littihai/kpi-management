import api from './api'
import type {
  Assignee, IssueDetail, IssueFormValues, IssueListItem, IssueQuery, IssueSummary, Paged, PublicReceipt,
} from '../types/issue'

function toForm(values: IssueFormValues, files: File[], extra: Record<string, string | undefined> = {}) {
  const form = new FormData()
  form.append('Title', values.title)
  form.append('Description', values.description)
  form.append('Category', String(values.category))
  form.append('Module', String(values.module))
  form.append('Impact', String(values.impact))
  for (const [key, value] of Object.entries(extra)) if (value) form.append(key, value)
  files.forEach((file) => form.append('Files', file))
  return form
}

/** What the browser can tell us for free, so the reporter does not have to describe their setup. */
export function clientInfo(): string {
  const { innerWidth, innerHeight } = window
  const nav = navigator as Navigator & { userAgentData?: { platform?: string } }
  return [
    nav.userAgentData?.platform ?? navigator.platform,
    `จอ ${innerWidth}×${innerHeight}`,
    navigator.language,
  ].filter(Boolean).join(' · ')
}

export const issueService = {
  async list(query: IssueQuery): Promise<Paged<IssueListItem>> {
    const res = await api.get<Paged<IssueListItem>>('/api/issues', { params: query })
    return res.data
  },

  async summary(): Promise<IssueSummary> {
    return (await api.get<IssueSummary>('/api/issues/summary')).data
  },

  async get(id: string): Promise<IssueDetail> {
    return (await api.get<IssueDetail>(`/api/issues/${id}`)).data
  },

  async create(values: IssueFormValues, files: File[], pageUrl?: string): Promise<IssueDetail> {
    const form = toForm(values, files, { PageUrl: pageUrl, ClientInfo: clientInfo() })
    return (await api.post<IssueDetail>('/api/issues', form)).data
  },

  async createPublic(
    values: IssueFormValues,
    contact: { name: string; email: string; phone: string; departmentId: string },
    files: File[],
    honeypot: string,
  ): Promise<PublicReceipt> {
    const form = toForm(values, files, {
      ReporterName: contact.name,
      ReporterEmail: contact.email,
      ReporterPhone: contact.phone,
      DepartmentId: contact.departmentId,
      ClientInfo: clientInfo(),
      Website: honeypot,
    })
    return (await api.post<PublicReceipt>('/api/public/issues', form)).data
  },

  async publicDepartments(): Promise<{ id: string; name: string }[]> {
    return (await api.get('/api/public/departments')).data
  },

  async comment(id: string, body: string, internalNote: boolean): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/comments`, { body, internalNote })).data
  },

  async assign(id: string, assigneeId: string): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/assign`, { assigneeId })).data
  },

  async start(id: string): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/start`)).data
  },

  async requestInfo(id: string, message: string): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/request-info`, { message })).data
  },

  async resolve(id: string, body: {
    resolution: string; rootCause: number; prevention?: string; fixReference?: string; timeSpentMinutes?: number | null
  }): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/resolve`, body)).data
  },

  async confirm(id: string): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/confirm`)).data
  },

  async reopen(id: string, reason: string): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/reopen`, { reason })).data
  },

  async reject(id: string, reason: string, duplicateOfId?: string): Promise<IssueDetail> {
    return (await api.post<IssueDetail>(`/api/issues/${id}/reject`, { reason, duplicateOfId: duplicateOfId || null })).data
  },

  async setPriority(id: string, priority: number): Promise<IssueDetail> {
    return (await api.put<IssueDetail>(`/api/issues/${id}/priority`, { priority })).data
  },

  async attach(id: string, files: File[]): Promise<IssueDetail> {
    const form = new FormData()
    files.forEach((file) => form.append('files', file))
    return (await api.post<IssueDetail>(`/api/issues/${id}/attachments`, form)).data
  },

  async assignees(): Promise<Assignee[]> {
    return (await api.get<Assignee[]>('/api/issues/assignees')).data
  },

  /** Images need the sign-in token, so they are fetched as blobs rather than used as plain <img src>. */
  async attachmentBlob(issueId: string, attachmentId: string): Promise<Blob> {
    const res = await api.get(`/api/issues/${issueId}/attachments/${attachmentId}`, { responseType: 'blob' })
    return res.data as Blob
  },
}
