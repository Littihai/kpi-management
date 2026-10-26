import { useEffect, useState } from 'react'
import BrandName from '../components/BrandName'
import ServerAddressDialog from '../components/ServerAddressDialog'
import Button from '../components/ui/Button'
import { LoadingState } from '../components/ui/LoadingBar'
import { Alert } from '../components/ui/Layout'

type Phase = 'starting' | 'choose'

/**
 * First start of the Windows app: nothing is chosen and no company's server is built in.
 *
 * Normally the app starts its own built-in server (one that is part of the installation) and carries on to the sign-in page, where the
 * connect form (Server Name, login, password) opens by itself: the person connects SQL Server, like in SSMS. This page only stays when that
 * cannot be done (the program is not there, or would not start), and then offers a company's server by its address.
 */
export default function ChooseServerPage() {
  const local = window.desktop?.localServer
  const [phase, setPhase] = useState<Phase>(local ? 'starting' : 'choose')
  const [problem, setProblem] = useState('')
  const [attempt, setAttempt] = useState(0)
  const [companyOpen, setCompanyOpen] = useState(false)
  const [available, setAvailable] = useState(false)

  // Turn the built-in server on; when it answers, load the sign-in page (a reload: its address is read once, when the page starts).
  useEffect(() => {
    if (!local) return
    let alive = true
    local.status()
      .then(async (status) => {
        if (!alive) return
        setAvailable(status?.available === true)
        if (status?.available !== true) { setPhase('choose'); return }
        const started = await local.enable()
        if (!alive) return
        if (started?.running) { window.location.assign('/login'); return }
        setProblem(started?.error || 'ไม่ทราบสาเหตุ')
        setPhase('choose')
      })
      .catch(() => { if (alive) { setProblem('เริ่มเซิร์ฟเวอร์ในตัวแอปไม่สำเร็จ'); setPhase('choose') } })
    return () => { alive = false }
  }, [local, attempt])

  const retry = () => { setProblem(''); setPhase('starting'); setAttempt((n) => n + 1) }

  return (
    <main className="grid min-h-[var(--vh100)] place-items-center bg-canvas px-4 py-12">
      <div className="w-full max-w-md">
        <div className="text-center">
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-xl bg-accent text-on-accent" aria-hidden="true">
            <i className="ti ti-server text-2xl" />
          </span>
          <BrandName className="mt-3 block text-lg font-semibold text-ink" />
        </div>

        {phase === 'starting' ? (
          <LoadingState className="mt-8" title="กำลังเตรียมเซิร์ฟเวอร์ในแอป" hint="สักครู่ แล้วจะเปิดหน้าเชื่อมต่อ SQL Server" />
        ) : (
          <div className="mt-8 space-y-4">
            <h1 className="text-center text-2xl font-semibold text-ink">เชื่อมต่อกับระบบของบริษัท</h1>
            {problem && (
              <Alert tone="error">
                <p className="font-medium">เริ่มเซิร์ฟเวอร์ในแอปไม่สำเร็จ</p>
                <p className="mt-0.5 break-all text-[13px]">{problem}</p>
              </Alert>
            )}
            {local && !available && (
              <Alert tone="info">ตัวติดตั้งนี้ไม่มีเซิร์ฟเวอร์ในตัวแอป ใช้ได้เฉพาะเซิร์ฟเวอร์ Helpdesk ของบริษัท</Alert>
            )}
            <section className="rounded-card border border-line bg-card p-5 shadow-card">
              <i className="ti ti-building text-2xl text-accent" aria-hidden="true" />
              <h2 className="mt-3 text-base font-semibold text-ink">เซิร์ฟเวอร์ Helpdesk ของบริษัท</h2>
              <p className="mt-1 text-[13px] text-ink-2">ใส่ที่อยู่ที่ฝ่าย IT ให้ (เช่น http://helpdesk.company.local:8080) ฐานข้อมูลถูกตั้งค่าไว้ที่เซิร์ฟเวอร์นั้นแล้ว</p>
              <Button variant="primary" className="mt-4 w-full" onClick={() => setCompanyOpen(true)}>ใส่ที่อยู่เซิร์ฟเวอร์</Button>
            </section>
            {available && (
              <Button className="w-full" icon="refresh" onClick={retry}>ลองเริ่มเซิร์ฟเวอร์ในแอปอีกครั้ง</Button>
            )}
          </div>
        )}
      </div>
      {companyOpen && <ServerAddressDialog open onClose={() => setCompanyOpen(false)} />}
    </main>
  )
}
