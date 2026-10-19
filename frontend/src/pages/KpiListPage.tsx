import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import Badge from '../components/ui/Badge'
import Button from '../components/ui/Button'
import { Card, Stat } from '../components/ui/Card'
import { Alert, EmptyState, Page, PageHeader, Pagination, Segmented, Skeleton } from '../components/ui/Layout'
import Progress from '../components/ui/Progress'
import { useKpiEvents } from '../components/realtimeContext'
import { exportService } from '../services/dashboard.service'
import { kpiService } from '../services/kpi.service'
import { useAuthStore, useCan } from '../stores/authStore'
import { apiError } from '../utils/errors'
import { formatDate } from '../utils/format'
import { usePaging } from '../utils/paging'
import { kpiStatusLabel, kpiStatusTone } from '../utils/labels'
import type { KpiDto } from '../types/kpi'

const filters = ['All', 'Active', 'PendingApproval', 'AtRisk', 'Delayed', 'Completed'] as const

export default function KpiListPage() {
  const navigate = useNavigate()
  const token = useAuthStore((s) => s.token)
  const canCreate = useCan('kpi.create')
  const canExport = useCan('export.view')
  const [kpis, setKpis] = useState<KpiDto[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<(typeof filters)[number]>('All')
  const [error, setError] = useState('')
  const [exporting, setExporting] = useState<'excel' | 'pdf' | null>(null)

  const load = useCallback(() =>
    kpiService.getAll()
      .then((data) => { setKpis(data); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดรายการ KPI ไม่สำเร็จ')))
      .finally(() => setLoading(false)), [])

  useEffect(() => { load() }, [load])

  // Another user changed a KPI: keep the list current without a reload.
  useKpiEvents(() => { load() })

  const counts = useMemo(() => {
    const map: Record<string, number> = { All: kpis.length }
    kpis.forEach((k) => { map[k.statusLabel] = (map[k.statusLabel] ?? 0) + 1 })
    return map
  }, [kpis])

  const filtered = kpis.filter((k) => {
    const matchFilter = filter === 'All' || k.statusLabel === filter
    const term = search.trim().toLowerCase()
    return matchFilter && (!term || k.name.toLowerCase().includes(term) || k.departmentName.toLowerCase().includes(term)
      || k.ownerName.toLowerCase().includes(term))
  })

  const paging = usePaging(filtered, `${filter}|${search}`)

  const download = async (kind: 'excel' | 'pdf') => {
    if (!token) return
    setExporting(kind)
    try {
      await (kind === 'excel' ? exportService.downloadExcel(token) : exportService.downloadPdf(token))
    } catch (err) {
      setError(apiError(err, 'ส่งออกไม่สำเร็จ'))
    } finally {
      setExporting(null)
    }
  }

  return (
    <Page wide>
      <PageHeader title="KPI" subtitle={`${kpis.length} รายการ`}
        actions={
          <>
            {canExport && (
              <>
                <Button icon="file-spreadsheet" loading={exporting === 'excel'} onClick={() => download('excel')}>Excel</Button>
                <Button icon="file-type-pdf" loading={exporting === 'pdf'} onClick={() => download('pdf')}>PDF</Button>
              </>
            )}
            {canCreate && <Button variant="primary" icon="plus" onClick={() => navigate('/kpi/create')}>สร้าง KPI</Button>}
          </>
        } />

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      <section aria-label="สรุป" className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
        <Stat dark label="ทั้งหมด" value={counts.All ?? 0} icon="target-arrow" />
        <Stat label="กำลังดำเนินการ" value={counts.Active ?? 0} icon="player-play" tone="green" />
        <Stat label="เสี่ยงล่าช้า" value={counts.AtRisk ?? 0} icon="alert-triangle" tone={counts.AtRisk ? 'amber' : 'default'} />
        <Stat label="ล่าช้า" value={counts.Delayed ?? 0} icon="alarm" tone={counts.Delayed ? 'red' : 'default'} />
      </section>

      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <Segmented label="กรองตามสถานะ" value={filter} onChange={setFilter}
            options={filters.map((f) => ({ value: f, label: f === 'All' ? 'ทั้งหมด' : kpiStatusLabel[f], count: counts[f] ?? 0 }))} />
          <div className="relative ml-auto w-full sm:w-64">
            <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
            <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="ค้นหา KPI"
              placeholder="ค้นหาชื่อ แผนก หรือเจ้าของ"
              className="h-9 w-full rounded-control border border-line bg-card pl-9 pr-3 text-[13px] focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
          </div>
        </div>

        {loading ? (
          <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}</div>
        ) : filtered.length === 0 ? (
          <EmptyState icon={kpis.length === 0 ? 'target-arrow' : 'search-off'}
            title={kpis.length === 0 ? 'ยังไม่มี KPI' : 'ไม่พบ KPI ที่ตรงกับตัวกรอง'}
            action={kpis.length === 0 && canCreate ? <Button variant="primary" icon="plus" onClick={() => navigate('/kpi/create')}>สร้าง KPI แรก</Button> : undefined}>
            {kpis.length === 0 ? 'สร้าง KPI ของแผนก แล้วส่งขออนุมัติได้เลย' : 'ลองเปลี่ยนสถานะหรือคำค้นหา'}
          </EmptyState>
        ) : (
          <>
            <ul className="divide-y divide-line">
              {paging.pageItems.map((kpi) => (
                <li key={kpi.id}>
                  <Link to={`/kpi/${kpi.id}`} className="block px-5 py-4 transition-colors hover:bg-surface">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="truncate font-medium text-ink">{kpi.name}</p>
                        <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-ink-3">
                          <span><i className="ti ti-user" aria-hidden="true" /> {kpi.ownerName}</span>
                          <span aria-hidden="true">·</span><span>{kpi.departmentName}</span>
                          <span aria-hidden="true">·</span><span>ครบกำหนด {formatDate(kpi.dueDate)}</span>
                        </p>
                      </div>
                      <Badge tone={kpiStatusTone[kpi.statusLabel] ?? 'gray'} dot>{kpiStatusLabel[kpi.statusLabel] ?? kpi.statusLabel}</Badge>
                    </div>
                    <div className="mt-3 flex items-center gap-3">
                      <Progress value={kpi.progress} max={kpi.target || 100} color={kpi.trafficLight} label={`ความคืบหน้า ${kpi.name}`} />
                      <span className="w-24 shrink-0 text-right text-[13px] font-medium tabular">
                        {Number(kpi.progress).toFixed(0)} / {Number(kpi.target).toFixed(0)}%
                      </span>
                    </div>
                    {kpi.rejectReason && kpi.statusLabel === 'Draft' && (
                      <p className="mt-2 text-xs text-amber-ink"><i className="ti ti-arrow-back-up" aria-hidden="true" /> ถูกตีกลับ: {kpi.rejectReason}</p>
                    )}
                  </Link>
                </li>
              ))}
            </ul>
            <Pagination page={paging.page} pageSize={paging.pageSize} total={paging.total}
              onChange={paging.setPage} onPageSizeChange={paging.setPageSize} />
          </>
        )}
      </Card>
    </Page>
  )
}
