export type ServerUrlResult = { ok: true; url: string } | { ok: false; error: string }

/**
 * Turns what a person typed into the address of a server: its origin, nothing else (https://host or http://host:8080).
 * The API paths are always added by the app, so a path, query or sign-in in the address is refused rather than guessed at.
 */
export function normalizeServerUrl(input: string): ServerUrlResult {
  const text = input.trim()
  if (!text) return { ok: false, error: 'กรอกที่อยู่เซิร์ฟเวอร์' }
  if (!/^https?:\/\//i.test(text)) return { ok: false, error: 'ที่อยู่ต้องขึ้นต้นด้วย http:// หรือ https:// เช่น http://helpdesk.company.local:8080' }
  let url: URL
  try {
    url = new URL(text)
  } catch {
    return { ok: false, error: 'ที่อยู่ไม่ถูกต้อง ตรวจการสะกดและเครื่องหมายต่าง ๆ' }
  }
  if (!url.hostname) return { ok: false, error: 'ที่อยู่ไม่ถูกต้อง ไม่มีชื่อเซิร์ฟเวอร์' }
  if (url.username || url.password) return { ok: false, error: 'ไม่ต้องใส่ชื่อผู้ใช้หรือรหัสผ่านในที่อยู่' }
  if ((url.pathname !== '/' && url.pathname !== '') || url.search || url.hash) {
    return { ok: false, error: 'ใส่เฉพาะที่อยู่เซิร์ฟเวอร์ ไม่ต้องมีเส้นทางต่อท้าย เช่น http://helpdesk.company.local:8080' }
  }
  return { ok: true, url: url.origin }
}

/** True when talking to this address sends everything unencrypted across the network (anything but https and this computer). */
export function isUnencrypted(url: string): boolean {
  try {
    const u = new URL(url)
    return u.protocol === 'http:' && !['localhost', '127.0.0.1', '[::1]'].includes(u.hostname)
  } catch {
    return false
  }
}

/** Compares addresses ignoring a trailing slash and capitals in the host. */
export function sameServer(a: string, b: string): boolean {
  const norm = (value: string) => {
    const r = normalizeServerUrl(value)
    return r.ok ? r.url : value.replace(/\/+$/, '')
  }
  return norm(a) === norm(b)
}
