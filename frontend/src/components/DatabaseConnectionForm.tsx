import { useEffect, useRef, useState } from 'react'
import Button from './ui/Button'
import { SelectInput, TextInput } from './ui/Field'
import { Alert } from './ui/Layout'
import { LoadingState } from './ui/LoadingIndicator'
import { ConnectionProblem, DatabaseChecklist } from './settings/DatabaseChecklist'
import { stepLabel } from './settings/databaseText'
import type { DatabaseReport } from '../services/database.service'
import { rememberConnection, type ConnectionEntry } from '../lib/connectionHistory'
import {
  apiIsSameOrigin, appTalksToThisPc, setupService, SetupFailure, type ServerProbe, type SetupAdmin, type SetupConnection, type SetupPlan, type SetupStatus,
} from '../services/setup.service'

const MIN_PASSWORD = 8

const blockerText: Record<NonNullable<SetupPlan['blocker']>, string> = {
  'no-permission-database': 'ฐานข้อมูลนี้ยังไม่มี และบัญชีนี้สร้างไม่ได้ ต้องเป็น sysadmin หรือมีบทบาท dbcreator ใช้บัญชี sa หรือให้ผู้ดูแล SQL สร้างฐานข้อมูลนี้ไว้ก่อน',
  'no-access': 'ฐานข้อมูลนี้มีอยู่แล้ว แต่บัญชีนี้เปิดไม่ได้ ให้ผู้ดูแล SQL เพิ่มบัญชีนี้เป็นผู้ใช้ของฐานข้อมูลนั้น หรือใช้บัญชี sa',
  'no-permission-login': 'บัญชีนี้สร้างบัญชีสำหรับโปรแกรมไม่ได้ ติ๊ก "ใช้บัญชีนี้เป็นบัญชีของโปรแกรมด้วย" หรือใช้บัญชี sa',
  'invalid-login': 'ชื่อบัญชีของเครื่องนี้ต้องขึ้นต้นด้วย helpdesk_ และใช้ได้เฉพาะตัวอักษรอังกฤษ ตัวเลข และ _ (แก้ใน "ตัวเลือกเพิ่มเติม")',
}

const serverProblem: Record<string, string> = {
  unreachable: 'เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ตรวจชื่อหรือที่อยู่ พอร์ต ว่าเซิร์ฟเวอร์ SQL ทำงานอยู่ (เปิดโปรโตคอล TCP/IP) และ firewall',
  'login-failed': 'เซิร์ฟเวอร์ปฏิเสธการล็อกอิน ตรวจชื่อผู้ใช้และรหัสผ่าน และถ้าใช้ sa ต้องเปิดโหมด "SQL Server and Windows Authentication" และเปิดใช้งานบัญชี sa',
  other: 'เชื่อมต่อไม่สำเร็จ',
}

const provisioningLabel: Record<string, string> = {
  'create-database': 'สร้างฐานข้อมูล',
  'create-login': 'สร้างบัญชีของเครื่องนี้',
  'reset-login': 'ตั้งรหัสผ่านใหม่ให้บัญชีของเครื่องนี้',
  ...stepLabel,
}

const failureText: Partial<Record<SetupFailure['code'], string>> = {
  'cross-origin': 'หน้านี้เชื่อมต่อกับเซิร์ฟเวอร์ที่อยู่คนละที่กับหน้าเว็บ ตั้งค่าได้เฉพาะจากเบราว์เซอร์บนเครื่องเซิร์ฟเวอร์ที่ http://localhost:พอร์ต',
  busy: 'กำลังตั้งค่าอยู่ รอให้เสร็จก่อน',
}

const localNames = ['localhost', '127.0.0.1', '[::1]', '::1']

/**
 * Where SQL Server is and who may create things on it (sa, or the Windows account). "Connect" says what is there; "Start" creates the
 * database if it is not there yet (once: the next machine finds it and only gets an account of its own), creates what is missing in it,
 * makes the first administrator if there is none, and remembers the answer. sa is used once and not kept unless asked.
 */
