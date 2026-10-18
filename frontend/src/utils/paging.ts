import { useState } from 'react'

/** The row counts a person can pick for a long list. */
export const PAGE_SIZES = [10, 20, 30, 100, 200] as const
export const DEFAULT_PAGE_SIZE = 20

const STORAGE_KEY = 'pageSize'

function readSavedSize(): number {
  try {
    const saved = Number(localStorage.getItem(STORAGE_KEY))
    return (PAGE_SIZES as readonly number[]).includes(saved) ? saved : DEFAULT_PAGE_SIZE
  } catch {
    return DEFAULT_PAGE_SIZE
  }
}

/** Rows per page, remembered in this browser and shared by every list. */
export function usePageSize(): [number, (size: number) => void] {
  const [size, setSize] = useState(readSavedSize)

  const change = (next: number) => {
    setSize(next)
    try {
      localStorage.setItem(STORAGE_KEY, String(next))
    } catch {
      // Storage can be blocked (private window): the choice then lasts for this visit only.
    }
  }
  return [size, change]
}

/**
 * Pages a list that is already in memory. The page goes back to 1 whenever `resetKey` changes
 * (a new search or filter), and is pulled back in range if the list shrinks.
 */
export function usePaging<T>(items: T[], resetKey: string) {
  const [pageSize, setPageSize] = usePageSize()
  const [requested, setRequested] = useState({ page: 1, key: resetKey })

  const pages = Math.max(1, Math.ceil(items.length / pageSize))
  const page = Math.min(requested.key === resetKey ? requested.page : 1, pages)

  return {
    page,
    pageSize,
    total: items.length,
    pageItems: items.slice((page - 1) * pageSize, page * pageSize),
    setPage: (next: number) => setRequested({ page: next, key: resetKey }),
    setPageSize: (size: number) => {
      setPageSize(size)
      setRequested({ page: 1, key: resetKey })
    },
  }
}
