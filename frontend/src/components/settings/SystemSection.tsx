import { useCallback, useEffect, useMemo, useState } from 'react'
import Badge from '../ui/Badge'
import Button from '../ui/Button'
import { Card } from '../ui/Card'
import { Alert, EmptyState, Segmented, Skeleton } from '../ui/Layout'
import Modal from '../ui/Modal'
import { settingsService, type SettingHistoryRow, type SettingRow } from '../../services/settings.service'
import { useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { formatDateTime } from '../../utils/format'

const ALL = '__all__'

const display = (row: SettingRow, value: string) =>
  row.kind === 'bool' ? (value === 'true' ? 'เปิด' : 'ปิด') : `${value}${row.unit ? ' ' + row.unit : ''}`

/**
 * System settings: the numbers and switches an administrator may change without a release (SLA targets, limits,
 * e-mail on/off, retention). Changes apply at once; every change is kept in a history.
 */
export default function SystemSection() {
  const canManage = useCan('settings.manage')
  const [rows, setRows] = useState<SettingRow[] | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [category, setCategory] = useState(ALL)
  const [search, setSearch] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [history, setHistory] = useState<SettingRow | null>(null)

  const load = useCallback(() => {
    settingsService.list()
      // E-mail has its own screen (Settings > E-mail)
      .then((list) => { setRows(list.filter((r) => !r.key.startsWith('email.') && !r.key.startsWith('smtp.'))); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดการตั้งค่าไม่สำเร็จ')))
  }, [])
  useEffect(() => { load() }, [load])

  const categories = useMemo(() => [...new Set((rows ?? []).map((r) => r.category))], [rows])
  const shown = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (rows ?? []).filter((r) =>
      (category === ALL || r.category === category)
      && (!q || r.label.toLowerCase().includes(q) || r.description.toLowerCase().includes(q) || r.key.toLowerCase().includes(q)))
  }, [rows, category, search])

  const current = (r: SettingRow) => draft[r.key] ?? r.value
  const dirty = (rows ?? []).filter((r) => draft[r.key] !== undefined && draft[r.key] !== r.value)

  const setValue = (r: SettingRow, value: string) => {
    setNotice('')
    setDraft((d) => {
      const next = { ...d }
      if (value === r.value) delete next[r.key]; else next[r.key] = value
      return next
    })
  }

  const save = async () => {
    setSaving(true)
    setError('')
    setNotice('')
    const failures: string[] = []
    let saved = 0
    for (const r of dirty) {
      try {
        const updated = await settingsService.update(r.key, draft[r.key])
        setRows((list) => list?.map((x) => (x.key === r.key ? updated : x)) ?? list)
        setDraft((d) => { const next = { ...d }; delete next[r.key]; return next })
        saved++
      } catch (err) {
        failures.push(`${r.label}: ${apiError(err, 'บันทึกไม่สำเร็จ')}`)
      }
    }
    setSaving(false)
    if (failures.length) setError(failures.join(' · '))
    if (saved) setNotice(`บันทึกแล้ว ${saved} รายการ มีผลทันที`)
  }

  const reset = async (r: SettingRow) => {
    setSaving(true)
    setError('')
    try {
      const updated = await settingsService.reset(r.key)
      setRows((list) => list?.map((x) => (x.key === r.key ? updated : x)) ?? list)
      setDraft((d) => { const next = { ...d }; delete next[r.key]; return next })
      setNotice(`คืนค่าเริ่มต้นของ "${r.label}" แล้ว`)
    } catch (err) {
      setError(apiError(err, 'คืนค่าเริ่มต้นไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  if (!rows) {
    return error
      ? <Alert tone="error">{error}</Alert>
      : <div className="space-y-3" role="status" aria-label="กำลังโหลด">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-16" />)}</div>
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="relative w-full sm:w-72">
          <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="ค้นหาการตั้งค่า" placeholder="ค้นหาการตั้งค่า"
            className="h-9 w-full rounded-control border border-line-strong bg-card pl-9 pr-3 text-[13px]" />
        </div>
        {canManage && (
          <div className="flex items-center gap-2">
            {dirty.length > 0 && <Button variant="ghost" onClick={() => setDraft({})} disabled={saving}>ยกเลิกที่แก้</Button>}
            <Button variant="primary" icon="device-floppy" loading={saving} disabled={dirty.length === 0} onClick={save}>
              บันทึก{dirty.length > 0 ? ` (${dirty.length})` : ''}
            </Button>
          </div>
        )}
      </div>

      <Segmented<string> label="หมวดการตั้งค่า" value={category} onChange={setCategory}
        options={[{ value: ALL, label: 'ทั้งหมด' }, ...categories.map((c) => ({ value: c, label: c }))]} />

      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      {notice && <Alert tone="success" onClose={() => setNotice('')}>{notice}</Alert>}
      {!canManage && <Alert tone="info">คุณดูการตั้งค่าได้อย่างเดียว การแก้ไขต้องมีสิทธิ์ "แก้ไขการตั้งค่าระบบ"</Alert>}

      {shown.length === 0 ? (
        <Card><EmptyState icon="settings-off" title="ไม่พบการตั้งค่า">ลองเปลี่ยนคำค้นหาหรือหมวด</EmptyState></Card>
      ) : (
        (category === ALL ? categories : [category]).map((c) => {
          const items = shown.filter((r) => r.category === c)
          if (items.length === 0) return null
          return (
            <Card key={c}>
              <h3 className="border-b border-line px-5 py-3 text-[13px] font-bold text-ink-3">{c}</h3>
              <ul className="divide-y divide-line">
                {items.map((r) => {
                  const value = current(r)
                  const changed = draft[r.key] !== undefined
                  return (
                    <li key={r.key} className="flex flex-wrap items-start gap-x-6 gap-y-3 px-5 py-4">
                      <div className="min-w-0 flex-1 basis-64">
                        <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-ink">
                          {r.label}
                          {!r.isDefault && <Badge tone="blue">แก้ไขแล้ว</Badge>}
                          {changed && <Badge tone="amber">ยังไม่บันทึก</Badge>}
                        </p>
                        <p className="mt-0.5 text-xs text-ink-3">{r.description}</p>
                        <p className="mt-1 text-[11px] text-ink-3">
                          <span className="font-mono">{r.key}</span> · ค่าเริ่มต้น {display(r, r.default)}
                          {r.updatedAt && !r.isDefault ? ` · แก้ล่าสุด ${formatDateTime(r.updatedAt)}${r.updatedByName ? ' โดย ' + r.updatedByName : ''}` : ''}
                        </p>
                      </div>
                      <div className="flex items-center gap-2">
                        {r.kind === 'bool' ? (
                          <label className="flex items-center gap-2 text-[13px] text-ink">
                            <input type="checkbox" className="h-4 w-4 accent-[var(--lt-accent)]" disabled={!canManage || saving}
                              checked={value === 'true'} onChange={(e) => setValue(r, e.target.checked ? 'true' : 'false')} />
                            {value === 'true' ? 'เปิด' : 'ปิด'}
                          </label>
                        ) : (
                          <label className="flex items-center gap-2 text-[13px] text-ink-2">
                            <input type={r.kind === 'number' ? 'number' : 'text'} inputMode={r.kind === 'number' ? 'decimal' : undefined}
                              min={r.min ?? undefined} max={r.max ?? undefined} step="any" value={value} disabled={!canManage || saving}
                              aria-label={r.label} onChange={(e) => setValue(r, e.target.value)}
                              className={`h-9 w-28 rounded-control border bg-card px-3 text-right text-[13px] tabular ${changed ? 'border-accent' : 'border-line-strong'}`} />
                            {r.unit}
                          </label>
                        )}
                        <Button size="sm" variant="ghost" icon="history" title="ประวัติการเปลี่ยนค่า" aria-label={`ประวัติของ ${r.label}`} onClick={() => setHistory(r)} />
                        {canManage && !r.isDefault && (
                          <Button size="sm" variant="ghost" icon="restore" title="คืนค่าเริ่มต้น" aria-label={`คืนค่าเริ่มต้นของ ${r.label}`}
                            disabled={saving} onClick={() => reset(r)} />
                        )}
                      </div>
                    </li>
                  )
                })}
              </ul>
            </Card>
          )
        })
      )}

      {history && <HistoryDialog row={history} onClose={() => setHistory(null)} />}
    </div>
  )
}

function HistoryDialog({ row, onClose }: { row: SettingRow; onClose: () => void }) {
  const [items, setItems] = useState<SettingHistoryRow[] | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    settingsService.history(row.key).then(setItems).catch((err) => setError(apiError(err, 'โหลดประวัติไม่สำเร็จ')))
  }, [row.key])

  return (
    <Modal open onClose={onClose} title={`ประวัติ: ${row.label}`} footer={<Button onClick={onClose}>ปิด</Button>}>
      {error ? <Alert tone="error">{error}</Alert>
        : !items ? <Skeleton className="h-16" />
        : items.length === 0 ? <EmptyState icon="history" title="ยังไม่เคยเปลี่ยนค่า">ใช้ค่าเริ่มต้นมาตลอด</EmptyState>
        : (
          <ul className="divide-y divide-line text-[13px]">
            {items.map((h, i) => (
              <li key={i} className="py-2.5">
                <p className="text-ink"><span className="text-ink-3">{h.oldValue === null ? '—' : display(row, h.oldValue)}</span>
                  <i className="ti ti-arrow-right mx-2 text-ink-3" aria-hidden="true" />
                  <span className="font-medium">{h.newValue === null ? '—' : display(row, h.newValue)}</span></p>
                <p className="text-xs text-ink-3">{formatDateTime(h.changedAt)}{h.changedByName ? ' · ' + h.changedByName : ''}</p>
              </li>
            ))}
          </ul>
        )}
    </Modal>
  )
}
