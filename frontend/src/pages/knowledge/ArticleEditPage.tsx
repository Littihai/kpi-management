import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { TextArea, TextInput } from '../../components/ui/Field'
import { Alert, Page, PageHeader, PageSkeleton } from '../../components/ui/Layout'
import { knowledgeService } from '../../services/knowledge.service'
import { ARTICLE_LIMITS, type Article, type ArticleInput } from '../../types/knowledge'
import { apiError } from '../../utils/errors'

const empty: ArticleInput = { title: '', summary: '', body: '', category: '' }

type Errors = Partial<Record<keyof ArticleInput, string>>

function validate(values: ArticleInput): Errors {
  const errors: Errors = {}
  if (values.title.trim().length < 3) errors.title = 'กรุณาระบุหัวข้ออย่างน้อย 3 ตัวอักษร'
  if (!values.body.trim()) errors.body = 'กรุณาเขียนเนื้อหาบทความ'
  return errors
}

/** Write a new article or change one. Saving keeps a draft a draft; publishing is its own step (or the second button here). */
export default function ArticleEditPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [article, setArticle] = useState<Article | null>(null)
  const [values, setValues] = useState<ArticleInput>(empty)
  const [categories, setCategories] = useState<string[]>([])
  const [errors, setErrors] = useState<Errors>({})
  const [error, setError] = useState('')
  const [stale, setStale] = useState(false)
  // Without the article an edit page must not fall back to the blank "write" form: saving would create a duplicate.
  const [loadFailed, setLoadFailed] = useState(false)
  const [loading, setLoading] = useState(!!id)
  const [saving, setSaving] = useState<'save' | 'publish' | null>(null)

  const load = (articleId: string) => {
    // Opening the editor is not a reader opening the article, so it is not counted as a view.
    knowledgeService.get(articleId, false)
      .then((loaded) => {
        setArticle(loaded)
        setValues({ title: loaded.title, summary: loaded.summary, body: loaded.body, category: loaded.category })
        setStale(false)
        setLoadFailed(false)
        setError('')
      })
      .catch((err) => {
        setLoadFailed(true)
        setError(apiError(err, 'โหลดบทความไม่สำเร็จ'))
      })
      .finally(() => setLoading(false))
  }

  useEffect(() => { if (id) load(id) }, [id])
  useEffect(() => {
    knowledgeService.categories().then(setCategories).catch(() => { /* free text still works */ })
  }, [])

  const set = (field: keyof ArticleInput) => (e: { target: { value: string } }) => setValues((v) => ({ ...v, [field]: e.target.value }))

  const submit = async (publish: boolean) => {
    const found = validate(values)
    setErrors(found)
    if (Object.keys(found).length > 0) return

    setSaving(publish ? 'publish' : 'save')
    setError('')
    try {
      let saved = article ? await knowledgeService.update(article.id, values, article.version) : await knowledgeService.create(values)
      // The page must keep the draft if the second step fails, so a failed publish does not lose the save.
      setArticle(saved)
      if (publish && !saved.isPublished) saved = await knowledgeService.publish(saved.id)
      navigate(`/knowledge/${saved.id}`)
    } catch (err) {
      const conflict = (err as { response?: { status?: number } }).response?.status === 409
      setStale(conflict && !!article)
      setError(apiError(err, 'บันทึกไม่สำเร็จ'))
    } finally {
      setSaving(null)
    }
  }

  if (loading) return <Page><PageSkeleton /></Page>

  if (id && !article) {
    return (
      <Page>
        <PageHeader
          back={<Link to="/knowledge" className="inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink"><i className="ti ti-arrow-left" aria-hidden="true" />กลับ</Link>}
          title="ไม่พบบทความ" />
        <Alert tone="error">{loadFailed && error ? error : 'โหลดบทความไม่สำเร็จ'}</Alert>
      </Page>
    )
  }

  const published = article?.isPublished ?? false

  return (
    <Page>
      <PageHeader
        back={<Link to={article ? `/knowledge/${article.id}` : '/knowledge'} className="inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink"><i className="ti ti-arrow-left" aria-hidden="true" />กลับ</Link>}
        title={article ? 'แก้ไขบทความ' : 'เขียนบทความ'}
        subtitle={published ? 'บทความนี้เผยแพร่อยู่ การบันทึกจะเปลี่ยนที่ผู้อ่านเห็นทันที' : 'บันทึกเป็นฉบับร่างก่อน แล้วเผยแพร่เมื่อพร้อม'} />

      <form noValidate onSubmit={(e) => { e.preventDefault(); submit(false) }}>
        <Card className="space-y-5 p-5 sm:p-6">
          {error && (
            <Alert tone="error" onClose={stale ? undefined : () => setError('')}>
              {error}
              {stale && article && <> <button type="button" className="font-semibold underline" onClick={() => { setLoading(true); load(article.id) }}>โหลดเวอร์ชันล่าสุด</button> (ข้อความที่พิมพ์ไว้จะหายไป ควรคัดลอกเก็บไว้ก่อน)</>}
            </Alert>
          )}

          <TextInput label="หัวข้อ" required value={values.title} onChange={set('title')} maxLength={ARTICLE_LIMITS.title}
            error={errors.title} placeholder="เช่น ต่อ VPN จากที่บ้านไม่ได้" />
          <TextArea label="คำอธิบายสั้น ๆ" rows={2} value={values.summary} onChange={set('summary')} maxLength={ARTICLE_LIMITS.summary}
            hint="แสดงในรายการและในกล่องแนะนำตอนแจ้งปัญหา ใช้ช่วยให้คนเลือกอ่านถูกบทความ" />
          <TextInput label="หมวดหมู่" value={values.category} onChange={set('category')} maxLength={ARTICLE_LIMITS.category}
            list="knowledge-categories" hint="เว้นว่างไว้จะเป็น &quot;ทั่วไป&quot;" />
          <datalist id="knowledge-categories">{categories.map((c) => <option key={c} value={c} />)}</datalist>
          <TextArea label="เนื้อหา" required rows={16} value={values.body} onChange={set('body')} maxLength={ARTICLE_LIMITS.body}
            error={errors.body} className="font-[inherit] leading-relaxed"
            hint={`ข้อความล้วน เว้นบรรทัดว่างเพื่อแยกย่อหน้า · "# " นำหน้าเป็นหัวข้อย่อย · "- " เป็นรายการ · "1. " เป็นขั้นตอน · ลิงก์ http/https กดได้เอง (${values.body.length.toLocaleString()}/${ARTICLE_LIMITS.body.toLocaleString()})`} />
        </Card>

        <div className="mt-5 flex flex-wrap justify-end gap-2">
          <Button onClick={() => navigate(article ? `/knowledge/${article.id}` : '/knowledge')}>ยกเลิก</Button>
          {published ? (
            <Button type="submit" variant="primary" icon="device-floppy" loading={saving !== null}>บันทึก</Button>
          ) : (
            <>
              <Button icon="device-floppy" loading={saving === 'save'} disabled={saving !== null} onClick={() => submit(false)}>บันทึกฉบับร่าง</Button>
              <Button variant="primary" icon="send" loading={saving === 'publish'} disabled={saving !== null} onClick={() => submit(true)}>บันทึกและเผยแพร่</Button>
            </>
          )}
        </div>
      </form>
    </Page>
  )
}
