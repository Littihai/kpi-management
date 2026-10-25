import { useEffect, useState } from 'react'
import DatabaseConnectionForm from './DatabaseConnectionForm'
import Button from './ui/Button'
import Modal from './ui/Modal'
import { Alert } from './ui/Layout'
import { DatabaseFacts } from './settings/DatabaseChecklist'
import { useOpenSettings } from './settings/settingsNav'
import { clearHistory, describeEntry, forgetConnection, useConnectionHistory, type ConnectionEntry } from '../lib/connectionHistory'
import { currentServerUrl, describeServer } from '../lib/server'
import { databaseService, type DatabaseReport } from '../services/database.service'
import { useAuthStore, useCan } from '../stores/authStore'
import { useSetupStore } from '../stores/setupStore'
import { formatDateTime } from '../utils/format'

type Tab = 'history' | 'connect'

/**
 * The connect window (after SSMS's): while the server waits for its database, two tabs, "History" (where this device connected
 * before; a click fills the form, never a password) and the connection form itself. Once the server runs, it shows where
 * it is connected to and sends an administrator to Settings > Database to check or repair it.
 */
export default function DatabaseConnectionDialog({ open, onClose, tab: firstTab = 'connect', prefill: firstPrefill }: {
  open: boolean
  onClose: () => void
  tab?: Tab
  prefill?: ConnectionEntry
}) {
  const setup = useSetupStore((s) => s.status)
  // while it is setting up, a click outside or Escape must not throw the progress (and the errors) away
  const [busy, setBusy] = useState(false)
  const [tab, setTab] = useState<Tab>(firstTab)
  const [prefill, setPrefill] = useState<ConnectionEntry | undefined>(firstPrefill)
  const [formKey, setFormKey] = useState(0)

  const use = (entry: ConnectionEntry) => {
    setPrefill(entry)
    setFormKey((k) => k + 1) // the form reads its starting values once, so a new entry means a new form
    setTab('connect')
  }

  return (
    <Modal open={open} onClose={busy ? () => undefined : onClose} width="lg" title="เชื่อมต่อฐานข้อมูล">
      {setup ? (
        <div className="space-y-4">
          <div role="tablist" aria-label="วิธีเลือกการเชื่อมต่อ" className="flex gap-1 border-b border-line">
            {([['history', 'ประวัติ', 'history'], ['connect', 'การเชื่อมต่อ', 'plug-connected']] as const).map(([id, label, icon]) => (
              <button key={id} type="button" role="tab" id={`connect-tab-${id}`} aria-selected={tab === id} aria-controls={`connect-panel-${id}`}
                disabled={busy} onClick={() => setTab(id)}
                className={`-mb-px flex items-center gap-2 border-b-2 px-4 py-2 text-[13px] font-semibold transition-colors disabled:opacity-50
                  ${tab === id ? 'border-accent text-accent-ink' : 'border-transparent text-ink-3 hover:text-ink'}`}>
                <i className={`ti ti-${icon} text-base`} aria-hidden="true" />{label}
              </button>
            ))}
          </div>
          {tab === 'history'
            ? <div role="tabpanel" id="connect-panel-history" aria-labelledby="connect-tab-history"><HistoryList onUse={use} /></div>
            : <div role="tabpanel" id="connect-panel-connect" aria-labelledby="connect-tab-connect">
                <DatabaseConnectionForm key={formKey} status={setup} onBusyChange={setBusy} prefill={prefill} />
              </div>}
        </div>
      ) : <RunningInfo onClose={onClose} />}
    </Modal>
  )
}

