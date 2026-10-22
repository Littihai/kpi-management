import { useState } from 'react'
import Button from '../components/ui/Button'
import { Card, CardHeader } from '../components/ui/Card'
import { SelectInput, TextInput } from '../components/ui/Field'
import { Alert } from '../components/ui/Layout'
import { ConnectionProblem, DatabaseChecklist, DatabaseFacts } from '../components/settings/DatabaseChecklist'
import { problemText, stepLabel } from '../components/settings/databaseText'
import type { DatabaseReport } from '../services/database.service'
import { setupService, SetupFailure, type SetupAdmin, type SetupConnection, type SetupStatus } from '../services/setup.service'

const isLocalHost = () => ['localhost', '127.0.0.1', '[::1]', '::1'].includes(window.location.hostname)
const MIN_PASSWORD = 8

/**
 * Shown by the server while it does not know where its database is (first install, or the database cannot be reached).
 * The person types the setup key (read from a file on the server), says where SQL Server is, and the server creates what is
 * missing, makes the first administrator, remembers the answer and starts, without anyone restarting the service.
 */
export default function SetupPage({ status }: { status: SetupStatus }) {
  const current = status.current
  const [key, setKey] = useState('')
  const [server, setServer] = useState(current?.server ?? '')
  const [database, setDatabase] = useState(current?.database ?? 'helpdesk')
  const [auth, setAuth] = useState<'sql' | 'windows'>(current?.auth === 'windows' ? 'windows' : 'sql')
  const [user, setUser] = useState(current?.user ?? '')
  const [password, setPassword] = useState('')
  const [trust, setTrust] = useState(current?.trust ?? true)

  const [report, setReport] = useState<DatabaseReport | null>(null)
  const [startupError, setStartupError] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [phase, setPhase] = useState<'form' | 'starting' | 'slow'>('form')

  const [adminEmail, setAdminEmail] = useState('')
  const [adminFirst, setAdminFirst] = useState('')
  const [adminLast, setAdminLast] = useState('')
  const [adminPassword, setAdminPassword] = useState('')
  const [adminAgain, setAdminAgain] = useState('')

  const connection = (): SetupConnection => ({
    server: server.trim(), database: database.trim(), auth, trust,
    ...(auth === 'sql' ? { user: user.trim(), password } : {}),
  })
  const formReady = key.trim() !== '' && server.trim() !== '' && database.trim() !== '' && (auth === 'windows' || (user.trim() !== '' && password !== ''))
  const needsAdmin = report?.connected === true && report.seed?.hasActiveAdmin !== true
  const adminProblem = !needsAdmin ? null
    : !adminEmail.includes('@') || adminEmail.includes(' ') ? 'กรอกอีเมลผู้ดูแลระบบให้ถูกต้อง'
    : adminFirst.trim() === '' ? 'กรอกชื่อผู้ดูแลระบบ'
    : adminPassword.length < MIN_PASSWORD ? `รหัสผ่านต้องยาวอย่างน้อย ${MIN_PASSWORD} ตัวอักษร`
    : adminPassword !== adminAgain ? 'รหัสผ่านสองช่องไม่ตรงกัน'
    : null
  const canSave = formReady && report?.connected === true && adminProblem === null

  const explain = (failure: unknown) => {
    if (!(failure instanceof SetupFailure)) return 'เกิดข้อผิดพลาด'
    if (failure.report) setReport(failure.report)
    return failure.message
  }

  const test = async () => {
    setTesting(true)
    setError('')
    try {
      const result = await setupService.test(key.trim(), connection())
      setReport(result.report)
      setStartupError(result.startupError)
    } catch (failure) {
      setReport(null)
      setError(explain(failure))
    } finally {
      setTesting(false)
    }
  }

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      const admin: SetupAdmin | undefined = needsAdmin
        ? { email: adminEmail.trim(), firstName: adminFirst.trim(), lastName: adminLast.trim(), password: adminPassword }
        : undefined
      await setupService.apply(key.trim(), { connection: connection(), repair: { applyMigrations: true, createMissingObjects: true, seed: true }, admin })
      setPhase('starting')
      if (await setupService.waitUntilRunning()) window.location.assign('/login')
      else setPhase('slow')
    } catch (failure) {
      setError(explain(failure))
    } finally {
      setSaving(false)
    }
  }

  if (phase !== 'form') {
    return (
      <Shell>
        <Card>
          <div className="space-y-3 p-8 text-center" role="status">
            {phase === 'starting'
              ? <>
                  <i className="ti ti-loader-2 animate-spin text-3xl text-accent" aria-hidden="true" />
                  <p className="text-base font-medium text-ink">บันทึกแล้ว กำลังเริ่มระบบ</p>
                  <p className="text-sm text-ink-2">รอสักครู่ เมื่อพร้อมแล้วจะพาไปหน้าเข้าสู่ระบบเอง</p>
                </>
              : <>
                  <i className="ti ti-clock-exclamation text-3xl text-amber-ink" aria-hidden="true" />
                  <p className="text-base font-medium text-ink">บันทึกแล้ว แต่ระบบยังไม่ตอบ</p>
                  <p className="text-sm text-ink-2">การสร้างตารางครั้งแรกอาจใช้เวลานาน ลองรีเฟรชหน้านี้อีกครั้งในอีกสักครู่ ถ้ายังเป็นเหมือนเดิมให้ดูล็อกของ service</p>
                  <Button variant="primary" icon="refresh" onClick={() => window.location.reload()}>รีเฟรช</Button>
                </>}
          </div>
        </Card>
      </Shell>
    )
  }

  return (
    <Shell>
      <div className="space-y-5">
        <Card>
          <div className="space-y-2 px-5 py-4 text-sm text-ink-2">
            {status.reason === 'NotConfigured' && <p className="font-medium text-ink">ยังไม่ได้ตั้งค่าฐานข้อมูล</p>}
            {status.reason === 'Unreachable' && (
              <>
                <p className="font-medium text-ink">เชื่อมต่อฐานข้อมูลที่ตั้งไว้ไม่ได้</p>
                <p>{problemText[status.problem ?? 'other']}</p>
                <p className="text-[13px]">ถ้าแก้ที่ฝั่งเซิร์ฟเวอร์ฐานข้อมูลแล้ว ระบบจะลองใหม่เองทุก 10 วินาทีและเริ่มทำงานต่อโดยไม่ต้องทำอะไร หรือกรอกที่อยู่ใหม่ด้านล่างได้เลย</p>
              </>
            )}
            {status.reason === 'StartupFailed' && (
              <>
                <p className="font-medium text-ink">เชื่อมต่อได้ แต่โปรแกรมเริ่มทำงานไม่สำเร็จ</p>
                <p className="text-[13px]">มักเกิดจากบัญชีที่ใช้ไม่มีสิทธิ์สร้างหรือแก้ตาราง ลองทดสอบด้านล่างเพื่อดูว่าขาดอะไร</p>
              </>
            )}
            <p>ตั้งค่าครั้งเดียว ระบบจะจำไว้ในไฟล์ตั้งค่าบนเซิร์ฟเวอร์ และเริ่มทำงานให้ทันที</p>
          </div>
        </Card>

        {!status.remoteAllowed && !isLocalHost() && (
          <Alert tone="info">
            หน้านี้บันทึกค่าได้เฉพาะเมื่อเปิดจากตัวเซิร์ฟเวอร์เอง ให้เปิดเว็บบนเครื่องเซิร์ฟเวอร์ด้วยที่อยู่ <span className="font-mono">http://localhost:{window.location.port || '80'}</span>
          </Alert>
        )}
        {!status.canWriteConfig && (
          <Alert tone="error">โปรแกรมเขียนไฟล์ตั้งค่าไม่ได้ ให้สิทธิ์เขียนโฟลเดอร์ของไฟล์ตั้งค่าแก่บัญชีที่รัน service แล้วรีเฟรชหน้านี้</Alert>
        )}
        {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
        {startupError && <Alert tone="error"><p className="font-medium">โปรแกรมเริ่มไม่ได้เพราะ</p><p className="mt-1 break-all text-xs">{startupError}</p></Alert>}

        <Card>
          <CardHeader eyebrow="ขั้นที่ 1" title="รหัสตั้งค่า" />
          <div className="space-y-3 px-5 pb-5 pt-3">
            <TextInput label="รหัสตั้งค่า (Setup key)" type="password" autoComplete="off" value={key} onChange={(e) => setKey(e.target.value)}
              hint={status.keyFile ? `อ่านได้จากไฟล์ ${status.keyFile} บนเซิร์ฟเวอร์ (เปิดได้เฉพาะผู้ดูแลเครื่อง)` : 'รหัสนี้แสดงตอนติดตั้ง หรืออยู่ในค่า Setup__Key ของ service เพื่อยืนยันว่าเป็นผู้ดูแลเครื่อง'} />
          </div>
        </Card>

        <Card>
          <CardHeader eyebrow="ขั้นที่ 2" title="SQL Server" />
          <div className="grid gap-4 px-5 pb-5 pt-3 sm:grid-cols-2">
            <TextInput label="เซิร์ฟเวอร์" required value={server} onChange={(e) => setServer(e.target.value)} placeholder="SQL01 หรือ SQL01\SQLEXPRESS หรือ 10.0.0.5,1433"
              hint="ชื่อ ชื่อ\instance หรือที่อยู่,พอร์ต" wrapperClassName="sm:col-span-2" />
            <TextInput label="ชื่อฐานข้อมูล" required value={database} onChange={(e) => setDatabase(e.target.value)} hint="ต้องสร้างไว้ก่อนด้วยสคริปต์ 01-create-database.sql" wrapperClassName="sm:col-span-2" />
            <SelectInput label="วิธีล็อกอิน" value={auth} onChange={(e) => setAuth(e.target.value as 'sql' | 'windows')} wrapperClassName="sm:col-span-2"
              options={[{ value: 'sql', label: 'บัญชี SQL Server (ชื่อผู้ใช้และรหัสผ่าน)' }, { value: 'windows', label: 'บัญชี Windows ที่ service ใช้อยู่' }]}
              hint={auth === 'windows' ? 'บัญชีที่รัน service ต้องมีสิทธิ์เปิดฐานข้อมูลนี้ใน SQL Server' : undefined} />
            {auth === 'sql' && (
              <>
                <TextInput label="ชื่อผู้ใช้" required autoComplete="off" value={user} onChange={(e) => setUser(e.target.value)} placeholder="helpdesk_app" />
                <TextInput label="รหัสผ่าน" required type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)}
                  hint={status.jwtConfigured ? 'จะถูกเข้ารหัสก่อนเก็บในไฟล์ตั้งค่า' : 'เซิร์ฟเวอร์ยังไม่มี Jwt__Key จึงเก็บรหัสผ่านไม่ได้ ใช้บัญชี Windows แทน'} />
              </>
            )}
            <label className="inline-flex cursor-pointer items-center gap-2 text-[13px] text-ink-2 sm:col-span-2">
              <input type="checkbox" checked={trust} onChange={(e) => setTrust(e.target.checked)} className="h-4 w-4 rounded border-line-strong accent-accent" />
              เชื่อใบรับรองของเซิร์ฟเวอร์ (TrustServerCertificate) ปิดได้ถ้าเซิร์ฟเวอร์มีใบรับรองที่ออกโดยหน่วยงานที่เชื่อถือ
            </label>
            <div className="flex justify-end sm:col-span-2">
              <Button variant="secondary" icon="plug-connected" loading={testing} disabled={!formReady} onClick={test}>ทดสอบการเชื่อมต่อ</Button>
            </div>
          </div>
        </Card>

        {report && (
          <>
            {!report.connected && <ConnectionProblem report={report} />}
            {report.connected && (
              <>
                <Card><DatabaseFacts report={report} /></Card>
                <DatabaseChecklist report={report} adminHint="ยังไม่มีผู้ดูแลระบบ กรอกในขั้นที่ 3 เพื่อสร้างให้" />
                {report.state === 'NeedsRepair' && (
                  <Alert tone="info">
                    <p className="font-medium">เมื่อกด “บันทึกและเริ่มระบบ” ระบบจะสร้างสิ่งที่ขาดให้ก่อน โดยไม่ลบหรือแก้ของเดิม:</p>
                    <ul className="mt-1 list-inside list-disc text-[13px]">
                      {report.pendingMigrations.length > 0 && <li>{stepLabel['apply-migrations']}</li>}
                      {report.pendingMigrations.length === 0 && (report.missingTables.length > 0 || report.missingColumns.length > 0) && <li>{stepLabel['create-tables']} / {stepLabel['add-columns']}</li>}
                      {(report.seed === null || report.seed.missingRoles.length > 0 || report.seed.permissionsFound < report.seed.permissionsExpected) && <li>{stepLabel.seed}</li>}
                    </ul>
                    <p className="mt-1 text-xs">ถ้าเป็นฐานข้อมูลที่ใช้งานอยู่ ควรสำรองไว้ก่อน</p>
                  </Alert>
                )}
              </>
            )}
          </>
        )}

        {needsAdmin && (
          <Card>
            <CardHeader eyebrow="ขั้นที่ 3" title="ผู้ดูแลระบบคนแรก" />
            <div className="grid gap-4 px-5 pb-5 pt-3 sm:grid-cols-2">
              <TextInput label="อีเมล (ใช้เข้าสู่ระบบ)" required type="email" autoComplete="off" value={adminEmail} onChange={(e) => setAdminEmail(e.target.value)} wrapperClassName="sm:col-span-2" />
              <TextInput label="ชื่อ" required autoComplete="off" value={adminFirst} onChange={(e) => setAdminFirst(e.target.value)} />
              <TextInput label="นามสกุล" autoComplete="off" value={adminLast} onChange={(e) => setAdminLast(e.target.value)} />
              <TextInput label="รหัสผ่าน" required type="password" autoComplete="new-password" value={adminPassword} onChange={(e) => setAdminPassword(e.target.value)} hint={`อย่างน้อย ${MIN_PASSWORD} ตัวอักษร`} />
              <TextInput label="รหัสผ่านอีกครั้ง" required type="password" autoComplete="new-password" value={adminAgain} onChange={(e) => setAdminAgain(e.target.value)} />
              {adminProblem && (adminEmail || adminPassword) && <p className="text-xs text-amber-ink sm:col-span-2">{adminProblem}</p>}
            </div>
          </Card>
        )}

        {report?.connected && (
          <div className="flex justify-end">
            <Button variant="primary" size="lg" icon="player-play" loading={saving} disabled={!canSave || !status.canWriteConfig} onClick={save}>บันทึกและเริ่มระบบ</Button>
          </div>
        )}
      </div>
    </Shell>
  )
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-[var(--vh100)] bg-canvas px-4 py-10 sm:px-8">
      <main className="mx-auto w-full max-w-2xl">
        <div className="mb-6 flex items-center gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-accent text-on-accent" aria-hidden="true">
            <i className="ti ti-chart-donut-3 text-2xl" />
          </span>
          <div>
            <h1 className="text-xl font-semibold text-ink">ตั้งค่าฐานข้อมูล</h1>
            <p className="text-sm text-ink-2">Helpdesk · ตั้งค่าครั้งแรก</p>
          </div>
        </div>
        {children}
      </main>
    </div>
  )
}
