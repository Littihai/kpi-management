import api from './api'
import type { Paged } from '../types/issue'
import type { Article, ArticleInput, ArticleListItem, ArticleQuery, SuggestedArticle } from '../types/knowledge'

export const knowledgeService = {
  async list(query: ArticleQuery): Promise<Paged<ArticleListItem>> {
    return (await api.get<Paged<ArticleListItem>>('/api/knowledge', { params: query })).data
  },

  async categories(): Promise<string[]> {
    return (await api.get<string[]>('/api/knowledge/categories')).data
  },

  /** Published articles that look related to a title being typed; nothing for a very short title. */
  async suggest(title: string, signal?: AbortSignal): Promise<SuggestedArticle[]> {
    return (await api.get<SuggestedArticle[]>('/api/knowledge/suggest', { params: { title }, signal })).data
  },

  /** `countView` is false for the editor: loading an article to change it is not a reader opening it. */
  async get(id: string, countView = true): Promise<Article> {
    return (await api.get<Article>(`/api/knowledge/${id}`, { params: { countView } })).data
  },

  async create(input: ArticleInput): Promise<Article> {
    return (await api.post<Article>('/api/knowledge', input)).data
  },

  async update(id: string, input: ArticleInput, version: number): Promise<Article> {
    return (await api.put<Article>(`/api/knowledge/${id}`, { ...input, version })).data
  },

  async publish(id: string): Promise<Article> {
    return (await api.post<Article>(`/api/knowledge/${id}/publish`)).data
  },

  async unpublish(id: string): Promise<Article> {
    return (await api.post<Article>(`/api/knowledge/${id}/unpublish`)).data
  },

  async remove(id: string): Promise<void> {
    await api.delete(`/api/knowledge/${id}`)
  },
}
