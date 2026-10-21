import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import ChangePasswordForm from './settings/ChangePasswordForm'
import Button from './ui/Button'
import { Card } from './ui/Card'
import { authService } from '../services/auth.service'
import { useAuthStore } from '../stores/authStore'

/**
 * Keeps the app closed until a new password is chosen, when the account needs one (set by an administrator, or
 * expired). The server refuses every other request meanwhile; this is what the person sees instead of errors.
 * It also re-checks the account now and then, so a requirement set while someone is signed in reaches them.
 */
export default function PasswordGate() {
  const user = useAuthStore((s) => s.user)
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  const updateUser = useAuthStore((s) => s.updateUser)
  const logout = useAuthStore((s) => s.logout)
  const navigate = useNavigate()
  const [done, setDone] = useState(false)

  useEffect(() => {
    if (!isAuthenticated) return
    const check = () => authService.me()
      .then((p) => updateUser({ passwordChangeRequired: p.passwordChangeRequired ?? false, passwordChangeReason: p.passwordChangeReason ?? null }))
      .catch(() => { /* 401 is handled by the API client */ })
    check()
    const timer = window.setInterval(check, 60_000)
    window.addEventListener('focus', check)
    return () => { window.clearInterval(timer); window.removeEventListener('focus', check) }
  }, [isAuthenticated, updateUser])

  if (!isAuthenticated || !user?.passwordChangeRequired) return null

  const expired = user.passwordChangeReason === 'expired'
  return (
    <div className="fixed inset-0 z-[80] grid place-items-center overflow-y-auto bg-scrim p-4" role="presentation">
      <Card className="my-auto w-full max-w-lg" role="dialog" aria-modal="true" aria-labelledby="pw-gate-title">
        <div className="space-y-4 p-6">
          <div className="flex items-start gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-amber-soft text-amber-ink" aria-hidden="true">
              <i className="ti ti-lock-exclamation text-xl" />
            </span>
            <div>
              <h2 id="pw-gate-title" className="text-lg font-semibold text-ink">{expired ? 'รหัสผ่านหมดอายุ' : 'กรุณาตั้งรหัสผ่านใหม่'}</h2>
              <p className="mt-1 text-[13px] text-ink-3">
                {expired ? 'รหัสผ่านของคุณใช้มานานเกินกำหนด' : 'ผู้ดูแลระบบตั้งรหัสผ่านให้คุณ'} ตั้งรหัสผ่านของคุณเองก่อนจึงจะใช้งานต่อได้
              </p>
            </div>
          </div>
          {done && <p className="text-sm text-green-ink">เปลี่ยนรหัสผ่านแล้ว</p>}
          <ChangePasswordForm onChanged={() => { setDone(true); updateUser({ passwordChangeRequired: false, passwordChangeReason: null }) }} />
          <div className="border-t border-line pt-3 text-right">
            <Button variant="ghost" size="sm" icon="logout" onClick={() => { logout(); navigate('/login') }}>ออกจากระบบ</Button>
          </div>
        </div>
      </Card>
    </div>
  )
}