function HistoryList({ onUse }: { onUse: (entry: ConnectionEntry) => void }) {
  const history = useConnectionHistory()
  if (history.length === 0) {
    return (
      <div className="px-2 py-8 text-center">
        <i className="ti ti-history text-3xl text-ink-4" aria-hidden="true" />
        <p className="mt-2 text-sm font-medium text-ink">ยังไม่มีประวัติการเชื่อมต่อ</p>
        <p className="mt-0.5 text-xs text-ink-3">เซิร์ฟเวอร์ที่เชื่อมต่อสำเร็จจะมาอยู่ที่นี่ เพื่อเลือกใช้ซ้ำโดยไม่ต้องพิมพ์ใหม่</p>
      </div>
    )
  }
  return (
    <div className="space-y-2">
      <ul className="divide-y divide-line overflow-hidden rounded-control border border-line">
        {history.map((entry) => (
          <li key={`${entry.server}|${entry.database}|${entry.auth}|${entry.user ?? ''}`} className="flex items-center gap-1 pr-2 hover:bg-surface">
            <button type="button" onClick={() => onUse(entry)} className="flex min-w-0 flex-1 items-center gap-3 px-3.5 py-2.5 text-left">
              <i className="ti ti-database text-lg text-accent" aria-hidden="true" />
              <span className="min-w-0">
                <span className="block truncate text-sm font-medium text-ink">{entry.server}</span>
                <span className="block truncate text-xs text-ink-3">{describeEntry(entry)}{entry.at > 0 ? ` · ใช้ล่าสุด ${formatDateTime(new Date(entry.at))}` : ''}</span>
              </span>
            </button>
            <button type="button" onClick={() => forgetConnection(entry)} aria-label={`ลบ ${entry.server} ออกจากประวัติ`} title="ลบออกจากประวัติ"
              className="flex h-8 w-8 flex-none items-center justify-center rounded-control text-ink-3 hover:bg-surface-2 hover:text-ink">
              <i className="ti ti-x" aria-hidden="true" />
            </button>
          </li>
        ))}
      </ul>
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs text-ink-3">เก็บเฉพาะที่อยู่ ชื่อฐานข้อมูล และชื่อผู้ใช้ ไม่เก็บรหัสผ่าน</p>
        <Button size="sm" variant="ghost" icon="trash" onClick={clearHistory}>ล้างประวัติ</Button>
      </div>
    </div>
  )
}

/** The server is running: where the app is connected to, and for an administrator the database's facts. */
function RunningInfo({ onClose }: { onClose: () => void }) {
  const server = useSetupStore((s) => s.server)
  const signedIn = useAuthStore((s) => s.isAuthenticated)
  const canView = useCan('settings.view')
  const openSettings = useOpenSettings()
  const [report, setReport] = useState<DatabaseReport | null>(null)
  const [failed, setFailed] = useState(false)

  // the shared API client signs a person out on a 401, so it is only asked by someone who is signed in and may look
  const mayAsk = signedIn && canView
  useEffect(() => {
    if (!mayAsk) return
    let alive = true
    databaseService.status().then((r) => { if (alive) setReport(r) }).catch(() => { if (alive) setFailed(true) })
    return () => { alive = false }
  }, [mayAsk])

  return (
    <div className="space-y-4">
      {server === 'running'
        ? <Alert tone="success">เซิร์ฟเวอร์ทำงานปกติและเชื่อมต่อฐานข้อมูลแล้ว</Alert>
        : <Alert tone="info">ยังตรวจสถานะเซิร์ฟเวอร์ไม่ได้ (อาจกำลังเริ่มทำงาน) ลองใหม่ในอีกสักครู่</Alert>}

      <dl className="divide-y divide-line rounded-control border border-line text-[13px]">
        <div className="flex justify-between gap-4 px-4 py-2.5">
          <dt className="shrink-0 text-ink-3">เซิร์ฟเวอร์ที่แอปเชื่อมต่อ</dt>
          <dd className="min-w-0 truncate text-right font-medium text-ink">{describeServer(currentServerUrl())}</dd>
        </div>
      </dl>

      {report && <div className="rounded-control border border-line"><DatabaseFacts report={report} /></div>}
      {failed && <Alert tone="warning">อ่านข้อมูลฐานข้อมูลไม่สำเร็จ ลองเปิดหน้าตรวจฐานข้อมูลเพื่อดูสาเหตุ</Alert>}
      {!signedIn && <p className="text-sm text-ink-2">ดูรายละเอียดฐานข้อมูลได้หลังเข้าสู่ระบบด้วยบัญชีผู้ดูแล การเปลี่ยนฐานข้อมูลทำได้เฉพาะตอนที่เซิร์ฟเวอร์ยังไม่ได้เชื่อมต่อฐานข้อมูล</p>}
      {signedIn && !canView && <p className="text-sm text-ink-2">ดูรายละเอียดฐานข้อมูลต้องมีสิทธิ์ "ดูการตั้งค่าระบบ"</p>}

      {mayAsk && (
        <div className="flex justify-end">
          <Button variant="primary" icon="database-cog" onClick={() => { onClose(); openSettings('database') }}>เปิดหน้าตรวจฐานข้อมูล</Button>
        </div>
      )}
    </div>
  )
}
