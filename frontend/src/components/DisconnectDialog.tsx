import { useState } from 'react'
import Button from './ui/Button'
import Modal from './ui/Modal'
import { Alert } from './ui/Layout'

/**
 * "Disconnect" for the Windows app's built-in server: forget where the database is on this PC and go back to the connect form, to connect
 * another server. The data in the database is not touched; only what this PC remembers (server, user name, the saved password) is.
 */
export default function DisconnectDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const disconnect = async () => {
    const server = window.desktop?.localServer
    if (!server) return
    setBusy(true)
    setError('')
    try {
      const status = await server.resetDatabase()
      if (!status?.running) { setError(status?.error || 'เริ่มเซิร์ฟเวอร์ในตัวแอปใหม่ไม่สำเร็จ'); setBusy(false); return }
      // the sign-in belonged to the connection that is gone; the page reloads because the server has a new address
      try { localStorage.removeItem('token'); localStorage.removeItem('user') } catch { /* the reload below still lands on the sign-in page */ }
      window.location.href = '/login'
    } catch {
      setError('ยกเลิกการเชื่อมต่อไม่สำเร็จ')
      setBusy(false)
    }
  }

  return (
    <Modal open={open} onClose={busy ? () => undefined : onClose} width="sm" title="ยกเลิกการเชื่อมต่อฐานข้อมูล?"
      footer={<>
        <Button onClick={onClose} disabled={busy}>ไม่ยกเลิก</Button>
        <Button variant="danger" icon="plug-x" loading={busy} onClick={disconnect}>ยกเลิกการเชื่อมต่อ</Button>
      </>}>
      <div className="space-y-3 text-sm text-ink-2">
        {error && <Alert tone="error">{error}</Alert>}
        <p>แอปจะลืมการเชื่อมต่อ SQL Server ของเครื่องนี้ (ที่อยู่เซิร์ฟเวอร์ ชื่อผู้ใช้ และรหัสผ่านที่บันทึกไว้) แล้วกลับไปหน้าเชื่อมต่อ เพื่อต่อเซิร์ฟเวอร์อื่น</p>
        <p>ข้อมูลในฐานข้อมูลเดิม <b className="text-ink">ไม่ถูกลบ</b> และจะเข้าสู่ระบบอีกครั้งเมื่อเชื่อมต่อ</p>
      </div>
    </Modal>
  )
}
