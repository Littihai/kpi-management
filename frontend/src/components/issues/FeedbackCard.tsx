import { useRef, useState } from 'react'
import Button from '../ui/Button'
import { Card, CardHeader } from '../ui/Card'
import { TextArea } from '../ui/Field'
import { feedbackLabel } from '../../utils/labels'
import { formatDateTime } from '../../utils/format'
import type { IssueFeedback } from '../../types/issue'

const SCORES = [1, 2, 3, 4, 5]

/** Five stars as one radio group: arrow keys move and choose, and every star is a 44px target for touch. */
export function StarPicker({ value, onChange, disabled }: { value: number; onChange: (rating: number) => void; disabled?: boolean }) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])

  const move = (to: number) => {
    const next = Math.min(5, Math.max(1, to))
    onChange(next)
    refs.current[next - 1]?.focus()
  }

  return (
    <div role="radiogroup" aria-label="คะแนนความพึงพอใจ" className="flex items-center gap-1">
      {SCORES.map((score) => {
        const on = score <= value
        return (
          <button key={score} type="button" role="radio" aria-checked={score === value} aria-label={`${score} ดาว ${feedbackLabel[score]}`}
            ref={(el) => { refs.current[score - 1] = el }}
            // Only one star is a tab stop: the chosen one, or the first while nothing is chosen.
            tabIndex={score === (value || 1) ? 0 : -1} disabled={disabled}
            onClick={() => onChange(score)}
            onKeyDown={(e) => {
              if (e.key === 'ArrowRight' || e.key === 'ArrowUp') { e.preventDefault(); move((value || 0) + 1) }
              if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') { e.preventDefault(); move((value || 2) - 1) }
            }}
            className="flex h-11 w-11 items-center justify-center rounded-control transition-colors hover:bg-surface disabled:opacity-50">
            <i className={`ti ti-star${on ? '-filled' : ''} text-[28px] ${on ? 'text-amber-dot' : 'text-ink-4'}`} aria-hidden="true" />
          </button>
        )
      })}
    </div>
  )
}

/** A rating that is already given: stars that read as text for screen readers. */
export function StarsRead({ rating, className = '' }: { rating: number; className?: string }) {
  return (
    <span role="img" aria-label={`${rating} จาก 5 ดาว ${feedbackLabel[rating] ?? ''}`} className={`inline-flex items-center gap-0.5 ${className}`}>
      {SCORES.map((score) => (
        <i key={score} className={`ti ti-star${score <= rating ? '-filled' : ''} ${score <= rating ? 'text-amber-dot' : 'text-ink-4'}`} aria-hidden="true" />
      ))}
    </span>
  )
}

/**
 * The satisfaction rating on an issue's page. The reporter gets the form while a rating can be given and sees their
 * answer afterwards; IT sees the answer; nobody else is shown the card at all.
 */
export default function FeedbackCard({
  canRate, feedback, isReporter, busy, onSubmit,
}: {
  canRate: boolean
  feedback: IssueFeedback | null
  isReporter: boolean
  busy: boolean
  onSubmit: (rating: number, note: string) => Promise<boolean>
}) {
  const [rating, setRating] = useState(0)
  const [note, setNote] = useState('')

  if (!canRate && !feedback) return null

  if (!canRate && feedback) {
    return (
      <Card>
        <CardHeader eyebrow="ความพึงพอใจ" title={isReporter ? 'คะแนนของคุณ' : 'คะแนนจากผู้แจ้ง'} />
        <div className="space-y-2 px-5 pb-5 pt-4">
          <p className="flex flex-wrap items-center gap-2 text-sm">
            <StarsRead rating={feedback.rating} className="text-lg" />
            <span className="font-medium text-ink">{feedbackLabel[feedback.rating]}</span>
          </p>
          {feedback.note && <p className="whitespace-pre-wrap break-words text-[14px] text-ink">{feedback.note}</p>}
          <p className="text-xs text-ink-3">ให้คะแนนเมื่อ {formatDateTime(feedback.ratedAt)}</p>
        </div>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader eyebrow="ความพึงพอใจ" title="พอใจกับการแก้ไขครั้งนี้แค่ไหน" />
      <div className="space-y-3 px-5 pb-5 pt-4">
        {feedback && <p className="text-xs text-ink-3">เรื่องนี้ถูกเปิดใหม่และแก้ไขอีกครั้ง — ให้คะแนนใหม่ได้ (คะแนนเดิมจะถูกแทนที่)</p>}
        <StarPicker value={rating} onChange={setRating} disabled={busy} />
        <p className="min-h-5 text-sm font-medium text-ink-2" aria-live="polite">{rating ? feedbackLabel[rating] : 'แตะดาวเพื่อให้คะแนน'}</p>
        <TextArea label="ความเห็นเพิ่มเติม (ถ้ามี)" rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs text-ink-3">ให้คะแนนได้ภายใน 30 วันหลังแก้ไขเสร็จ ให้ได้ครั้งเดียวต่อการแก้ไข</p>
          <Button variant="primary" icon="send" disabled={rating === 0} loading={busy}
            onClick={async () => { if (await onSubmit(rating, note)) { setRating(0); setNote('') } }}>ส่งคะแนน</Button>
        </div>
      </div>
    </Card>
  )
}
