import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import IssueFormFields from '../../components/issues/IssueFormFields'
import { emptyIssueForm, validateIssueForm } from '../../components/issues/issueForm'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { SelectInput, TextInput } from '../../components/ui/Field'
import { Alert } from '../../components/ui/Layout'
import { issueService } from '../../services/issue.service'
import { apiError } from '../../utils/errors'
import { IssueModule } from '../../types/issue'
import type { IssueFormValues, PublicReceipt } from '../../types/issue'

/** For people who cannot sign in: no account needed, just a name and a way to reach them. */
export default function PublicReportPage() {
  const [values, setValues] = useState<IssueFormValues>({ ...emptyIssueForm, module: IssueModule.Login })
  const [files, setFiles] = useState<File[]>([])
  const [contact, setContact] = useState({ name: '', email: '', phone: '', departmentId: '' })
  const [honeypot, setHoneypot] = useState('')
  const [departments, setDepartments] = useState<{ id: string; name: string }[]>([])
  const [errors, setErrors] = useState<ReturnType<typeof validateIssueForm> & { contact?: string; name?: string }>({})
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [receipt, setReceipt] = useState<PublicReceipt | null>(null)

  useEffect(() => {
    issueService.publicDepartments().then(setDepartments).catch(() => { /* the department is optional */ })
  }, [])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const found: typeof errors = validateIssueForm(values)
    if (contact.name.trim().length < 2) found.name = 'กรุณากรอกชื่อ'
    if (!contact.email.trim() && !contact.phone.trim()) found.contact = 'กรอกอีเมลหรือเบอร์โทรอย่างน้อยหนึ่งช่องทาง เพื่อให้ติดต่อกลับได้'
    setErrors(found)
    if (Object.keys(found).length > 0) return

    setSaving(true)
    setError('')
    try {
      setReceipt(await issueService.createPublic(values, contact, files, honeypot))
    } catch (err) {
      setError(apiError(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="min-h-[var(--vh100)] bg-canvas">
      <header className="border-b border-line bg-card">
        <div className="mx-auto flex max-w-3xl items-center justify-between px-4 py-4">
          <div className="flex items-center gap-3">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-accent text-on-accent" aria-hidden="true">
              <i className="ti ti-lifebuoy text-xl" />
            </span>
            <span className="text-[15px] font-semibold">แจ้งปัญหา</span>
          </div>
          <Link to="/login" className="text-sm text-ink-2 hover:text-ink">เข้าสู่ระบบ</Link>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-4 py-8">
        {receipt ? (
          <Card className="p-8 text-center">
            <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-green-soft text-green-ink">
              <i className="ti ti-circle-check text-3xl" aria-hidden="true" />
            </span>
            <h1 className="mt-4 text-xl font-semibold">รับเรื่องของคุณแล้ว</h1>
            <p className="mt-1 text-ink-2">เลขที่เรื่อง (เก็บไว้อ้างอิงเวลาติดต่อทีม IT)</p>
            <p className="mt-2 text-3xl font-semibold tracking-tight text-accent tabular">{receipt.number}</p>
            <p className="mx-auto mt-4 max-w-md text-sm text-ink-2">
              ทีม IT จะติดต่อกลับภายใน 2 ชั่วโมง ทางอีเมลหรือเบอร์โทรที่คุณให้ไว้
            </p>
            <div className="mt-6 flex justify-center gap-2">
              <Link to="/login"><Button variant="primary">กลับไปหน้าเข้าสู่ระบบ</Button></Link>
            </div>
          </Card>
        ) : (
          <>
            <div className="mb-6">
              <h1 className="text-2xl font-semibold">เข้าระบบไม่ได้หรือมีปัญหาอื่น ๆ?</h1>
              <p className="mt-1 text-ink-2">ไม่ต้องเข้าสู่ระบบ กรอกฟอร์มนี้แล้วทีม IT จะติดต่อกลับ</p>
            </div>

            <form onSubmit={submit} noValidate>
              <Card className="space-y-6 p-5 sm:p-6">
                {error && <Alert tone="error">{error}</Alert>}

                <section aria-labelledby="who" className="space-y-4">
                  <h2 id="who" className="text-xs font-semibold text-ink-3">ข้อมูลของคุณ</h2>
                  <div className="grid gap-4 sm:grid-cols-2">
                    <TextInput label="ชื่อ-นามสกุล" required value={contact.name} maxLength={100} error={errors.name}
                      autoComplete="name" onChange={(e) => setContact({ ...contact, name: e.target.value })} />
                    {departments.length > 0 && (
                      <SelectInput label="แผนก" value={contact.departmentId} placeholder="ไม่ระบุ"
                        options={departments.map((d) => ({ value: d.id, label: d.name }))}
                        onChange={(e) => setContact({ ...contact, departmentId: e.target.value })} />
                    )}
                    <TextInput label="อีเมล" type="email" value={contact.email} autoComplete="email" maxLength={254}
                      placeholder="name@company.com" onChange={(e) => setContact({ ...contact, email: e.target.value })} />
                    <TextInput label="เบอร์โทร" type="tel" value={contact.phone} autoComplete="tel" maxLength={40}
                      placeholder="08x-xxx-xxxx" onChange={(e) => setContact({ ...contact, phone: e.target.value })} />
                  </div>
                  {errors.contact && <p className="text-xs text-red-ink" role="alert">{errors.contact}</p>}
                  {/* Bots fill every field they see; people never see this one. */}
                  <div className="absolute -left-[9999px]" aria-hidden="true">
                    <label>เว็บไซต์ <input tabIndex={-1} autoComplete="off" value={honeypot} onChange={(e) => setHoneypot(e.target.value)} /></label>
                  </div>
                </section>

                <section aria-labelledby="what" className="space-y-4 border-t border-line pt-6">
                  <h2 id="what" className="text-xs font-semibold text-ink-3">ปัญหาที่พบ</h2>
                  <IssueFormFields values={values} onChange={setValues} files={files} onFilesChange={setFiles} errors={errors} />
                </section>

                <div className="flex justify-end border-t border-line pt-5">
                  <Button type="submit" variant="primary" icon="send" loading={saving}>ส่งเรื่อง</Button>
                </div>
              </Card>
            </form>
          </>
        )}
      </main>
    </div>
  )
}
