import { useEffect, useMemo, useRef, useState } from 'react'
import { ChoiceCards, SelectInput, TextArea, TextInput } from '../ui/Field'
import { Alert } from '../ui/Layout'
import type { IssueFormValues } from '../../types/issue'
import { IssueImpact } from '../../types/issue'
import { IMAGE_TYPES, MAX_FILES, MAX_FILE_BYTES } from './issueForm'
import { categoryLabel, enumOptions, impactHint, impactLabel, moduleLabel } from '../../utils/labels'
import { formatBytes } from '../../utils/format'

interface Props {
  values: IssueFormValues
  onChange: (values: IssueFormValues) => void
  files: File[]
  onFilesChange: (files: File[]) => void
  /** Field-level problems keyed by field name, e.g. from the last submit attempt. */
  errors?: Partial<Record<keyof IssueFormValues, string>>
}

/** The questions every report asks (shared by the signed-in form and the public form). */
export default function IssueFormFields({ values, onChange, files, onFilesChange, errors = {} }: Props) {
  const [fileError, setFileError] = useState('')
  const [dragging, setDragging] = useState(false)
  const input = useRef<HTMLInputElement>(null)

  const set = <K extends keyof IssueFormValues>(key: K, value: IssueFormValues[K]) =>
    onChange({ ...values, [key]: value })

  const addFiles = (incoming: File[]) => {
    const images = incoming.filter((f) => IMAGE_TYPES.includes(f.type))
    const tooBig = images.find((f) => f.size > MAX_FILE_BYTES)
    if (images.length < incoming.length) setFileError('แนบได้เฉพาะรูปภาพ (PNG, JPG, GIF, WebP)')
    else if (tooBig) setFileError(`ไฟล์ "${tooBig.name}" ใหญ่เกิน ${formatBytes(MAX_FILE_BYTES)}`)
    else if (files.length + images.length > MAX_FILES) setFileError(`แนบได้ไม่เกิน ${MAX_FILES} รูป`)
    else setFileError('')

    const accepted = images.filter((f) => f.size <= MAX_FILE_BYTES).slice(0, MAX_FILES - files.length)
    if (accepted.length) onFilesChange([...files, ...accepted])
  }

  // Paste a screenshot straight from the clipboard (Ctrl+V anywhere on the form).
  useEffect(() => {
    const onPaste = (e: ClipboardEvent) => {
      const pasted = [...(e.clipboardData?.files ?? [])]
      if (pasted.length) addFiles(pasted)
    }
    document.addEventListener('paste', onPaste)
    return () => document.removeEventListener('paste', onPaste)
  })

  const previews = useMemo(() => files.map((f) => ({ file: f, url: URL.createObjectURL(f) })), [files])
  useEffect(() => () => previews.forEach((p) => URL.revokeObjectURL(p.url)), [previews])

  return (
    <div className="space-y-5">
      <TextInput label="หัวข้อปัญหา" required value={values.title} maxLength={200}
        placeholder="เช่น กดอนุมัติ KPI แล้วไม่มีอะไรเกิดขึ้น"
        error={errors.title} onChange={(e) => set('title', e.target.value)} />

      <div className="grid gap-4 sm:grid-cols-2">
        <SelectInput label="ส่วนของระบบที่มีปัญหา" required value={values.module}
          options={enumOptions(moduleLabel)} onChange={(e) => set('module', Number(e.target.value) as IssueFormValues['module'])} />
        <SelectInput label="ประเภทปัญหา" required value={values.category}
          options={enumOptions(categoryLabel)} onChange={(e) => set('category', Number(e.target.value) as IssueFormValues['category'])} />
      </div>

      <ChoiceCards<IssueFormValues['impact']> label="กระทบการทำงานแค่ไหน" required value={values.impact}
        onChange={(v) => set('impact', v)}
        options={[
          { value: IssueImpact.Blocking, title: impactLabel[2], hint: impactHint[2], icon: 'alert-octagon' },
          { value: IssueImpact.Degraded, title: impactLabel[1], hint: impactHint[1], icon: 'alert-triangle' },
          { value: IssueImpact.Minor, title: impactLabel[0], hint: impactHint[0], icon: 'info-circle' },
        ]} />

      <TextArea label="เกิดอะไรขึ้น และคาดว่าควรเป็นอย่างไร" required rows={5} maxLength={4000}
        value={values.description} error={errors.description}
        hint="เล่าขั้นตอนที่ทำ ข้อความ error ที่เห็น และเกิดตอนไหน — ยิ่งละเอียด ยิ่งแก้ได้เร็ว"
        placeholder={'1. เข้าหน้า KPI → เปิด "IT Automation 2026"\n2. กดปุ่ม Approve\n3. สถานะยังเป็น Pending (ควรเป็น Active)'}
        onChange={(e) => set('description', e.target.value)} />

      <div>
        <p className="mb-1.5 text-[13px] font-medium text-ink">ภาพหน้าจอ <span className="font-normal text-ink-3">(ไม่บังคับ)</span></p>
        <div
          onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
          onDragLeave={() => setDragging(false)}
          onDrop={(e) => { e.preventDefault(); setDragging(false); addFiles([...e.dataTransfer.files]) }}
          className={`rounded-control border border-dashed px-4 py-5 text-center transition-colors
            ${dragging ? 'border-accent bg-accent-soft' : 'border-line-strong bg-surface'}`}>
          <i className="ti ti-photo-up text-2xl text-ink-3" aria-hidden="true" />
          <p className="mt-1 text-[13px] text-ink-2">
            ลากรูปมาวาง, กด <kbd className="rounded border border-line bg-card px-1.5 py-0.5 text-xs">Ctrl</kbd>+
            <kbd className="rounded border border-line bg-card px-1.5 py-0.5 text-xs">V</kbd> เพื่อวางภาพหน้าจอ หรือ{' '}
            <button type="button" className="font-medium text-accent hover:underline" onClick={() => input.current?.click()}>
              เลือกไฟล์
            </button>
          </p>
          <p className="mt-0.5 text-xs text-ink-3">สูงสุด {MAX_FILES} รูป รูปละไม่เกิน {formatBytes(MAX_FILE_BYTES)}</p>
          <input ref={input} type="file" accept={IMAGE_TYPES.join(',')} multiple className="sr-only" aria-label="เลือกรูปภาพ"
            onChange={(e) => { addFiles([...(e.target.files ?? [])]); e.target.value = '' }} />
        </div>

        {fileError && <div className="mt-2"><Alert tone="error">{fileError}</Alert></div>}

        {previews.length > 0 && (
          <ul className="mt-3 flex flex-wrap gap-3">
            {previews.map(({ file, url }) => (
              <li key={url} className="relative">
                <img src={url} alt={file.name} className="h-20 w-20 rounded-control border border-line object-cover" />
                <button type="button" aria-label={`เอา ${file.name} ออก`}
                  onClick={() => onFilesChange(files.filter((f) => f !== file))}
                  className="absolute -right-2 -top-2 flex h-6 w-6 items-center justify-center rounded-full bg-deep text-on-deep hover:brightness-125">
                  <i className="ti ti-x text-xs" aria-hidden="true" />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
