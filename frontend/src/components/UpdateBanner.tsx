import { useEffect, useState } from 'react'
import Button from './ui/Button'
import api from '../services/api'
import { useDesktopUpdate } from '../lib/desktop'
import { isOlderThan } from '../lib/versionCompare'

/**
 * Desktop app only. Shows (1) a full-screen block when the installed version is older than the server's minimum,
 * and (2) a slim bar once a new version has been downloaded. Renders nothing in a normal browser.
 */
export default function UpdateBanner() {
  const desktop = window.desktop
  const update = useDesktopUpdate()
  const [tooOld, setTooOld] = useState(false)
  const [dismissed, setDismissed] = useState(false)

  useEffect(() => {
    if (!desktop) return
    const ctrl = new AbortController()
    fetch(`${String(api.defaults.baseURL ?? '')}/api/client-config`, { signal: ctrl.signal, cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((cfg) => { if (cfg && isOlderThan(desktop.version, cfg.minDesktopVersion)) setTooOld(true) })
      .catch(() => { /* cannot reach the server: do nothing */ })
    return () => ctrl.abort()
  }, [desktop])

  if (!desktop) return null

  const ready = update?.status === 'downloaded'

  if (tooOld) {
    return (
      <div role="alertdialog" aria-modal="true" aria-labelledby="too-old-title"
        className="fixed inset-0 z-[100] flex items-center justify-center bg-surface p-6">
        <div className="w-full max-w-md space-y-4 rounded-card border border-line bg-card p-8 text-center shadow-card">
          <i className="ti ti-cloud-download text-4xl text-accent-ink" aria-hidden="true" />
          <h1 id="too-old-title" className="text-lg font-semibold text-ink">แอปเวอร์ชันนี้เก่าเกินไป กรุณาอัปเดต</h1>
          <p className="text-[13px] text-ink-3">เวอร์ชันที่ติดตั้ง {desktop.version} — ต้องอัปเดตก่อนจึงจะใช้งานต่อได้</p>
          {ready ? (
            <Button variant="primary" icon="refresh" onClick={() => desktop.restartToUpdate()}>รีสตาร์ตเพื่ออัปเดต</Button>
          ) : (
            <Button variant="primary" icon="refresh" loading={update?.status === 'checking' || update?.status === 'downloading'}
              onClick={() => { void desktop.checkForUpdates() }}>
              {update?.status === 'downloading' ? `กำลังดาวน์โหลด ${update.percent ?? 0}%` : 'ตรวจหาอัปเดต'}
            </Button>
          )}
          {update?.status === 'error' && (
            <p className="text-[13px] text-ink-3">ตรวจหาอัปเดตไม่สำเร็จ ตรวจสอบอินเทอร์เน็ตแล้วลองอีกครั้ง</p>
          )}
        </div>
      </div>
    )
  }

  if (!ready || dismissed) return null

  return (
    <div role="status"
      className="fixed inset-x-0 bottom-0 z-[90] flex flex-wrap items-center justify-center gap-x-4 gap-y-1 border-t border-line bg-deep px-4 py-2 text-on-deep">
      <span className="text-[13px] font-medium">มีเวอร์ชันใหม่ พร้อมอัปเดต</span>
      <Button size="sm" variant="primary" icon="refresh" onClick={() => desktop.restartToUpdate()}>รีสตาร์ตเพื่ออัปเดต</Button>
      <button type="button" onClick={() => setDismissed(true)} className="text-xs underline opacity-80 hover:opacity-100">ไว้ภายหลัง</button>
      <span className="text-[11px] opacity-70">หากปิดแถบนี้ แอปจะอัปเดตให้เองเมื่อคุณปิดโปรแกรม</span>
    </div>
  )
}
