import { useEffect, useState } from 'react'
import { knowledgeService } from '../../services/knowledge.service'
import { useCan } from '../../stores/authStore'
import type { Article, SuggestedArticle } from '../../types/knowledge'
import { apiError } from '../../utils/errors'
import Button from '../ui/Button'
import { Card } from '../ui/Card'
import Modal from '../ui/Modal'
import ArticleBody from './ArticleBody'

/** Waiting after the last keystroke keeps one request per pause instead of one per letter. */
const TYPING_PAUSE_MS = 500
/** The server suggests nothing for fewer characters than this. */
const MIN_TITLE = 3

/**
 * "This may already answer it": published articles that look related to the title being typed. An article opens in a
 * dialog, so the half-written report in the page behind it is not lost. It never blocks reporting, and a failure to load
 * just leaves the box out.
 */
export default function SuggestedArticles({ title }: { title: string }) {
  const allowed = useCan('knowledge.view')
  const [found, setFound] = useState<SuggestedArticle[]>([])
  const [open, setOpen] = useState<Article | null>(null)
  const [openError, setOpenError] = useState('')
  const [opening, setOpening] = useState<string | null>(null)

  useEffect(() => {
    const text = title.trim()
    if (!allowed || text.length < MIN_TITLE) return

    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      knowledgeService.suggest(text, controller.signal)
        .then(setFound)
        .catch(() => { /* a suggestion is a convenience */ })
    }, TYPING_PAUSE_MS)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [title, allowed])

  const show = async (id: string) => {
    setOpening(id)
    setOpenError('')
    try {
      setOpen(await knowledgeService.get(id))
    } catch (err) {
      setOpenError(apiError(err, 'เปิดบทความไม่สำเร็จ'))
    } finally {
      setOpening(null)
    }
  }

  // a title cut back below the minimum hides what the longer one found (the effect sets no state for it)
  const shown = title.trim().length >= MIN_TITLE ? found : []
  if (!allowed || shown.length === 0) return null

  return (
    <>
      <Card className="p-4" role="region" aria-label="บทความที่อาจช่วยได้">
        <p className="flex items-center gap-2 text-sm font-semibold text-ink">
          <i className="ti ti-bulb text-lg text-accent" aria-hidden="true" />บทความที่อาจช่วยได้
        </p>
        <p className="mt-0.5 text-xs text-ink-3">ลองดูก่อนส่งเรื่อง บางทีวิธีแก้อยู่ในนี้แล้ว</p>
        <ul className="mt-3 divide-y divide-line">
          {shown.map((article) => (
            <li key={article.id}>
              <button type="button" onClick={() => show(article.id)} disabled={opening !== null}
                className="flex w-full items-start gap-2 py-2.5 text-left hover:text-accent-deep disabled:opacity-60">
                <i className={`ti ${opening === article.id ? 'ti-loader-2 animate-spin' : 'ti-article'} mt-0.5 shrink-0 text-ink-3`} aria-hidden="true" />
                <span className="min-w-0">
                  <span className="block truncate text-[13.5px] font-medium text-ink">{article.title}</span>
                  {article.summary && <span className="line-clamp-2 break-words text-xs text-ink-2">{article.summary}</span>}
                </span>
              </button>
            </li>
          ))}
        </ul>
        {openError && <p role="alert" className="mt-2 text-xs text-red-ink">{openError}</p>}
      </Card>

      <Modal open={open !== null} onClose={() => setOpen(null)} title={open?.title ?? ''} width="lg"
        footer={<Button onClick={() => setOpen(null)}>ปิดและกลับไปแจ้งปัญหา</Button>}>
        {open && (
          <>
            {open.summary && <p className="mb-4 text-sm text-ink-2">{open.summary}</p>}
            <ArticleBody body={open.body} />
          </>
        )}
      </Modal>
    </>
  )
}
