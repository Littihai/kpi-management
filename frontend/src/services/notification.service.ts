import api from './api'
import type { AppNotification, QuickSearchResult } from '../types/notification'
import type { Paged } from '../types/issue'

export const notificationService = {
  async list(params: { unreadOnly?: boolean; page?: number; pageSize?: number } = {}): Promise<Paged<AppNotification>> {
    return (await api.get<Paged<AppNotification>>('/api/notifications', { params })).data
  },

  /** Counted up to 100 by the server. */
  async unreadCount(): Promise<number> {
    return (await api.get<{ count: number }>('/api/notifications/unread-count')).data.count
  },

  async markRead(id: string): Promise<void> {
    await api.post(`/api/notifications/${id}/read`)
  },

  async markAllRead(): Promise<number> {
    return (await api.post<{ marked: number }>('/api/notifications/read-all')).data.marked
  },

  /** Marks the entries about one issue read; resolves to how many there were. */
  async markIssueRead(issueId: string): Promise<number> {
    return (await api.post<{ marked: number }>(`/api/notifications/read-issue/${issueId}`)).data.marked
  },
}

export const searchService = {
  async quick(q: string, signal?: AbortSignal): Promise<QuickSearchResult> {
    return (await api.get<QuickSearchResult>('/api/search', { params: { q }, signal })).data
  },
}
