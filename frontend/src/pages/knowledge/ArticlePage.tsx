import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import ArticleBody from '../../components/knowledge/ArticleBody'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Alert, EmptyState, Page, PageHeader, PageSkeleton } from '../../components/ui/Layout'
import Modal from '../../components/ui/Modal'
import { knowledgeService } from '../../services/knowledge.service'
import { useCan } from '../../stores/authStore'
import type { Article } from '../../types/knowledge'
import { apiError } from '../../utils/errors'
import { formatDate } from '../../utils/format'

/** One article. A draft is a "not found" to anyone who cannot manage the knowledge base, so a link to one reveals nothing. */
export default function ArticlePage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const canManage = useCan('knowledge.manage')
  // what was fetched and for which address: another id counts as loading again without the effect setting any state
  const [loaded, setLoaded] = useState<{ id: string; article: Article | null; missing: boolean } | null>(null)
  const here = loaded?.id === id ? loaded : null
  const article = here?.article ?? null
  const missing = here?.missing ?? false
  const setArticle = (next: Article) => setLoaded({ id, article: next, missing: false })
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    let current = true
    knowledgeService.get(id)
      .then((result) => { if (current) setLoaded({ id, article: result, missing: false }) })
      .catch((err) => { if (current) { setLoaded({ id, article: null, missing: true }); setError(apiError(err, 'เปิดบทความไม่สำเร็จ')) } })
    return () => { current = false }
  }, [id])

  const run = async (action: () => Promise<Article>) => {
    setBusy(true)
    setError('')
    try {
      setArticle(await action())
    } catch (err) {
      setError(apiError(err, 'ทำรายการไม่สำเร็จ'))
    } finally {
      setBusy(false)
    }
  }

  const remove = async () => {
    setBusy(true)
    try {
      await knowledgeService.remove(id)
      navigate('/knowledge', { replace: true })
    } catch (err) {
      setConfirmDelete(false)
      setError(apiError(err, 'ลบบทความไม่สำเร็จ'))
      setBusy(false)
    }
  }

  const back = <Link to="/knowledge" className="inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink"><i className="ti ti-arrow-left" aria-hidden="true" />ฐานความรู้</Link>

  if (missing) {
    return (
      <Page>
        <PageHeader back={back} title="ไม่พบบทความ" />
        <Card><EmptyState icon="book-off" title="ไม่พบบทความนี้">{error || 'บทความอาจถูกลบ หรือยังไม่ได้เผยแพร่'}</EmptyState></Card>
      </Page>
    )
  }
  if (!article) return <Page><PageSkeleton /></Page>

  return (
    <Page>
      <PageHeader back={back} title={article.title}
        subtitle={(
          <>
            {article.category} · เขียนโดย {article.authorName} · อัปเดต {formatDate(article.updatedAt)}
            {article.isPublished && <> · อ่านแล้ว {article.viewCount} ครั้ง</>}
          </>
        )}
        actions={canManage && (
          <>
            <Link to={`/knowledge/${article.id}/edit`}><Button icon="pencil" tabIndex={-1}>แก้ไข</Button></Link>
            {article.isPublished
              ? <Button icon="eye-off" loading={busy} onClick={() => run(() => knowledgeService.unpublish(article.id))}>ถอดออกจากการเผยแพร่</Button>
              : <Button variant="primary" icon="send" loading={busy} onClick={() => run(() => knowledgeService.publish(article.id))}>เผยแพร่</Button>}
            <Button variant="danger" icon="trash" disabled={busy} onClick={() => setConfirmDelete(true)}>ลบ</Button>
          </>
        )} />

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}
      {!article.isPublished && (
        <div className="mb-4">
          <Alert tone="info"><Badge tone="amber" icon="pencil">ฉบับร่าง</Badge> <span className="ml-1">เห็นเฉพาะผู้ดูแลฐานความรู้ จนกว่าจะเผยแพร่</span></Alert>
        </div>
      )}

      <Card className="p-5 sm:p-7">
        {article.summary && <p className="mb-5 border-l-4 border-accent pl-4 text-[14.5px] text-ink-2">{article.summary}</p>}
        <ArticleBody body={article.body} />
      </Card>

      <Modal open={confirmDelete} onClose={() => setConfirmDelete(false)} title="ลบบทความนี้" width="sm"
        footer={<>
          <Button onClick={() => setConfirmDelete(false)}>ยกเลิก</Button>
          <Button variant="danger" loading={busy} onClick={remove}>ลบบทความ</Button>
        </>}>
        <p className="text-sm text-ink-2">
          บทความ "{article.title}" จะถูกลบถาวรและกู้คืนไม่ได้ ถ้าเพียงต้องการซ่อนจากผู้อ่าน ให้เลือก "ถอดออกจากการเผยแพร่" แทน
        </p>
      </Modal>
    </Page>
  )
}
