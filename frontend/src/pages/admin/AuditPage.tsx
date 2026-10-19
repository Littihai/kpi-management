import { Fragment, useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { CompactSelect } from '../../components/ui/Field'
import { Alert, EmptyState, Page, PageHeader, Pagination, Segmented, Skeleton } from '../../components/ui/Layout'
import { adminService, type AuditRow, type RequestLogRow } from '../../services/admin.service'
import { apiError } from '../../utils/errors'
import { dayEndIso, dayStartIso, formatDateTime } from '../../utils/format'
import { usePageSize } from '../../utils/paging'
import { auditActionLabel, roleLabel } from '../../utils/labels'
import type { Paged } from '../../types/issue'

type Tab = 'audit' | 'requests'

function csvCell(value: unknown): string {
  const text = value == null ? '' : String(value)
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

function downloadCsv(name: string, header: string[], rows: unknown[][]) {
  // BOM so Excel opens Thai text correctly.
  const csv = '﻿' + [header, ...rows].map((r) => r.map(csvCell).join(',')).join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  URL.revokeObjectURL(url)
}

export default function AuditPage() {
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as Tab) || 'audit'
  const q = params.get('q') ?? ''
  const entity = params.get('entity') ?? ''
  const ip = params.get('ip') ?? ''
  const method = params.get('method') ?? ''
  const minStatus = params.get('minStatus') ?? ''
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const page = Number(params.get('page') ?? 1)
  const [pageSize, setPageSize] = usePageSize()

  const [audit, setAudit] = useState<Paged<AuditRow> | null>(null)
  const [requests, setRequests] = useState<Paged<RequestLogRow> | null>(null)
  const [error, setError] = useState('')
  const [loadedKey, setLoadedKey] = useState('')
  const [expanded, setExpanded] = useState<string | null>(null)
  const [searchText, setSearchText] = useState(q)
  const [ipText, setIpText] = useState(ip)

  const update = useCallback((changes: Record<string, string | null>) => {
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [key, value] of Object.entries(changes)) {
        if (value === null || value === '') next.delete(key)
        else next.set(key, value)
      }
      if (!('page' in changes)) next.delete('page')
      return next
    }, { replace: true })
  }, [setParams])

  // Rows belong to the filters they were fetched for; while those differ from the current ones we are loading.
  const key = JSON.stringify([tab, q, entity, ip, method, minStatus, from, to, page, pageSize])
  const loading = loadedKey !== key

  useEffect(() => {
    let cancelled = false
    const range = { from: from ? dayStartIso(from) : undefined, to: to ? dayEndIso(to) : undefined }
    const request = tab === 'audit'
      ? adminService.audit({ search: q || undefined, entity: entity || undefined, ip: ip || undefined, ...range, page, pageSize })
          .then((rows) => { if (!cancelled) setAudit(rows) })
      : adminService.requestLogs({
          search: q || undefined, ip: ip || undefined, method: method || undefined,
          minStatus: minStatus ? Number(minStatus) : undefined, ...range, page, pageSize,
        }).then((rows) => { if (!cancelled) setRequests(rows) })

    const forKey = JSON.stringify([tab, q, entity, ip, method, minStatus, from, to, page, pageSize])
    request
      .then(() => { if (!cancelled) setError('') })
      .catch((err) => { if (!cancelled) setError(apiError(err, 'โหลดบันทึกไม่สำเร็จ')) })
      .finally(() => { if (!cancelled) setLoadedKey(forKey) })
    return () => { cancelled = true }
  }, [tab, q, entity, ip, method, minStatus, from, to, page, pageSize])

  useEffect(() => {
    if (searchText === q && ipText === ip) return
    const id = window.setTimeout(() => update({ q: searchText.trim(), ip: ipText.trim() }), 400)
    return () => window.clearTimeout(id)
  }, [searchText, ipText, q, ip, update])

  const data = tab === 'audit' ? audit : requests
  const hasFilters = Boolean(q || entity || ip || method || minStatus || from || to)

  const exportCsv = () => {
    if (tab === 'audit' && audit) {
      downloadCsv(`audit-${Date.now()}.csv`,
        ['เวลา', 'ผู้ใช้', 'อีเมล', 'ตำแหน่ง', 'แผนก', 'การกระทำ', 'รายการ', 'รหัส', 'ค่าเดิม', 'ค่าใหม่', 'IP', 'อุปกรณ์'],
        audit.items.map((a) => [formatDateTime(a.createdAt), a.userName, a.userEmail, a.userRole && (roleLabel[a.userRole] ?? a.userRole),
          a.departmentName, auditActionLabel[a.action] ?? a.action, a.entityName, a.entityId, a.oldValues, a.newValues, a.ipAddress, a.userAgent]))
    } else if (requests) {
      downloadCsv(`requests-${Date.now()}.csv`,
        ['เวลา', 'ผู้ใช้', 'ตำแหน่ง', 'Method', 'Path', 'สถานะ', 'ms', 'IP', 'อุปกรณ์'],
        requests.items.map((r) => [formatDateTime(r.createdAt), r.userEmail, r.userRole && (roleLabel[r.userRole] ?? r.userRole),
          r.method, r.path, r.statusCode, r.durationMs, r.ipAddress, r.userAgent]))
    }
  }

  return (
    <Page wide>
      <PageHeader title="บันทึกการใช้งาน" subtitle="ใครทำอะไร เมื่อไหร่ จากเครื่องไหน — เห็นเฉพาะผู้ดูแลระบบ"
        actions={<Button icon="download" onClick={exportCsv} disabled={!data || data.items.length === 0}>ส่งออก CSV (หน้านี้)</Button>} />

      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <Segmented<Tab> label="ประเภทบันทึก" value={tab} onChange={(v) => update({ tab: v, entity: null, method: null, minStatus: null })}
            options={[{ value: 'audit', label: 'การเปลี่ยนแปลงข้อมูล' }, { value: 'requests', label: 'คำขอและความปลอดภัย' }]} />
        </div>

        <div className="flex flex-wrap items-end gap-2 border-b border-line bg-surface px-4 py-3">
          <div>
            <label htmlFor="q" className="mb-1 block text-xs text-ink-3">ค้นหา</label>
            <input id="q" type="search" value={searchText} onChange={(e) => setSearchText(e.target.value)}
              placeholder={tab === 'audit' ? 'ชื่อ อีเมล หรือข้อความ' : 'อีเมลหรือ path'}
              className="h-9 w-52 rounded-control border border-line bg-white px-3 text-[13px] focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
          </div>
          <div>
            <label htmlFor="ip" className="mb-1 block text-xs text-ink-3">IP</label>
            <input id="ip" value={ipText} onChange={(e) => setIpText(e.target.value)} placeholder="203.0.113.7"
              className="h-9 w-36 rounded-control border border-line bg-white px-3 text-[13px] tabular focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
          </div>
          {tab === 'audit' ? (
            <div>
              <label htmlFor="entity" className="mb-1 block text-xs text-ink-3">รายการ</label>
              <CompactSelect id="entity" value={entity} onChange={(e) => update({ entity: e.target.value })}>
                <option value="">ทั้งหมด</option><option value="Kpi">KPI</option><option value="Issue">แจ้งปัญหา</option>
              </CompactSelect>
            </div>
          ) : (
            <>
              <div>
                <label htmlFor="method" className="mb-1 block text-xs text-ink-3">Method</label>
                <CompactSelect id="method" value={method} onChange={(e) => update({ method: e.target.value })}>
                  <option value="">ทั้งหมด</option>{['POST', 'PUT', 'DELETE', 'GET'].map((m) => <option key={m}>{m}</option>)}
                </CompactSelect>
              </div>
              <div>
                <label htmlFor="status" className="mb-1 block text-xs text-ink-3">ผลลัพธ์</label>
                <CompactSelect id="status" value={minStatus} onChange={(e) => update({ minStatus: e.target.value })}>
                  <option value="">ทั้งหมด</option><option value="400">มีปัญหา (4xx/5xx)</option><option value="500">ข้อผิดพลาดเซิร์ฟเวอร์</option>
                </CompactSelect>
              </div>
            </>
          )}
          <div>
            <label htmlFor="from" className="mb-1 block text-xs text-ink-3">ตั้งแต่วันที่</label>
            <input id="from" type="date" value={from} max={to || undefined} onChange={(e) => update({ from: e.target.value })}
              className="h-9 rounded-control border border-line bg-white px-2 text-[13px]" />
          </div>
          <div>
            <label htmlFor="to" className="mb-1 block text-xs text-ink-3">ถึงวันที่</label>
            <input id="to" type="date" value={to} min={from || undefined} onChange={(e) => update({ to: e.target.value })}
              className="h-9 rounded-control border border-line bg-white px-2 text-[13px]" />
          </div>
          {hasFilters && (
            <Button size="sm" variant="ghost" icon="x" onClick={() => { setSearchText(''); setIpText(''); update({ q: null, entity: null, ip: null, method: null, minStatus: null, from: null, to: null }) }}>
              ล้างตัวกรอง
            </Button>
          )}
        </div>

        {error && <div className="p-4"><Alert tone="error">{error}</Alert></div>}

        {loading ? (
          <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-12" />)}</div>
        ) : !data || data.items.length === 0 ? (
          <EmptyState icon="history" title={hasFilters ? 'ไม่พบบันทึกที่ตรงกับตัวกรอง' : 'ยังไม่มีบันทึก'}>
            {hasFilters ? 'ลองเปลี่ยนช่วงวันที่หรือล้างตัวกรอง' : 'บันทึกจะปรากฏเมื่อมีการใช้งานระบบ'}
          </EmptyState>
        ) : tab === 'audit' ? (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[920px] text-left text-[13px]">
              <thead className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                <tr><th className="px-5 py-3">เวลา</th><th className="px-3 py-3">ผู้ใช้</th>
                  <th className="px-3 py-3">การกระทำ</th><th className="px-3 py-3">รายละเอียด</th>
                  <th className="px-5 py-3">IP</th></tr>
              </thead>
              <tbody className="divide-y divide-line">
                {audit!.items.map((a) => (
                  <Fragment key={a.id}>
                    <tr className="cursor-pointer hover:bg-surface" onClick={() => setExpanded(expanded === a.id ? null : a.id)}>
                      <td className="whitespace-nowrap px-5 py-3 tabular text-ink-2">{formatDateTime(a.createdAt)}</td>
                      <td className="px-3 py-3">
                        <p className="font-medium">{a.userName ?? a.userEmail ?? 'ระบบ'}</p>
                        <p className="text-xs text-ink-3">{[a.userRole && (roleLabel[a.userRole] ?? a.userRole), a.departmentName].filter(Boolean).join(' · ')}</p>
                      </td>
                      <td className="px-3 py-3">
                        <span className="font-medium">{auditActionLabel[a.action] ?? a.action}</span>{' '}
                        <Badge tone="gray">{a.entityName === 'Kpi' ? 'KPI' : a.entityName === 'Issue' ? 'แจ้งปัญหา' : a.entityName}</Badge>
                      </td>
                      <td className="max-w-sm px-3 py-3 text-ink-2">
                        <span className="line-clamp-1 break-words">{a.oldValues && a.newValues ? `${a.oldValues} → ${a.newValues}` : (a.newValues ?? a.oldValues ?? '–')}</span>
                      </td>
                      <td className="whitespace-nowrap px-5 py-3 tabular text-ink-2">{a.ipAddress ?? '–'}</td>
                    </tr>
                    {expanded === a.id && (
                      <tr className="bg-surface">
                        <td colSpan={5} className="px-5 py-4">
                          <dl className="grid gap-x-8 gap-y-2 text-[13px] sm:grid-cols-2">
                            <div><dt className="text-ink-3">รหัสรายการ</dt><dd className="break-all tabular">{a.entityId}</dd></div>
                            <div><dt className="text-ink-3">อีเมล</dt><dd>{a.userEmail ?? '–'}</dd></div>
                            <div><dt className="text-ink-3">ค่าเดิม</dt><dd className="whitespace-pre-wrap break-words">{a.oldValues ?? '–'}</dd></div>
                            <div><dt className="text-ink-3">ค่าใหม่</dt><dd className="whitespace-pre-wrap break-words">{a.newValues ?? '–'}</dd></div>
                            <div className="sm:col-span-2"><dt className="text-ink-3">อุปกรณ์ / เบราว์เซอร์</dt><dd className="break-all">{a.userAgent ?? '–'}</dd></div>
                          </dl>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-left text-[13px]">
              <thead className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                <tr><th className="px-5 py-3">เวลา</th><th className="px-3 py-3">ผู้ใช้</th>
                  <th className="px-3 py-3">คำขอ</th><th className="px-3 py-3">ผล</th>
                  <th className="px-3 py-3">ใช้เวลา</th><th className="px-5 py-3">IP</th></tr>
              </thead>
              <tbody className="divide-y divide-line">
                {requests!.items.map((r) => (
                  <tr key={r.id} title={r.userAgent ?? undefined}>
                    <td className="whitespace-nowrap px-5 py-3 tabular text-ink-2">{formatDateTime(r.createdAt)}</td>
                    <td className="px-3 py-3">
                      <p className="font-medium">{r.userEmail ?? <span className="text-ink-3">ไม่ได้เข้าสู่ระบบ</span>}</p>
                      {r.userRole && <p className="text-xs text-ink-3">{roleLabel[r.userRole] ?? r.userRole}</p>}
                    </td>
                    <td className="px-3 py-3"><span className="font-medium">{r.method}</span> <span className="break-all text-ink-2">{r.path}</span></td>
                    <td className="px-3 py-3"><Badge tone={r.statusCode >= 500 ? 'red' : r.statusCode >= 400 ? 'amber' : 'green'}>{r.statusCode}</Badge></td>
                    <td className="px-3 py-3 tabular text-ink-2">{r.durationMs} ms</td>
                    <td className="whitespace-nowrap px-5 py-3 tabular text-ink-2">{r.ipAddress ?? '–'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {data && (
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total}
            onChange={(p) => update({ page: String(p) })}
            onPageSizeChange={(size) => { setPageSize(size); update({}) }} />
        )}
      </Card>
    </Page>
  )
}
