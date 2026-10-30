import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import IssueFormFields from '../../components/issues/IssueFormFields'
import { emptyIssueForm, validateIssueForm } from '../../components/issues/issueForm'
import SuggestedArticles from '../../components/knowledge/SuggestedArticles'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Alert, Page, PageHeader } from '../../components/ui/Layout'
import { issueService } from '../../services/issue.service'
import { useAuthStore } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { formatDateTime } from '../../utils/format'
import type { IssueDetail, IssueFormValues } from '../../types/issue'

/** Report a problem while signed in: who you are, your department and the time are filled in for you. */
export default function IssueNewPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const user = useAuthStore((s) => s.user)
  const from = (location.state as { from?: string } | null)?.from

  const [values, setValues] = useState<IssueFormValues>(emptyIssueForm)
  const [files, setFiles] = useState<File[]>([])
  const [errors, setErrors] = useState<ReturnType<typeof validateIssueForm>>({})
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [created, setCreated] = useState<IssueDetail | null>(null)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const found = validateIssueForm(values)
    setErrors(found)
    if (Object.keys(found).length > 0) return

    setSaving(true)
    setError('')
    try {
      setCreated(await issueService.create(values, files, from ? `${window.location.origin}${from}` : undefined))
    } catch (err) {
      setError(apiError(err))
    } finally {
      setSaving(false)
    }
  }

  if (created) {
    return (
      <Page>
        <Card className="mx-auto mt-6 max-w-xl p-8 text-center">
          <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-green-soft text-green-ink">
            <i className="ti ti-circle-check text-3xl" aria-hidden="true" />
          </span>
          <h1 className="mt-4 text-xl font-semibold">ส่งเรื่องเรียบร้อยแล้ว</h1>
          <p className="mt-1 text-ink-2">เลขที่เรื่องของคุณคือ</p>
          <p className="mt-2 text-3xl font-semibold tracking-tight text-accent tabular">{created.number}</p>
          <p className="mx-auto mt-4 max-w-sm text-sm text-ink-2">
            ทีม IT จะตอบรับภายใน 2 ชั่วโมง และแจ้งความคืบหน้าให้คุณทราบทางระบบ (และอีเมล) ติดตามได้ที่หน้า "เรื่องที่ฉันแจ้ง"
          </p>
          <div className="mt-6 flex flex-wrap justify-center gap-2">
            <Button variant="primary" onClick={() => navigate(`/issues/${created.id}`)}>ดูเรื่องนี้</Button>
            <Button onClick={() => { setCreated(null); setValues(emptyIssueForm); setFiles([]) }}>แจ้งเรื่องอื่น</Button>
          </div>
        </Card>
      </Page>
    )
  }

  return (
    <Page>
      <PageHeader
        back={<Link to="/issues" className="inline-flex items-center gap-1 text-sm text-ink-2 hover:text-ink"><i className="ti ti-arrow-left" aria-hidden="true" />กลับ</Link>}
        title="แจ้งปัญหา"
        subtitle="บอกเราว่าเกิดอะไรขึ้น ใช้เวลาไม่ถึง 1 นาที" />

      <form onSubmit={submit} noValidate className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <Card className="p-5 sm:p-6">
          {error && <div className="mb-5"><Alert tone="error">{error}</Alert></div>}
          <IssueFormFields values={values} onChange={setValues} files={files} onFilesChange={setFiles} errors={errors} />
          <div className="mt-6 flex justify-end gap-2 border-t border-line pt-5">
            <Button onClick={() => navigate(-1)}>ยกเลิก</Button>
            <Button type="submit" variant="primary" icon="send" loading={saving}>ส่งเรื่อง</Button>
          </div>
        </Card>

        <aside className="space-y-4">
          <SuggestedArticles title={values.title} />
          <Card className="p-5">
            <p className="text-xs font-semibold text-ink-3">ระบบกรอกให้อัตโนมัติ</p>
            <dl className="mt-3 space-y-3 text-sm">
              <div><dt className="text-ink-3">ผู้แจ้ง</dt><dd className="font-medium">{user?.fullName}</dd></div>
              <div><dt className="text-ink-3">แผนก</dt><dd className="font-medium">{user?.departmentName ?? 'ไม่ระบุ'}</dd></div>
              <div><dt className="text-ink-3">วันที่แจ้ง</dt><dd className="font-medium tabular">{formatDateTime(new Date())}</dd></div>
              {from && <div><dt className="text-ink-3">หน้าที่กำลังใช้</dt><dd className="break-all font-medium">{from}</dd></div>}
            </dl>
          </Card>
          <Card className="bg-accent-soft p-5">
            <p className="flex items-center gap-2 text-sm font-semibold text-accent-ink">
              <i className="ti ti-clock-bolt text-lg" aria-hidden="true" />ตอบรับภายใน 2 ชั่วโมง
            </p>
            <p className="mt-1 text-[13px] text-accent-ink/80">
              เรื่องที่กระทบการทำงานมากจะถูกจัดลำดับก่อน และคุณจะเห็นความคืบหน้าทุกขั้นตอน
            </p>
          </Card>
          <p className="px-1 text-xs text-ink-3">
            ถ้าเข้าสู่ระบบไม่ได้ ใช้ <Link to="/report" className="text-accent hover:underline">ฟอร์มแจ้งปัญหาสำหรับผู้ที่ยังไม่ได้เข้าสู่ระบบ</Link>
          </p>
        </aside>
      </form>
    </Page>
  )
}
