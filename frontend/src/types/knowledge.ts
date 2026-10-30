export interface ArticleListItem {
  id: string
  title: string
  summary: string
  category: string
  isPublished: boolean
  publishedAt: string | null
  updatedAt: string
  viewCount: number
}

export interface Article {
  id: string
  title: string
  summary: string
  body: string
  category: string
  isPublished: boolean
  publishedAt: string | null
  authorName: string
  createdAt: string
  updatedAt: string
  /** The number the editor must send back when saving; a different one means somebody else saved first. */
  version: number
  viewCount: number
}

export interface SuggestedArticle {
  id: string
  title: string
  summary: string
  category: string
}

export interface ArticleQuery {
  search?: string
  category?: string
  inBody?: boolean
  /** Managers only: true for published, false for drafts, left out for both. */
  published?: boolean
  page?: number
  pageSize?: number
}

export interface ArticleInput {
  title: string
  summary: string
  body: string
  category: string
}

/** Limits the server enforces, repeated here so the form can say so before sending. */
export const ARTICLE_LIMITS = { title: 200, summary: 500, body: 20000, category: 100 } as const