export default function DatabaseConnectionForm({ status, onBusyChange, prefill }: { status: SetupStatus; onBusyChange?: (busy: boolean) => void; prefill?: ConnectionEntry }) {
  const current = status.current
  const sameOrigin = apiIsSameOrigin()
  const [key, setKey] = useState('')
  const [server, setServer] = useState(prefill?.server ?? current?.server ?? '')
  const [auth, setAuth] = useState<'sql' | 'windows'>(prefill?.auth ?? (current?.auth === 'windows' ? 'windows' : 'sql'))
  const [user, setUser] = useState(prefill?.user ?? current?.user ?? 'sa')
  const [password, setPassword] = useState('')
  const [database, setDatabase] = useState(prefill?.database ?? current?.database ?? 'helpdesk')
  const [trust, setTrust] = useState(prefill?.trust ?? current?.trust ?? true)
  const [useSame, setUseSame] = useState(false)
  const [loginName, setLoginName] = useState(status.machineLogin ?? '')
  const [confirmReset, setConfirmReset] = useState(false)

  const [probe, setProbe] = useState<ServerProbe | null>(null)
  const [plan, setPlan] = useState<SetupPlan | null>(null)
  const [report, setReport] = useState<DatabaseReport | null>(null)
  const [startupError, setStartupError] = useState<string | null>(status.reason === 'StartupFailed' ? status.message : null)
  const [testing, setTesting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [phase, setPhase] = useState<'form' | 'starting' | 'slow'>('form')
  // every change to the form drops what "Connect" found; an answer that arrives after that belongs to what was typed before and is ignored
  const generation = useRef(0)

  const [adminEmail, setAdminEmail] = useState('')
  const [adminFirst, setAdminFirst] = useState('')
  const [adminLast, setAdminLast] = useState('')
  const [adminPassword, setAdminPassword] = useState('')
  const [adminAgain, setAdminAgain] = useState('')

  const busy = saving || phase !== 'form'
  useEffect(() => { onBusyChange?.(busy) }, [busy, onBusyChange])

  const connection = (): SetupConnection => ({
    server: server.trim(), database: database.trim(), auth, trust,
    ...(auth === 'sql' ? { user: user.trim(), password } : {}),
  })
  const appAccess = (confirmed = confirmReset) => ({ mode: useSame ? ('same' as const) : ('auto' as const), login: loginName.trim() || undefined, confirmReset: confirmed })
  const keyNeeded = status.keyRequired
  const formReady = sameOrigin && (!keyNeeded || key.trim() !== '') && server.trim() !== '' && database.trim() !== '' && (auth === 'windows' || (user.trim() !== '' && password !== ''))

  const connected = probe?.connected === true && plan !== null
  const needsAdmin = connected && report?.seed?.hasActiveAdmin !== true
  // what the application will use, with the person's choice applied (not read back from an answer given before the box was ticked)
  const access = auth === 'windows' ? 'windows' : useSame ? 'same' : probe?.canCreateLogin ? 'dedicated' : 'same'
  const blocker = plan?.blocker === 'no-permission-login' && useSame ? null : plan?.blocker ?? null
  const loginExists = access === 'dedicated' && plan?.loginExists === true
  const adminProblem = !needsAdmin ? null
    : !adminEmail.includes('@') || adminEmail.includes(' ') ? 'กรอกอีเมลผู้ดูแลระบบให้ถูกต้อง'
    : adminFirst.trim() === '' ? 'กรอกชื่อผู้ดูแลระบบ'
    : adminPassword.length < MIN_PASSWORD ? `รหัสผ่านต้องยาวอย่างน้อย ${MIN_PASSWORD} ตัวอักษร`
    : adminPassword !== adminAgain ? 'รหัสผ่านสองช่องไม่ตรงกัน'
    : null
  const canStart = formReady && connected && blocker === null && adminProblem === null && status.canWriteConfig && (!loginExists || confirmReset)

  const forget = () => {
    generation.current++
    setProbe(null); setPlan(null); setReport(null); setConfirmReset(false)
    setStartupError(status.reason === 'StartupFailed' ? status.message : null)
  }

  const explain = (failure: unknown) => {
    if (!(failure instanceof SetupFailure)) return 'เกิดข้อผิดพลาด'
    if (failure.server && !failure.server.connected) return serverProblem[failure.server.problem ?? 'other'] ?? failure.message
    return failureText[failure.code] ?? failure.message
  }

  const show = (result: Awaited<ReturnType<typeof setupService.test>>) => {
    setProbe(result.server)
    setPlan(result.plan)
    setReport(result.report)
    if (result.startupError) setStartupError(result.startupError)
  }

  const test = async () => {
    setTesting(true)
    setError('')
    forget()
    const mine = generation.current
    try {
      const result = await setupService.test(keyNeeded ? key.trim() : undefined, { connection: connection(), appAccess: appAccess(false) })
      if (mine === generation.current) {
        show(result)
        // it worked: it goes to the History tab (where and who only, never the password)
        if (result.server.connected) rememberConnection({ server: server.trim(), database: database.trim(), auth, user: user.trim(), trust })
      }
    } catch (failure) {
      if (mine === generation.current) setError(explain(failure))
    } finally {
      setTesting(false)
    }
  }

  // after a step failed part-way (the database may now exist, a login may have been made) look again, so the form says what is true now
  const refresh = async () => {
    const mine = generation.current
    try {
      const result = await setupService.test(keyNeeded ? key.trim() : undefined, { connection: connection(), appAccess: appAccess(false) })
      if (mine === generation.current) show(result)
    } catch { /* the message already on screen stays */ }
  }

  const start = async () => {
    setSaving(true)
    setError('')
    const mine = generation.current
    try {
      const admin: SetupAdmin | undefined = needsAdmin
        ? { email: adminEmail.trim(), firstName: adminFirst.trim(), lastName: adminLast.trim(), password: adminPassword }
        : undefined
      await setupService.apply(keyNeeded ? key.trim() : undefined, {
        connection: connection(), repair: { applyMigrations: true, createMissingObjects: true, seed: true }, admin, appAccess: appAccess(),
      })
      setPhase('starting')
      if (await setupService.waitUntilRunning()) window.location.assign('/login')
      else setPhase('slow')
    } catch (failure) {
      setError(explain(failure))
      if (failure instanceof SetupFailure && failure.code !== 'network' && failure.code !== 'cross-origin' && mine === generation.current) void refresh()
    } finally {
      setSaving(false)
    }
  }

  if (phase !== 'form') {
    return (
      <div className="space-y-3 py-6 text-center" role="status">
        {phase === 'starting'
          ? <LoadingState title="บันทึกแล้ว กำลังเริ่มระบบ" hint="รอสักครู่ เมื่อพร้อมแล้วจะพาไปหน้าเข้าสู่ระบบเอง" />
          : <>
              <i className="ti ti-clock-exclamation text-3xl text-amber-ink" aria-hidden="true" />
              <p className="text-base font-medium text-ink">บันทึกแล้ว แต่ระบบยังไม่ตอบ</p>
              <p className="text-sm text-ink-2">การสร้างตารางครั้งแรกอาจใช้เวลานาน ลองรีเฟรชหน้านี้อีกครั้งในอีกสักครู่ ถ้ายังเป็นเหมือนเดิมให้ดูล็อกของ service (หน้านี้จะบอกสาเหตุถ้าโปรแกรมเริ่มไม่ได้)</p>
              <Button variant="primary" icon="refresh" onClick={() => window.location.reload()}>รีเฟรช</Button>
            </>}
      </div>
    )
  }

  const startLabel = plan?.createDatabase ? 'สร้างฐานข้อมูลและเริ่มระบบ' : 'เชื่อมต่อและเริ่มระบบ'

  return (
    <div className="space-y-4">
      <p className="text-sm text-ink-2">
        ใส่ที่อยู่ SQL Server กับบัญชีที่สร้างฐานข้อมูลได้ (เช่น <span className="font-mono">sa</span>) แล้วกด "เชื่อมต่อ" ระบบจะบอกว่ามีฐานข้อมูลนี้อยู่แล้วหรือไม่
        ถ้ายังไม่มี จะสร้างให้ครั้งเดียว ถ้ามีแล้ว (เช่นเครื่องอื่นในเครือข่ายสร้างไว้) จะเชื่อมต่อโดยไม่สร้างซ้ำ
      </p>

      {!sameOrigin && (
        <Alert tone="error">
          หน้านี้ไม่ได้เปิดจากตัวเซิร์ฟเวอร์เอง (เช่นเว็บที่อยู่คนละที่กับเซิร์ฟเวอร์ หรือแอป Windows ที่ต่อกับเซิร์ฟเวอร์เครื่องอื่น) จึงตั้งค่าที่นี่ไม่ได้ ให้เปิดเบราว์เซอร์บนเครื่องเซิร์ฟเวอร์ไปที่ <span className="font-mono">http://localhost:พอร์ต</span> หรือถ้าเซิร์ฟเวอร์อยู่ในเครื่องนี้ ให้เลือกเซิร์ฟเวอร์ในเครื่องนี้ที่ปุ่ม "เชื่อมต่อ" บนแถบด้านบน
        </Alert>
      )}
      {status.problem === 'password-unreadable' && (
        <Alert tone="error">รหัสผ่านฐานข้อมูลที่บันทึกไว้ก่อนหน้านี้อ่านไม่ได้ (คีย์ของเซิร์ฟเวอร์เปลี่ยน หรือไฟล์ตั้งค่ามาจากเครื่องอื่น) กรอกรหัสผ่านและเชื่อมต่อใหม่</Alert>
      )}
      {status.reason === 'Unreachable' && (
        <Alert tone="error">
          <p className="font-medium">เชื่อมต่อฐานข้อมูลที่ตั้งไว้เดิมไม่ได้{status.problem ? ` (${serverProblem[status.problem] ?? serverProblem.other})` : ''}</p>
          <p className="mt-1 text-[13px]">ถ้าแก้ที่ฝั่ง SQL Server แล้ว ระบบลองใหม่เองทุก 10 วินาที (ยกเว้นกรณีล็อกอินไม่ผ่าน ที่ต้องกรอกรหัสผ่านใหม่ด้านล่าง) และกลับมาทำงานเอง หรือกรอกที่อยู่ใหม่ด้านล่างได้เลย</p>
        </Alert>
      )}
      {sameOrigin && !status.remoteAllowed && !appTalksToThisPc() && !localNames.includes(window.location.hostname) && (
        <Alert tone="info">
          บันทึกได้เฉพาะเมื่อเปิดจากตัวเครื่องเซิร์ฟเวอร์เอง ให้เปิดเว็บบนเครื่องเซิร์ฟเวอร์ด้วย <span className="font-mono">http://localhost:{window.location.port || '80'}</span> โดยตรง (ไม่ผ่าน proxy)
        </Alert>
      )}
      {!status.canWriteConfig && <Alert tone="error">โปรแกรมเขียนไฟล์ตั้งค่าไม่ได้ ให้สิทธิ์เขียนโฟลเดอร์ของไฟล์ตั้งค่าแก่บัญชีที่รัน service แล้วรีเฟรชหน้านี้</Alert>}
      {!status.jwtConfigured && auth === 'sql' && <Alert tone="error">Jwt__Key ของเซิร์ฟเวอร์ยังไม่ได้ตั้งหรือใช้ไม่ได้ (สั้นเกินไปหรือเป็นค่าตัวอย่าง) จึงเก็บรหัสผ่านฐานข้อมูลไม่ได้ ตั้งค่าให้ถูกต้องก่อน หรือใช้บัญชี Windows</Alert>}
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      {startupError && <Alert tone="error"><p className="font-medium">เชื่อมต่อฐานข้อมูลได้ แต่โปรแกรมเริ่มทำงานไม่ได้เพราะ</p><p className="mt-1 break-all text-xs">{startupError}</p></Alert>}

      <fieldset disabled={!sameOrigin} className="grid gap-4 sm:grid-cols-2">
        {keyNeeded && (
          <TextInput label="รหัสตั้งค่า (Setup key)" type="password" autoComplete="off" value={key} onChange={(e) => { setKey(e.target.value); forget() }}
            wrapperClassName="sm:col-span-2"
            hint={status.keyFile ? `อ่านได้จากไฟล์ ${status.keyFile} บนเซิร์ฟเวอร์` : 'รหัสนี้คือค่า Setup:Key ของ service'} />
        )}
        <TextInput label="เซิร์ฟเวอร์ SQL" required value={server} onChange={(e) => { setServer(e.target.value); forget() }}
          placeholder="SQL01 หรือ SQL01\SQLEXPRESS หรือ 10.0.0.5,1433" hint="ชื่อ ชื่อ\instance หรือที่อยู่,พอร์ต" wrapperClassName="sm:col-span-2" />
        <SelectInput label="วิธีล็อกอิน" value={auth} onChange={(e) => { setAuth(e.target.value as 'sql' | 'windows'); forget() }} wrapperClassName="sm:col-span-2"
          options={[{ value: 'sql', label: 'บัญชี SQL Server (เช่น sa)' }, { value: 'windows', label: 'บัญชี Windows ที่ service ใช้อยู่' }]}
          hint={auth === 'windows' ? 'บัญชีที่รัน service ต้องมีสิทธิ์สร้างฐานข้อมูลบน SQL Server (หรือฐานข้อมูลต้องมีอยู่แล้ว)' : undefined} />
        {auth === 'sql' && (
          <>
            <TextInput label="ชื่อผู้ใช้" required autoComplete="off" value={user} onChange={(e) => { setUser(e.target.value); forget() }} />
            <TextInput label="รหัสผ่าน" required type="password" autoComplete="new-password" value={password} onChange={(e) => { setPassword(e.target.value); forget() }} />
          </>
        )}
        <TextInput label="ชื่อฐานข้อมูล" required value={database} onChange={(e) => { setDatabase(e.target.value); forget() }}
          hint="ถ้ายังไม่มีระบบจะสร้างให้ (ตัวอักษรอังกฤษ ตัวเลข _ - . ห้ามใช้ master, model, msdb, tempdb)" wrapperClassName="sm:col-span-2" />
        {auth === 'sql' && (
          <details className="text-[13px] text-ink-2 sm:col-span-2">
            <summary className="cursor-pointer font-medium text-ink">ตัวเลือกเพิ่มเติม</summary>
            <div className="mt-2 max-w-sm">
              <TextInput label="ชื่อบัญชีของเครื่องนี้" value={loginName} onChange={(e) => { setLoginName(e.target.value); forget() }}
                hint="บัญชีที่ระบบสร้างให้เครื่องนี้ แต่ละเครื่องที่ต่อกับ SQL Server เดียวกันใช้ชื่อไม่ซ้ำกัน ต้องขึ้นต้นด้วย helpdesk_" />
            </div>
          </details>
        )}
        <label className="inline-flex cursor-pointer items-center gap-2 text-[13px] text-ink-2 sm:col-span-2">
          <input type="checkbox" checked={trust} onChange={(e) => { setTrust(e.target.checked); forget() }} className="h-4 w-4 rounded border-line-strong accent-accent" />
          เชื่อใบรับรองของเซิร์ฟเวอร์ (TrustServerCertificate) ปิดได้ถ้าเซิร์ฟเวอร์มีใบรับรองที่ออกโดยหน่วยงานที่เชื่อถือ
        </label>
        <div className="flex justify-end sm:col-span-2">
          <Button variant="secondary" icon="plug-connected" loading={testing} disabled={!formReady} onClick={test}>เชื่อมต่อ</Button>
        </div>
      </fieldset>

      {probe && !probe.connected && (
        <Alert tone="error">
          <p className="font-medium">{serverProblem[probe.problem ?? 'other']}</p>
          {probe.message && probe.problem === 'other' && <p className="mt-1 break-all text-xs">{probe.message}</p>}
        </Alert>
      )}

      {connected && probe && plan && (
        <div className="space-y-3">
          <div className="rounded-control border border-line bg-surface px-4 py-3 text-[13px]">
            <p className="flex items-center gap-2 font-medium text-ink"><i className="ti ti-circle-check text-lg text-green-ink" aria-hidden="true" />เชื่อมต่อ SQL Server ได้</p>
            {probe.version && <p className="mt-0.5 text-ink-3">เวอร์ชัน {probe.version}</p>}
            <ul className="mt-2 space-y-1.5 text-ink-2">
              <li className="flex items-start gap-2">
                <i className={`ti ${plan.createDatabase ? 'ti-database-plus text-accent' : 'ti-database-check text-green-ink'} mt-0.5 text-base`} aria-hidden="true" />
                {plan.createDatabase
                  ? <span>ยังไม่มีฐานข้อมูล <b className="text-ink">{database.trim()}</b> ระบบจะสร้างให้ (ครั้งเดียว) แล้วสร้างตารางและข้อมูลตั้งต้น</span>
                  : <span>พบฐานข้อมูล <b className="text-ink">{database.trim()}</b> อยู่แล้ว จะ <b className="text-ink">ไม่สร้างใหม่</b> แค่เชื่อมต่อ และสร้างเฉพาะส่วนที่ขาด (ถ้ามี)</span>}
              </li>
              <li className="flex items-start gap-2">
                <i className="ti ti-key mt-0.5 text-base text-accent" aria-hidden="true" />
                {access === 'dedicated' && <span>เครื่องนี้ได้บัญชีของตัวเอง <span className="font-mono text-ink">{loginName.trim() || plan.login}</span> (เป็นเจ้าของฐานข้อมูลนี้เท่านั้น) บัญชี {user.trim() || 'sa'} ใช้ครั้งเดียวและไม่ถูกบันทึก</span>}
                {access === 'same' && <span>โปรแกรมจะใช้บัญชี <span className="font-mono text-ink">{user.trim()}</span> ที่กรอก (เก็บแบบเข้ารหัสในไฟล์ตั้งค่า)</span>}
                {access === 'windows' && <span>โปรแกรมจะใช้บัญชี Windows ที่ service ใช้อยู่ ไม่มีรหัสผ่านในไฟล์ตั้งค่า</span>}
              </li>
            </ul>
          </div>

          {auth === 'sql' && probe.canCreateLogin && (
            <label className="inline-flex cursor-pointer items-start gap-2 text-[13px] text-ink-2">
              <input type="checkbox" checked={useSame} onChange={(e) => { setUseSame(e.target.checked); setConfirmReset(false) }} className="mt-0.5 h-4 w-4 rounded border-line-strong accent-accent" />
              <span>ใช้บัญชี <span className="font-mono">{user.trim()}</span> เป็นบัญชีของโปรแกรมด้วย <span className="text-ink-3">(ไม่แนะนำ: โปรแกรมจะมีสิทธิ์ทั้งเซิร์ฟเวอร์ ถ้ารหัสนี้รั่วจะเสียหายกว่า)</span></span>
            </label>
          )}

          {loginExists && (
            <Alert tone="warning">
              <p className="font-medium">มีบัญชี <span className="font-mono">{loginName.trim() || plan.login}</span> อยู่บน SQL Server แล้ว</p>
              <p className="mt-0.5 text-[13px]">ถ้าเป็นของเครื่องนี้เองที่ติดตั้งใหม่ ติ๊กยืนยันเพื่อตั้งรหัสผ่านใหม่ให้ แต่ถ้าเป็นของเครื่องอื่น เครื่องนั้นจะต่อฐานข้อมูลไม่ได้ ให้ตั้งชื่ออื่นใน "ตัวเลือกเพิ่มเติม"</p>
              <label className="mt-2 inline-flex cursor-pointer items-center gap-2 text-[13px]">
                <input type="checkbox" checked={confirmReset} onChange={(e) => setConfirmReset(e.target.checked)} className="h-4 w-4 rounded border-line-strong accent-accent" />
                ยืนยัน ตั้งรหัสผ่านใหม่ให้บัญชีนี้
              </label>
            </Alert>
          )}

          {blocker && <Alert tone="error">{blockerText[blocker]}</Alert>}
          {report && <DatabaseChecklist report={report} adminHint="ยังไม่มีผู้ดูแลระบบ กรอกด้านล่างเพื่อสร้างให้" />}
          {report?.connected === false && <ConnectionProblem report={report} />}
        </div>
      )}

      {needsAdmin && blocker === null && (
        <div className="space-y-3 rounded-control border border-line p-4">
          <p className="text-sm font-semibold text-ink">ผู้ดูแลระบบคนแรก</p>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput label="อีเมล (ใช้เข้าสู่ระบบ)" required type="email" autoComplete="off" value={adminEmail} onChange={(e) => setAdminEmail(e.target.value)} wrapperClassName="sm:col-span-2" />
            <TextInput label="ชื่อ" required autoComplete="off" value={adminFirst} onChange={(e) => setAdminFirst(e.target.value)} />
            <TextInput label="นามสกุล" autoComplete="off" value={adminLast} onChange={(e) => setAdminLast(e.target.value)} />
            <TextInput label="รหัสผ่าน" required type="password" autoComplete="new-password" value={adminPassword} onChange={(e) => setAdminPassword(e.target.value)} hint={`อย่างน้อย ${MIN_PASSWORD} ตัวอักษร`} />
            <TextInput label="รหัสผ่านอีกครั้ง" required type="password" autoComplete="new-password" value={adminAgain} onChange={(e) => setAdminAgain(e.target.value)} />
            {adminProblem && (adminEmail || adminPassword) && <p className="text-xs text-amber-ink sm:col-span-2">{adminProblem}</p>}
          </div>
        </div>
      )}

      {connected && (
        <div className="flex flex-wrap items-center justify-end gap-3">
          <span className="mr-auto text-xs text-ink-3">{plan?.createDatabase ? 'เป็นฐานข้อมูลใหม่ ไม่ต้องสำรองอะไร' : 'ถ้าเป็นฐานข้อมูลที่ใช้งานอยู่ ควรสำรองไว้ก่อน'}</span>
          <Button variant="primary" size="lg" icon="player-play" loading={saving} disabled={!canStart} onClick={start}>{startLabel}</Button>
        </div>
      )}
      {connected && plan && (
        <p className="text-right text-[11px] text-ink-3">
          ขั้นตอน: {[plan.createDatabase ? provisioningLabel['create-database'] : null, access === 'dedicated' ? provisioningLabel[loginExists ? 'reset-login' : 'create-login'] : null, report?.state === 'Ready' ? null : 'สร้างตารางและข้อมูลตั้งต้นที่ขาด', needsAdmin ? 'สร้างผู้ดูแลระบบ' : null].filter(Boolean).join(' → ')}
        </p>
      )}
    </div>
  )
}
