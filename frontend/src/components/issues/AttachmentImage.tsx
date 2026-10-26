import { useEffect, useState } from 'react'
import { issueService } from '../../services/issue.service'
import type { IssueAttachment } from '../../types/issue'
import LoadingProgress from '../ui/LoadingProgress'
import Modal from '../ui/Modal'
import { formatBytes } from '../../utils/format'

/** Thumbnail of an attached image. Downloads it with the sign-in token, and opens full size on click. */
export default function AttachmentImage({ issueId, attachment }: { issueId: string; attachment: IssueAttachment }) {
  const [url, setUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)
  const [percent, setPercent] = useState<number | undefined>(undefined)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    let objectUrl: string | null = null
    let cancelled = false
    issueService.attachmentBlob(issueId, attachment.id, (p) => { if (!cancelled) setPercent(p) })
      .then((blob) => {
        if (cancelled) return
        objectUrl = URL.createObjectURL(blob)
        setUrl(objectUrl)
      })
      .catch(() => { if (!cancelled) setFailed(true) })
    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [issueId, attachment.id])

  if (failed) {
    return (
      <div className="flex h-28 w-28 flex-col items-center justify-center rounded-control border border-line bg-surface text-center text-xs text-ink-3">
        <i className="ti ti-photo-off text-xl" aria-hidden="true" />โหลดรูปไม่ได้
      </div>
    )
  }

  return (
    <>
      <button type="button" onClick={() => url && setOpen(true)} disabled={!url}
        aria-label={`ดูรูป ${attachment.fileName}`}
        className="group relative h-28 w-28 overflow-hidden rounded-control border border-line bg-surface-2">
        {url
          ? <img src={url} alt={attachment.fileName} className="h-full w-full object-cover transition-transform group-hover:scale-105" />
          : <span className="flex h-full items-center justify-center"><LoadingProgress width={64} title={null} percent={percent} /></span>}
      </button>
      <Modal open={open} onClose={() => setOpen(false)} title={attachment.fileName} width="lg"
        footer={<span className="mr-auto text-xs text-ink-3">{formatBytes(attachment.sizeBytes)}</span>}>
        {url && <img src={url} alt={attachment.fileName} className="mx-auto max-h-[70vh] rounded-control" />}
      </Modal>
    </>
  )
}
