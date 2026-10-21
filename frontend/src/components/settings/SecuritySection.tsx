import { useCallback, useEffect, useState } from 'react'
import Badge from '../ui/Badge'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { Alert, EmptyState, Pagination, Segmented, Skeleton } from '../ui/Layout'
import { CompactSelect } from '../ui/Field'
import { authLogService, type AuthLogRow } from '../../services/authLog.service'
import { settingsService, type SettingRow } from '../../services/settings.service'
import { useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { formatDateTime } from '../../utils/format'
import { usePageSize } from '../../utils/paging'
import type { Paged } from '../../types/issue'

type Tab = 'policy' | 'logs'

/** Password rules, lock-out and session length; and the log of who signed in. */
export default function SecuritySection() {
  const canLogs = useCan('audit.view')
  const [tab, setTab] = useState<Tab>('policy')
  return (
    <div className="space-y-4">
      {canLogs && (
        <Segmented<Tab> label="มุมมอง" value={tab} onChange={setTab}
          options={[{ value: 'policy', label: 'นโยบายรหัสผ่านและการเข้าสู่ระบบ' }, { value: 'logs', label: 'บันทึกการเข้าสู่ระบบ' }]} />
      )}
      {tab === 'logs' && canLogs ? <AuthLogsTab /> : <PolicyTab />}
    </div>
  )
}

// ───────────── policy ─────────────

const GROUPS: { title: string; eyebrow: string; keys: string[] }[] = [
  {
    eyebrow: 'รหัสผ่าน', title: 'ความยาวและความซับซ้อน',
    keys: ['security.password.minLength', 'security.password.requireUpper', 'security.password.requireLower', 'security.password.requireDigit', 'security.password.requireSpecial'],
  },
  {
    eyebrow: 'รหัสผ่าน', title: 'อายุและการใช้ซ้ำ',
    keys: ['security.password.maxAgeDays', 'security.password.historyCount', 'security.forceChangeOnAdminSet'],
  },
  {
    eyebrow: 'การเข้าสู่ระบบ', title: 'การล็อกบัญชีและอายุการเข้าสู่ระบบ',
    keys: ['security.login.maxFailures', 'security.login.lockMinutes', 'security.sessionDays'],
  },
]

function PolicyTab() {
  const canManage = useCan('settings.manage')
  const [rows, setRows] = useState<Record<string, SettingRow> | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    settingsService.list()
      .then((list) => setRows(Object.fromEntries(list.filter((r) => r.key.startsWith('security.')).map((r) => [r.key, r]))))
      .catch((err) => setError(apiError(err, 'โหลดการตั้งค่าไม่สำเร็จ')))
  }, [])

  if (!rows) return error ? <Alert tone="error">{error}</Alert> : <div className="space-y-3" role="status" aria-label="กำลังโหลด">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-24" />)}</div>

  const value = (key: string) => draft[key] ?? rows[key].value
  const changed = Object.keys(draft).filter((k) => draft[k] !== rows[k].value)
  const set = (key: string, v: string) => {
    setNotice('')
    setDraft((d) => {
      const next = { ...d }
      if (v === rows[key].value) delete next[key]; else next[key] = v
      return next
    })
  }

  const save = async () => {
    setSaving(true)
    setError('')
    setNotice('')
    const failures: string[] = []
    let saved = 0
    for (const key of changed) {
      try {
        const updated = await settingsService.update(key, draft[key])
        setRows((r) => (r ? { ...r, [key]: updated } : r))
        setDraft((d) => { const next = { ...d }; delete next[key]; return next })
        saved++
      } catch (err) {
        failures.push(`${rows[key].label}: ${apiError(err, 'บันทึกไม่สำเร็จ')}`)
      }
    }
    setSaving(false)
    if (failures.length) setError(failures.join(' · '))
    if (saved) setNotice(`บันทึกแล้ว ${saved} รายการ มีผลทันที`)
  }

  const num = (key: string) => Number(value(key))

  return (
    <div className="space-y-5">
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      {notice && <Alert tone="success" onClose={() => setNotice('')}>{notice}</Alert>}
      {!canManage && <Alert tone="info">คุณดูการตั้งค่าได้อย่างเดียว การแก้ไขต้องมีสิทธิ์ "แก้ไขการตั้งค่าระบบ"</Alert>}
      {num('security.password.maxAgeDays') > 0 && rows['security.password.maxAgeDays'].value === '0' && (
        <Alert tone="warning">เมื่อเปิดให้รหัสผ่านหมดอายุ ผู้ที่ไม่เคยเปลี่ยนรหัสและสร้างบัญชีนานเกินกำหนด จะถูกบังคับเปลี่ยนรหัสทันทีที่ใช้งานครั้งถัดไป</Alert>
      )}
      {num('security.login.maxFailures') > 0 && rows['security.login.maxFailures'].value === '0' && (
        <Alert tone="warning">การล็อกบัญชีทำให้ผู้อื่นที่เดารหัสผิดซ้ำ ๆ ล็อกบัญชีของเจ้าของได้ ผู้ดูแลปลดล็อกได้จากหน้า "ผู้ใช้งาน"</Alert>
      )}

      {GROUPS.map((g) => (
        <Card key={g.title}>
          <CardHeader eyebrow={g.eyebrow} title={g.title} />
          <ul className="divide-y divide-line pt-3">
            {g.keys.map((key) => {
              const r = rows[key]
              const v = value(key)
              const isChanged = draft[key] !== undefined
              return (
                <li key={key} className="flex flex-wrap items-start gap-x-6 gap-y-3 px-5 py-3.5">
                  <div className="min-w-0 flex-1 basis-64">
                    <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-ink">
                      {r.label}
                      {isChanged && <Badge tone="amber">ยังไม่บันทึก</Badge>}
                    </p>
                    <p className="mt-0.5 text-xs text-ink-3">{r.description}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    {r.kind === 'bool' ? (
                      <label className="flex items-center gap-2 text-[13px] text-ink">
                        <input type="checkbox" className="h-4 w-4 accent-[var(--lt-accent)]" disabled={!canManage || saving}
                          checked={v === 'true'} onChange={(e) => set(key, e.target.checked ? 'true' : 'false')} />
                        {v === 'true' ? 'เปิด' : 'ปิด'}
                      </label>
                    ) : (
                      <label className="flex items-center gap-2 text-[13px] text-ink-2">
                        <input type="number" inputMode="numeric" min={r.min ?? undefined} max={r.max ?? undefined} step="1" value={v}
                          disabled={!canManage || saving} aria-label={r.label} onChange={(e) => set(key, e.target.value)}
                          className={`h-9 w-24 rounded-control border bg-card px-3 text-right text-[13px] tabular ${isChanged ? 'border-accent' : 'border-line-strong'}`} />
                        {r.unit}
                      </label>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        </Card>
      ))}

      {canManage && (
        <div className="flex items-center justify-end gap-2">
          {changed.length > 0 && <Button variant="ghost" onClick={() => setDraft({})} disabled={saving}>ยกเลิกที่แก้</Button>}
          <Button variant="primary" icon="device-floppy" loading={saving} disabled={changed.length === 0} onClick={save}>
            บันทึก{changed.length > 0 ? ` (${changed.length})` : ''}
          </Button>
        </div>
      )}
    </div>
  )
}

// ───────────── sign-in log ─────────────

const eventLabel: Record<string, string> = {
  login_success: 'เข้าสู่ระบบ',
  login_failed: 'เข้าสู่ระบบไม่สำเร็จ',
  account_locked: 'บัญชีถูกล็อก',
  account_unlocked: 'ปลดล็อกบัญชี',
  password_changed: 'เปลี่ยนรหัสผ่าน (เอง)',
  password_reset: 'ตั้งรหัสผ่านใหม่ (โดยผู้ดูแล)',
}
const reasonLabel: Record<string, string> = {
  unknown_user: 'ไม่พบบัญชีนี้',
  bad_password: 'รหัสผ่านไม่ถูกต้อง',
  inactive: 'บัญชีถูกปิดใช้งาน',
  locked: 'บัญชีถูกล็อกอยู่',
  too_many_failures: 'ผิดติดกันเกินกำหนด',
}

function AuthLogsTab() {
  const [pageSize, setPageSize] = usePageSize()
  const [events, setEvents] = useState<string[]>([])
  const [data, setData] = useState<Paged<AuthLogRow> | null>(null)
  const [error, setError] = useState('')
  const [searchText, setSearchText] = useState('')
  const [search, setSearch] = useState('')
  const [event, setEvent] = useState('')
  const [status, setStatus] = useState<'all' | 'ok' | 'fail'>('all')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(1)

  useEffect(() => { authLogService.events().then(setEvents).catch(() => {}) }, [])
  useEffect(() => {
    const id = window.setTimeout(() => { setSearch(searchText.trim()); setPage(1) }, 400)
    return () => window.clearTimeout(id)
  }, [searchText])

  const load = useCallback(() => {
    let cancelled = false
    authLogService.search({
      search: search || undefined, event: event || undefined,
      success: status === 'all' ? undefined : status === 'ok',
      from: from ? new Date(from + 'T00:00:00').toISOString() : undefined,
      // "to" is a whole day: everything before the next midnight
      to: to ? new Date(new Date(to + 'T00:00:00').getTime() + 86_400_000).toISOString() : undefined,
      page, pageSize,
    })
      .then((r) => { if (!cancelled) { setData(r); setError('') } })
      .catch((err) => { if (!cancelled) setError(apiError(err, 'โหลดบันทึกไม่สำเร็จ')) })
    return () => { cancelled = true }
  }, [search, event, status, from, to, page, pageSize])
  useEffect(() => load(), [load])

  const filtered = Boolean(search || event || status !== 'all' || from || to)
  const clear = () => { setSearchText(''); setSearch(''); setEvent(''); setStatus('all'); setFrom(''); setTo(''); setPage(1) }
  const pick = (setter: (v: string) => void) => (e: React.ChangeEvent<HTMLSelectElement | HTMLInputElement>) => { setter(e.target.value); setPage(1) }
  const dateCls = 'h-9 rounded-control border border-line-strong bg-card px-2.5 text-[13px]'

  return (
    <Card>
      <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-4 py-3">
        <div className="relative w-full sm:w-64">
          <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
          <input type="search" value={searchText} onChange={(e) => setSearchText(e.target.value)} aria-label="ค้นหาอีเมลหรือ IP" placeholder="ค้นหาอีเมลหรือ IP"
            className="h-9 w-full rounded-control border border-line-strong bg-card pl-9 pr-3 text-[13px]" />
        </div>
        <CompactSelect aria-label="กรองตามเหตุการณ์" value={event} onChange={pick(setEvent)}>
          <option value="">ทุกเหตุการณ์</option>
          {events.map((e) => <option key={e} value={e}>{eventLabel[e] ?? e}</option>)}
        </CompactSelect>
        <CompactSelect aria-label="กรองตามผล" value={status} onChange={(e) => { setStatus(e.target.value as typeof status); setPage(1) }}>
          <option value="all">ทุกผล</option>
          <option value="ok">สำเร็จ</option>
          <option value="fail">ไม่สำเร็จ</option>
        </CompactSelect>
        <label className="flex items-center gap-1.5 text-xs text-ink-3">ตั้งแต่ <input type="date" className={dateCls} value={from} onChange={pick(setFrom)} /></label>
        <label className="flex items-center gap-1.5 text-xs text-ink-3">ถึง <input type="date" className={dateCls} value={to} onChange={pick(setTo)} /></label>
        {filtered && <Button size="sm" variant="ghost" icon="x" onClick={clear}>ล้างตัวกรอง</Button>}
      </div>

      {error && <div className="p-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}
      {!data ? (
        !error && <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-10" />)}</div>
      ) : data.items.length === 0 ? (
        <EmptyState icon="shield-check" title="ไม่พบบันทึก">{filtered ? 'ลองเปลี่ยนหรือล้างตัวกรอง' : 'ยังไม่มีการเข้าสู่ระบบที่บันทึกไว้'}</EmptyState>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-left text-[13px]">
              <thead>
                <tr className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                  <th scope="col" className="px-5 py-3">เวลา</th>
                  <th scope="col" className="px-3 py-3">เหตุการณ์</th>
                  <th scope="col" className="px-3 py-3">อีเมล</th>
                  <th scope="col" className="px-3 py-3">เครื่อง (IP)</th>
                  <th scope="col" className="px-3 py-3">ผล</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {data.items.map((r) => (
                  <tr key={r.id}>
                    <td className="whitespace-nowrap px-5 py-3 tabular text-ink-2">{formatDateTime(r.at)}</td>
                    <td className="px-3 py-3 font-medium text-ink">{eventLabel[r.event] ?? r.event}</td>
                    <td className="max-w-[220px] truncate px-3 py-3 text-ink-2" title={r.email ?? ''}>{r.email ?? '—'}</td>
                    <td className="px-3 py-3 text-ink-2" title={r.userAgent ?? ''}>{r.ipAddress ?? '—'}</td>
                    <td className="px-3 py-3">
                      <Badge tone={r.success ? 'green' : 'red'} dot>{r.success ? 'สำเร็จ' : 'ไม่สำเร็จ'}</Badge>
                      {r.reason && <span className="ml-2 text-xs text-ink-3">{reasonLabel[r.reason] ?? r.reason}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onChange={setPage}
            onPageSizeChange={(size) => { setPageSize(size); setPage(1) }} />
        </>
      )}
    </Card>
  )
}
