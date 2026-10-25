import { serverKey } from './server'

/**
 * "Remember me" on the login page, per server address (the same email can be another account on another server).
 *
 *  - In the Windows app the email AND password are remembered, encrypted with the Windows account by the app itself
 *    (see desktop/credentials.cjs); the page never stores a password.
 *  - In a web browser only the email is remembered, in localStorage. The password is left to the browser's own
 *    password manager, which is safer than anything a web page can do; the login form is marked up so it offers to save.
 */

const EMAIL_KEY = 'helpdesk.rememberedEmail'

export interface Remembered {
  email: string
  /** only in the Windows app */
  password?: string
}

const desktopStore = () => window.desktop?.credentials

/** Whether this device can remember the password itself (the Windows app with OS encryption). */
export async function canRememberPassword(): Promise<boolean> {
  try { return (await desktopStore()?.available()) === true } catch { return false }
}

function readEmails(): Record<string, string> {
  try {
    const data = JSON.parse(localStorage.getItem(EMAIL_KEY) || '{}')
    return data && typeof data === 'object' && !Array.isArray(data) ? data : {}
  } catch { return {} }
}

function writeEmails(map: Record<string, string>) {
  try {
    if (Object.keys(map).length === 0) localStorage.removeItem(EMAIL_KEY)
    else localStorage.setItem(EMAIL_KEY, JSON.stringify(map))
  } catch { /* not remembered, sign-in still works */ }
}

export async function loadRemembered(server = serverKey()): Promise<Remembered | null> {
  if (await canRememberPassword()) {
    try {
      const saved = await desktopStore()!.get(server)
      if (saved) return saved
    } catch { /* fall through to the email */ }
  }
  const email = readEmails()[server]
  return typeof email === 'string' && email ? { email } : null
}

/** Called after a successful sign-in with "remember me" ticked. */
export async function saveRemembered(email: string, password: string, server = serverKey()): Promise<void> {
  if (await canRememberPassword()) {
    try {
      if (await desktopStore()!.save(server, email, password)) return
    } catch { /* keep at least the email */ }
  }
  writeEmails({ ...readEmails(), [server]: email })
}

/** Forgets everything remembered for this server on this device. */
export async function forgetRemembered(server = serverKey()): Promise<void> {
  const { [server]: _gone, ...rest } = readEmails()
  void _gone
  writeEmails(rest)
  try { await desktopStore()?.forget(server) } catch { /* nothing more to do */ }
}

/** After a password change: keep the remembered password current, but only if it belongs to this account. */
export async function updateRememberedPassword(email: string, password: string, server = serverKey()): Promise<void> {
  try { await desktopStore()?.updatePassword(server, email, password) } catch { /* the old one just fails once at the next sign-in */ }
}
