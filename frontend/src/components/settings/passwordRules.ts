import type { PasswordPolicy } from '../../services/auth.service'

export interface RuleResult {
  label: string
  ok: boolean
}

/** The same rules the server enforces, for showing a live checklist while the person types. The server decides. */
export function checkPassword(policy: PasswordPolicy, password: string): RuleResult[] {
  const rules: RuleResult[] = [{ label: `ยาวอย่างน้อย ${policy.minLength} ตัวอักษร`, ok: password.length >= policy.minLength }]
  if (policy.requireUpper) rules.push({ label: 'มีตัวพิมพ์ใหญ่ (A-Z)', ok: /[A-Z]/.test(password) })
  if (policy.requireLower) rules.push({ label: 'มีตัวพิมพ์เล็ก (a-z)', ok: /[a-z]/.test(password) })
  if (policy.requireDigit) rules.push({ label: 'มีตัวเลข (0-9)', ok: /[0-9]/.test(password) })
  if (policy.requireSpecial) rules.push({ label: 'มีอักขระพิเศษ เช่น ! @ # $', ok: /[^\p{L}\p{N}\s]/u.test(password) })
  return rules
}
