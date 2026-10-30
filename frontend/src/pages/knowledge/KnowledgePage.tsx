import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Alert, EmptyState, Page, PageHeader, Pagination, Segmented, Skeleton } from '../../components/ui/Layout'
import { knowledgeService } from '../../services/knowledge.service'
import { useCan } from '../../stores/authStore'
import type { Paged } from '../../types/issue'
import type { ArticleListItem } from '../../types/knowledge'
import { apiError } from '../../utils/errors'
import { formatDate } from '../../utils/format'
import { usePageSize } from '../../utils/paging'

type Status = 'all' | 'published' | 'draft'

/** The knowledge base: search, pick a category, read. People who manage it also see drafts and can start a new article. */
export default function KnowledgePage() {
  const canManage = useCan('knowledge.manage')
  const [params, setParams] = useSearchParams()
  const search = params.get('q') ?? ''
  const inBody = params.get('body') === '1'
  const category = params.get('category') ?? ''
  // Only a manager has drafts to filter; the server ignores the filter for anyone else.
  const status: Status = canManage && (params.get('status') === 'published' || params.get('status') === 'draft')
    ? (params.get('status') as Status) : 'all'
  const page = Number(params.get('page') ?? 1)
  const [pageSize, setPageSize] = usePageSize()

  const [searchText, setSearchText] = useState(search)
  const [data, setData] = useState<Paged<ArticleListItem> | null>(null)
  const [categories, setCategories] = useState<string[]>([])
  const [error, setError] = useState('')
  // the request the list on screen answers; the page is loading while it differs from the request now wanted
  const [loadedKey, setLoadedKey] = useState('')
  const latestLoad = useRef(0)

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

  useEffect(() => {
    knowledgeService.categories().then(setCategories).catch(() => { /* the filter just stays empty */ })
  }, [])

  // Typing waits for a pause before it changes the address (and so the list).
  useEffect(() => {
    if (searchText.trim() === search) return
    const timer = window.setTimeout(() => update({ q: searchText.trim() || null }), 350)
    return () => window.clearTimeout(timer)
  }, [searchText, search, update])

  const wanted = [search, inBody, category, status, page, pageSize].join('|')
  const loading = loadedKey !== wanted

  useEffect(() => {
    const mine = ++latestLoad.current
    knowledgeService.list({
      search: search || undefined,
      category: category || undefined,
      inBody: inBody || undefined,
      published: status === 'published' ? true : status === 'draft' ? false : undefined,
      page, pageSize,
    })
      .then((result) => { if (mine === latestLoad.current) { setData(result); setError('') } })
      .catch((err) => { if (mine === latestLoad.current) setError(apiError(err, 'โหลดบทความไม่สำเร็จ')) })
      .finally(() => { if (mine === latestLoad.current) setLoadedKey(wanted) })
  }, [wanted, search, inBody, category, status, page, pageSize])

  const filtered = !!(search || category || status !== 'all')

  return (
    <Page>
      <PageHeader title="ฐานความรู้" subtitle="วิธีแก้ปัญหาที่พบบ่อย เขียนโดยทีม IT"
        actions={canManage && <Link to="/knowledge/new"><Button variant="primary" icon="plus" tabIndex={-1}>เขียนบทความ</Button></Link>} />

      <Card>
        <div className="space-y-3 border-b border-line px-4 py-3">
          <div>
            <div className="relative">
              <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
              <input type="search" value={searchText} onChange={(e) => setSearchText(e.target.value)}
                placeholder="ค้นหาหัวข้อหรือคำอธิบาย" aria-label="ค้นหาบทความ"
                className="h-9 w-full rounded-control border border-line bg-card pl-9 pr-3 text-[13px] focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
            </div>
            <label className="mt-1 flex items-center gap-1.5 text-xs text-ink-3">
              <input type="checkbox" checked={inBody} onChange={(e) => update({ body: e.target.checked ? '1' : null })} />
              ค้นหาในเนื้อหาด้วย (ช้ากว่า)
            </label>
          </div>

          {categories.length > 1 && (
            <Segmented label="หมวดหมู่" value={category} onChange={(next) => update({ category: next || null })}
              options={[{ value: '', label: 'ทุกหมวด' }, ...categories.map((c) => ({ value: c, label: c }))]} />
          )}
          {canManage && (
            <Segmented<Status> label="สถานะ" value={status} onChange={(next) => update({ status: next === 'all' ? null : next })}
              options={[{ value: 'all', label: 'ทั้งหมด' }, { value: 'published', label: 'เผยแพร่แล้ว' }, { value: 'draft', label: 'ฉบับร่าง' }]} />
          )}
        </div>

        {error && <div className="p-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

        {loading && !data ? (
          <div className="space-y-3 p-5"><Skeleton className="h-12" /><Skeleton className="h-12" /><Skeleton className="h-12" /></div>
        ) : data && data.items.length === 0 ? (
          <EmptyState icon="book-2" title={filtered ? 'ไม่พบบทความที่ตรงกับที่ค้นหา' : 'ยังไม่มีบทความ'}>
            {filtered ? 'ลองใช้คำอื่น หรือเลือกทุกหมวด' : canManage ? 'เริ่มจากบทความแรกเกี่ยวกับปัญหาที่ถูกถามบ่อย' : 'ทีม IT ยังไม่ได้เผยแพร่บทความ'}
          </EmptyState>
        ) : (
          <ul className={`divide-y divide-line ${loading ? 'opacity-60' : ''}`} aria-busy={loading}>
            {(data?.items ?? []).map((article) => (
              <li key={article.id}>
                <Link to={`/knowledge/${article.id}`} className="block px-5 py-4 hover:bg-surface">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="min-w-0 truncate text-[14.5px] font-semibold text-ink">{article.title}</p>
                    {!article.isPublished && <Badge tone="amber" icon="pencil">ฉบับร่าง</Badge>}
                  </div>
                  {article.summary && <p className="mt-0.5 line-clamp-2 break-words text-[13px] text-ink-2">{article.summary}</p>}
                  <p className="mt-1 text-xs text-ink-3">
                    {article.category} · อัปเดต {formatDate(article.updatedAt)}
                    {article.isPublished && <> · อ่านแล้ว {article.viewCount} ครั้ง</>}
                  </p>
                </Link>
              </li>
            ))}
          </ul>
        )}
        {data && <Pagination page={page} pageSize={pageSize} total={data.total} onChange={(p) => update({ page: String(p) })}
          onPageSizeChange={(size) => { setPageSize(size); update({ page: null }) }} />}
      </Card>
    </Page>
  )
}
