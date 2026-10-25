import { isUnencrypted, normalizeServerUrl, sameServer, type ServerUrlResult } from './serverUrl'

export { isUnencrypted, normalizeServerUrl, sameServer, type ServerUrlResult }

const KEY = 'helpdesk.serverUrl'

/**
 * Which server the app talks to. In order:
 *  1. the address chosen on this device (login page / Settings > About);
 *  2. the address the company's IT put on this computer for the Windows app (HELPDESK_SERVER_URL, or the first line of
 *     %ProgramData%\Helpdesk\server.txt);
 *  3. VITE_API_URL given when the web app was built (an empty value means "the page's own address");
 *  4. the local API in development;
 *  5. otherwise nothing is built in: a web page belongs to the server that serves it (its own address), and the Windows app,
 *     which has no server of its own, asks the person to choose one (null). No company's server is the default.
 */
export function builtInServerUrl(): string | null {
  const fromIt = typeof window !== 'undefined' ? window.desktop?.defaultServerUrl : undefined
  if (fromIt) return fromIt
  const built = import.meta.env.VITE_API_URL as string | undefined
  if (built !== undefined) return built
  if (import.meta.env.DEV) return 'http://localhost:5079'
  return typeof window !== 'undefined' && window.desktop ? null : ''
}

export function getServerOverride(): string | null {
  try {
    const saved = localStorage.getItem(KEY)
    if (!saved) return null
    const r = normalizeServerUrl(saved)
    return r.ok ? r.url : null
  } catch {
    return null
  }
}

/** The address in use right now; null while none has been chosen (the Windows app before the person picks one). */
export function currentServerUrl(): string | null {
  return getServerOverride() ?? builtInServerUrl()
}

/** Has a server been chosen (or does the page have its own)? Without one the app only asks for it. */
export const hasServer = (): boolean => currentServerUrl() !== null

/** The address as a key for things kept per server ("" while none is chosen). */
export const serverKey = (): string => currentServerUrl() ?? ''

/** What to show a person for an address ("" means the page's own address). */
export function describeServer(url: string | null): string {
  return url === null ? 'ยังไม่ได้เลือก' : url === '' ? window.location.origin : url
}

/**
 * Whether this build lets people pick a server. The Windows app does (every company has its own); a web page belongs to
 * the server that serves it, so it does not unless it was built with VITE_ALLOW_SERVER_CHOICE=true.
 */
export function serverChoiceAllowed(): boolean {
  return typeof window !== 'undefined' && (!!window.desktop || import.meta.env.DEV || import.meta.env.VITE_ALLOW_SERVER_CHOICE === 'true')
}

/**
 * Uses another server from now on (null = go back to the built-in one, if there is one). The sign-in belongs to the old server, so it is
 * dropped, and the page is reloaded because the API client and the live connection read the address once, when they start.
 */
export function switchServer(url: string | null): void {
  try {
    const builtIn = builtInServerUrl()
    if (url === null || (builtIn !== null && sameServer(url, builtIn))) localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, url)
    localStorage.removeItem('token')
    localStorage.removeItem('user')
  } catch { /* storage unavailable: the reload below still returns to the sign-in page */ }
  window.location.href = '/login'
}

export type ProbeResult =
  | { ok: true; ms: number }
  | { ok: false; reason: 'timeout' | 'unreachable' | 'status' | 'not-helpdesk'; status?: number }

/**
 * Asks an address whether it is a Helpdesk server: /api/client-config is public and answers with the minimum app version.
 * A browser cannot tell "nobody there" from "the server does not allow this app" (CORS); both come back as unreachable.
 */
export async function probeServer(url: string, timeoutMs = 20000): Promise<ProbeResult> {
  const controller = new AbortController()
  const timer = window.setTimeout(() => controller.abort(), timeoutMs)
  const started = performance.now()
  try {
    const res = await fetch(url + '/api/client-config', { signal: controller.signal, cache: 'no-store' })
    if (!res.ok) return { ok: false, reason: 'status', status: res.status }
    const body: unknown = await res.json().catch(() => null)
    if (!body || typeof (body as { minDesktopVersion?: unknown }).minDesktopVersion !== 'string') return { ok: false, reason: 'not-helpdesk' }
    return { ok: true, ms: Math.round(performance.now() - started) }
  } catch (err) {
    return { ok: false, reason: (err as { name?: string }).name === 'AbortError' ? 'timeout' : 'unreachable' }
  } finally {
    window.clearTimeout(timer)
  }
}
