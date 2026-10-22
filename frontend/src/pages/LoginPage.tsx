import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import BrandName from '../components/BrandName'
import Button from '../components/ui/Button'
import { TextInput } from '../components/ui/Field'
import { Alert } from '../components/ui/Layout'
import { authService } from '../services/auth.service'
import ServerAddressDialog from '../components/ServerAddressDialog'
import { currentServerUrl, describeServer, serverChoiceAllowed } from '../lib/server'
import { useAuthStore } from '../stores/authStore'
import { apiError } from '../utils/errors'

export default function LoginPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [serverOpen, setServerOpen] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)
    setError('')
    try {
      const res = await authService.login({ email, password })
      setAuth(res.token, {
        email: res.email,
        fullName: res.fullName,
        role: res.role,
        departmentId: res.departmentId,
        permissions: res.permissions,
        passwordChangeRequired: res.passwordChangeRequired,
        passwordChangeReason: res.passwordChangeReason,
      })
      navigate('/dashboard')
    } catch (err) {
      setError(apiError(err, 'อีเมลหรือรหัสผ่านไม่ถูกต้อง'))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="grid min-h-[var(--vh100)] lg:grid-cols-[minmax(0,1fr)_minmax(0,1.05fr)]">
      {/* Brand panel */}
      <aside className="relative hidden overflow-hidden bg-deep p-12 text-on-deep lg:flex lg:flex-col lg:justify-between">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-accent" aria-hidden="true">
            <i className="ti ti-chart-donut-3 text-2xl" />
          </span>
          <BrandName className="text-lg font-semibold" />
        </div>

        <div>
          <p className="text-xs font-semibold text-on-deep/50">ระบบภายในองค์กร</p>
          <h1 className="mt-3 max-w-md text-4xl font-semibold leading-tight">
            แจ้งปัญหา ติดตามงาน<br />และแก้ให้จบ <span className="text-accent">ในที่เดียว</span>
          </h1>
          <ul className="mt-8 max-w-sm space-y-3 text-[15px] text-on-deep/75">
            {[
              ['ticket', 'แจ้งเรื่องง่าย ๆ แนบรูปได้ ไม่ต้องรู้ว่าควรบอกใคร'],
              ['list-check', 'ติดตามสถานะและดูวิธีแก้ไขได้ทุกขั้นตอน'],
              ['bell-ringing', 'อัปเดตแบบสด ไม่ต้องรีเฟรชหน้า'],
            ].map(([icon, text]) => (
              <li key={icon} className="flex items-start gap-3">
                <i className={`ti ti-${icon} mt-0.5 text-xl text-accent`} aria-hidden="true" />{text}
              </li>
            ))}
          </ul>
        </div>

        <p className="text-xs text-on-deep/40">ทีม IT พร้อมตอบรับทุกเรื่องภายใน 2 ชั่วโมง</p>
        <span className="pointer-events-none absolute -bottom-32 -right-32 h-96 w-96 rounded-full border border-white/10" aria-hidden="true" />
        <span className="pointer-events-none absolute -bottom-12 -right-12 h-56 w-56 rounded-full border border-white/10" aria-hidden="true" />
      </aside>

      {/* Form */}
      <main className="flex items-center justify-center bg-canvas px-4 py-12 sm:px-8">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-accent text-on-accent" aria-hidden="true">
              <i className="ti ti-chart-donut-3 text-2xl" />
            </span>
            <BrandName className="text-lg font-semibold" />
          </div>

          <h2 className="text-2xl font-semibold">เข้าสู่ระบบ</h2>
          <p className="mt-1 text-sm text-ink-2">ใช้อีเมลและรหัสผ่านที่ผู้ดูแลระบบมอบให้</p>

          <form onSubmit={handleSubmit} className="mt-8 space-y-5">
            {error && <Alert tone="error">{error}</Alert>}

            <TextInput label="อีเมล" type="email" required autoComplete="username" autoFocus value={email}
              placeholder="name@company.com" onChange={(e) => setEmail(e.target.value)} />

            <div>
              <TextInput label="รหัสผ่าน" type={showPassword ? 'text' : 'password'} required autoComplete="current-password"
                value={password} onChange={(e) => setPassword(e.target.value)} />
              <label className="mt-2 inline-flex cursor-pointer items-center gap-2 text-[13px] text-ink-2">
                <input type="checkbox" checked={showPassword} onChange={(e) => setShowPassword(e.target.checked)}
                  className="h-4 w-4 rounded border-line-strong accent-accent" />
                แสดงรหัสผ่าน
              </label>
            </div>

            <Button type="submit" variant="primary" size="lg" loading={loading} className="w-full">เข้าสู่ระบบ</Button>
          </form>

          {serverChoiceAllowed() && (
            <p className="mt-6 flex flex-wrap items-center gap-x-2 text-[13px] text-ink-3">
              <i className="ti ti-server text-base" aria-hidden="true" />
              <span>เซิร์ฟเวอร์: <span className="font-medium text-ink-2" data-testid="server-address">{describeServer(currentServerUrl())}</span></span>
              <button type="button" onClick={() => setServerOpen(true)} className="font-medium text-accent hover:underline">เปลี่ยน</button>
            </p>
          )}

          <div className="mt-8 rounded-card border border-line bg-card p-4 text-sm">
            <p className="flex items-center gap-2 font-medium"><i className="ti ti-lifebuoy text-lg text-accent" aria-hidden="true" />เข้าสู่ระบบไม่ได้?</p>
            <p className="mt-1 text-ink-2">ลืมรหัสผ่านหรือบัญชีถูกล็อก แจ้งทีม IT ได้โดยไม่ต้องเข้าสู่ระบบ</p>
            <Link to="/report" className="mt-2 inline-flex items-center gap-1 font-medium text-accent hover:underline">
              แจ้งปัญหา <i className="ti ti-arrow-right" aria-hidden="true" />
            </Link>
          </div>
        </div>
      </main>
      {serverOpen && <ServerAddressDialog open onClose={() => setServerOpen(false)} />}
    </div>
  )
}
