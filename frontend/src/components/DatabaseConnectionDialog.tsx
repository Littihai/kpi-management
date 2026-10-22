import { useState } from 'react'
import DatabaseConnectionForm from './DatabaseConnectionForm'
import Modal from './ui/Modal'
import { Alert } from './ui/Layout'
import { useSetupStore } from '../stores/setupStore'

/** The database connection, from the sign-in page: the form while the server waits to be connected, otherwise where to change it. */
export default function DatabaseConnectionDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const setup = useSetupStore((s) => s.status)
  const server = useSetupStore((s) => s.server)
  // while it is setting up, a click outside or Escape must not throw the progress (and the errors) away
  const [busy, setBusy] = useState(false)

  return (
    <Modal open={open} onClose={busy ? () => undefined : onClose} width="lg" title="เชื่อมต่อฐานข้อมูล">
      {setup
        ? <DatabaseConnectionForm status={setup} onBusyChange={setBusy} />
        : (
          <div className="space-y-3">
            {server === 'running'
              ? <Alert tone="success">เซิร์ฟเวอร์ทำงานปกติและเชื่อมต่อฐานข้อมูลแล้ว</Alert>
              : <Alert tone="info">ยังตรวจสถานะเซิร์ฟเวอร์ไม่ได้ (อาจกำลังเริ่มทำงาน) ลองใหม่ในอีกสักครู่</Alert>}
            <p className="text-sm text-ink-2">
              ถ้าต้องการดูสถานะ ตรวจว่าขาดตารางอะไร หรือซ่อมฐานข้อมูล ให้เข้าสู่ระบบด้วยบัญชีผู้ดูแลแล้วไปที่ ตั้งค่า → ฐานข้อมูล
            </p>
          </div>
        )}
    </Modal>
  )
}
