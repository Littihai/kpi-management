/** Parses "1.2.3" / "v1.2" / "1.2.3-beta" into [major, minor, patch]; null when it is not a version. */
export function parseVersion(v: unknown): [number, number, number] | null {
  if (typeof v !== 'string') return null
  const m = /^v?(\d{1,5})(?:\.(\d{1,5}))?(?:\.(\d{1,5}))?(?:[-+].*)?$/.exec(v.trim())
  return m ? [Number(m[1]), Number(m[2] ?? 0), Number(m[3] ?? 0)] : null
}

/** True only when both are valid versions and `current` is older than `minimum`. Garbage never blocks anyone. */
export function isOlderThan(current: unknown, minimum: unknown): boolean {
  const a = parseVersion(current)
  const b = parseVersion(minimum)
  if (!a || !b) return false
  for (let i = 0; i < 3; i++) if (a[i] !== b[i]) return a[i] < b[i]
  return false
}
