import { useState } from 'react'
import BrandName from '../components/BrandName'
import ServerAddressDialog from '../components/ServerAddressDialog'
import Button from '../components/ui/Button'

/**
 * The Windows app has no server of its own and none is built in: no company's system is the default. Until the person (or the
 * company's IT, through server.txt / HELPDESK_SERVER_URL) says which server to use, this is all there is. Two ways in: a company's
 * server (its address), or the server on this PC, whose SQL Server the person then sets up (Server Name, login, password) themselves.
 */
export default function ChooseServerPage() {
  const [open, setOpen] = useState<'company' | 'local' | null>(null)

  const choices = [
    {
      id: 'company' as const, icon: 'building', title: 'เซิร์ฟเวอร์ Helpdesk ของบริษัท',
      text: 'ใส่ที่อยู่ที่ฝ่าย IT ให้ (เช่น http://helpdesk.company.local:8080) ฐานข้อมูลถูกตั้งค่าไว้ที่เซิร์ฟเวอร์นั้นแล้ว',
      button: 'ใส่ที่อยู่เซิร์ฟเวอร์',
    },
    {
      id: 'local' as const, icon: 'database-cog', title: 'ตั้งค่า SQL Server เอง',
      text: 'เมื่อติดตั้งเซิร์ฟเวอร์ Helpdesk ไว้ในเครื่องนี้ แล้วต้องการกรอก Server Name ชื่อผู้ใช้ และรหัสผ่านของ SQL Server เอง',
      button: 'เชื่อมต่อ SQL Server',
    },
  ]

  return (
    <main className="grid min-h-[var(--vh100)] place-items-center bg-canvas px-4 py-12">
      <div className="w-full max-w-2xl">
        <div className="text-center">
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-xl bg-accent text-on-accent" aria-hidden="true">
            <i className="ti ti-server text-2xl" />
          </span>
          <BrandName className="mt-3 block text-lg font-semibold text-ink" />
          <h1 className="mt-4 text-2xl font-semibold text-ink">เชื่อมต่อกับระบบของบริษัท</h1>
          <p className="mt-1 text-sm text-ink-3">แอปนี้ยังไม่ได้เชื่อมต่อกับเซิร์ฟเวอร์หรือฐานข้อมูลใดเลย เลือกวิธีเชื่อมต่อ</p>
        </div>

        <div className="mt-8 grid gap-4 sm:grid-cols-2">
          {choices.map((c) => (
            <section key={c.id} className="flex flex-col rounded-card border border-line bg-card p-5 shadow-card">
              <i className={`ti ti-${c.icon} text-2xl text-accent`} aria-hidden="true" />
              <h2 className="mt-3 text-base font-semibold text-ink">{c.title}</h2>
              <p className="mt-1 flex-1 text-[13px] text-ink-2">{c.text}</p>
              <Button variant="primary" className="mt-4 w-full" onClick={() => setOpen(c.id)}>{c.button}</Button>
            </section>
          ))}
        </div>
      </div>
      {open && <ServerAddressDialog open preset={open === 'local' ? 'local' : undefined} onClose={() => setOpen(null)} />}
    </main>
  )
}
