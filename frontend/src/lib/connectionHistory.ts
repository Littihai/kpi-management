import { useMemo, useSyncExternalStore } from 'react'

/**
 * The SQL Servers this device connected to before (the "History" tab of the connect window, as in SSMS). Only where and
 * who: server, database, how to sign in, the user name, the certificate choice. NEVER a password; the person types it
 * again, the way the window asks for it the first time.
 */
export interface ConnectionEntry {
  server: string
  database: string
  auth: 'sql' | 'windows'
  user?: string
  trust: boolean
  /** when it was last used (ms) */
  at: number
}

const KEY = 'helpdesk.connectionHistory'
const MAX = 10
const CHANGED = 'helpdesk:connection-history'

const same = (a: Pick<ConnectionEntry, 'server' | 'database' | 'auth' | 'user'>, b: Pick<ConnectionEntry, 'server' | 'database' | 'auth' | 'user'>) =>
  a.server.toLowerCase() === b.server.toLowerCase() && a.database.toLowerCase() === b.database.toLowerCase()
  && a.auth === b.auth && (a.user ?? '').toLowerCase() === (b.user ?? '').toLowerCase()

function parse(raw: string | null): ConnectionEntry[] {
  try {
    const data: unknown = JSON.parse(raw || '[]')
    if (!Array.isArray(data)) return []
    return data.flatMap((e): ConnectionEntry[] => {
      if (!e || typeof e !== 'object') return []
      const { server, database, auth, user, trust, at } = e as Record<string, unknown>
      if (typeof server !== 'string' || !server || typeof database !== 'string' || (auth !== 'sql' && auth !== 'windows')) return []
      return [{ server, database, auth, user: typeof user === 'string' && user ? user : undefined, trust: trust !== false, at: typeof at === 'number' ? at : 0 }]
    }).slice(0, MAX)
  } catch { return [] }
}

function read(): string | null {
  try { return localStorage.getItem(KEY) } catch { return null }
}

function write(list: ConnectionEntry[]) {
  try {
    if (list.length === 0) localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, JSON.stringify(list))
  } catch { /* not remembered, connecting still works */ }
  window.dispatchEvent(new Event(CHANGED))
}

export const loadHistory = (): ConnectionEntry[] => parse(read())

/** Called after a connection worked: puts it first, once. */
export function rememberConnection(entry: Omit<ConnectionEntry, 'at'>) {
  const clean = { ...entry, user: entry.auth === 'sql' ? entry.user : undefined }
  write([{ ...clean, at: Date.now() }, ...loadHistory().filter((e) => !same(e, clean))].slice(0, MAX))
}

export function forgetConnection(entry: ConnectionEntry) {
  write(loadHistory().filter((e) => !same(e, entry)))
}

export function clearHistory() {
  write([])
}

// The raw text is the snapshot (a string compares by value), so React re-renders only when the stored history really changed.
function subscribe(onChange: () => void) {
  window.addEventListener(CHANGED, onChange)
  window.addEventListener('storage', onChange)
  return () => { window.removeEventListener(CHANGED, onChange); window.removeEventListener('storage', onChange) }
}

/** The history, kept up to date while the page is open (also when another tab changes it). */
export function useConnectionHistory(): ConnectionEntry[] {
  const raw = useSyncExternalStore(subscribe, read, () => null)
  return useMemo(() => parse(raw), [raw])
}

/** How the entry reads in a list: who signs in, to which database. */
export function describeEntry(e: ConnectionEntry): string {
  return `${e.database} · ${e.auth === 'windows' ? 'บัญชี Windows' : e.user || 'SQL'}`
}
