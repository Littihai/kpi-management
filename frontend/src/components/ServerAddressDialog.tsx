import { useState } from 'react'
import Button from './ui/Button'
import { TextInput } from './ui/Field'
import { Alert } from './ui/Layout'
import Modal from './ui/Modal'
import {
  builtInServerUrl, currentServerUrl, describeServer, getServerOverride, isUnencrypted, normalizeServerUrl, probeServer, sameServer, switchServer,
  type ProbeResult,
} from '../lib/server'
import { useAuthStore } from '../stores/authStore'

const reasons: Record<Exclude<ProbeResult, { ok: true }>['reason'], string> = {
  timeout: 'เซิร์ฟเวอร์ไม่ตอบภายใน 20 วินาที ถ้าเป็นเซิร์ฟเวอร์แบบฟรีอาจกำลังตื่น ลองอีกครั้ง',
  unreachable: 'เชื่อมต่อไม่ได้ ตรวจที่อยู่และเครือข่าย หรือเซิร์ฟเวอร์ยังไม่อนุญาตให้แอปนี้เข้าถึง (ให้ผู้ดูแลเซิร์ฟเวอร์ตรวจค่า Cors)',
  status: 'เซิร์ฟเวอร์ตอบกลับด้วยข้อผิดพลาด',
  'not-helpdesk': 'ที่อยู่นี้ตอบกลับ แต่ไม่ใช่เซิร์ฟเวอร์ Helpdesk',
}

/** Pick the server this app talks to: type an address, test it, then use it. The sign-in is dropped when it changes. */
export default function ServerAddressDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const signedIn = useAuthStore((s) => s.isAuthenticated)
  const current = currentServerUrl()
  const builtIn = builtInServerUrl()
  const [text, setText] = useState(getServerOverride() ?? '')
  const [testing, setTesting] = useState(false)
  const [error, setError] = useState('')
  const [probe, setProbe] = useState<{ url: string; result: ProbeResult } | null>(null)

  const parsed = normalizeServerUrl(text)
  const tested = parsed.ok && probe?.url === parsed.url ? probe.result : null
  const usingBuiltIn = getServerOverride() === null

  const test = async () => {
    setError('')
    if (!parsed.ok) { setError(parsed.error); return }
    setTesting(true)
    setProbe({ url: parsed.url, result: await probeServer(parsed.url) })
    setTesting(false)
  }

  const use = () => {
    if (!parsed.ok) return
    if (sameServer(parsed.url, current)) { onClose(); return }
    switchServer(parsed.url)
  }

  return (
    <Modal open={open} onClose={onClose} title="เซิร์ฟเวอร์ที่ใช้เชื่อมต่อ"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" icon="plug-connected" disabled={!tested?.ok} onClick={use}>ใช้เซิร์ฟเวอร์นี้</Button>
      </>}>
      <div className="space-y-4">
        <p className="text-sm text-ink-2">
          ใช้เมื่อบริษัทของคุณติดตั้ง Helpdesk ไว้ที่เซิร์ฟเวอร์ของตัวเอง ถามที่อยู่จากฝ่าย IT
        </p>
        <dl className="rounded-control border border-line bg-surface px-3.5 py-2.5 text-[13px]">
          <div className="flex justify-between gap-4"><dt className="text-ink-3">กำลังใช้</dt><dd className="min-w-0 break-all text-right font-medium text-ink">{describeServer(current)}</dd></div>
          {!usingBuiltIn && <div className="mt-1 flex justify-between gap-4"><dt className="text-ink-3">ค่าเริ่มต้นของแอป</dt><dd className="min-w-0 break-all text-right text-ink-2">{describeServer(builtIn)}</dd></div>}
        </dl>

        <div className="flex items-end gap-2">
          <TextInput label="ที่อยู่เซิร์ฟเวอร์" wrapperClassName="flex-1" placeholder="http://helpdesk.company.local:8080" autoComplete="off" inputMode="url"
            value={text} error={error || undefined}
            onChange={(e) => { setText(e.target.value); setError('') }}
            onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void test() } }} />
          <Button icon="plug" loading={testing} disabled={!text.trim()} onClick={test}>ทดสอบ</Button>
        </div>

        {tested?.ok && <Alert tone="success">เชื่อมต่อได้ ตอบกลับใน {tested.ms} มิลลิวินาที</Alert>}
        {tested && !tested.ok && <Alert tone="error">{reasons[tested.reason]}{tested.status ? ` (รหัส ${tested.status})` : ''}</Alert>}
        {parsed.ok && isUnencrypted(parsed.url) && (
          <Alert tone="warning">ที่อยู่แบบ http ไม่เข้ารหัสข้อมูลระหว่างแอปกับเซิร์ฟเวอร์ รวมถึงรหัสผ่าน ควรใช้ในเครือข่ายภายในที่เชื่อถือได้เท่านั้น</Alert>
        )}
        {signedIn && <p className="text-xs text-ink-3">เมื่อเปลี่ยนเซิร์ฟเวอร์ ระบบจะออกจากระบบและกลับไปหน้าเข้าสู่ระบบ</p>}

        {!usingBuiltIn && (
          <button type="button" onClick={() => switchServer(null)} className="text-[13px] font-medium text-accent hover:underline">
            กลับไปใช้ค่าเริ่มต้น ({describeServer(builtIn)})
          </button>
        )}
      </div>
    </Modal>
  )
}
