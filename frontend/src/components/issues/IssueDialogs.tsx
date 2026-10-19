import { useState } from 'react'
import Button from '../ui/Button'
import { SelectInput, TextArea, TextInput } from '../ui/Field'
import Modal from '../ui/Modal'
import { IssueRootCause } from '../../types/issue'
import { enumOptions, rootCauseLabel } from '../../utils/labels'

export interface DialogProps<T> { open: boolean; onClose: () => void; busy: boolean; onSubmit: (value: T) => void }

export function RequestInfoDialog({ open, onClose, busy, onSubmit }: DialogProps<string>) {
  const [message, setMessage] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="ขอข้อมูลเพิ่มจากผู้แจ้ง"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" loading={busy} disabled={!message.trim()} onClick={() => { onSubmit(message.trim()); setMessage('') }}>ส่งคำถาม</Button>
      </>}>
      <p className="mb-3 text-sm text-ink-2">เรื่องจะเปลี่ยนเป็น "รอข้อมูลจากผู้แจ้ง" และหยุดนับเวลาจนกว่าผู้แจ้งจะตอบ</p>
      <TextArea label="ต้องการทราบอะไรเพิ่ม" rows={4} maxLength={4000} value={message} onChange={(e) => setMessage(e.target.value)}
        placeholder="เช่น เกิดกับเครื่อง/โปรแกรมไหน และเกิดตอนกี่โมง" />
    </Modal>
  )
}

export function ReopenDialog({ open, onClose, busy, onSubmit }: DialogProps<string>) {
  const [reason, setReason] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="ปัญหายังไม่หาย"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" loading={busy} disabled={!reason.trim()} onClick={() => { onSubmit(reason.trim()); setReason('') }}>เปิดเรื่องใหม่</Button>
      </>}>
      <TextArea label="ยังพบปัญหาอะไรอยู่" rows={4} maxLength={4000} value={reason} onChange={(e) => setReason(e.target.value)}
        hint="ทีม IT จะได้รับแจ้งทันที และเริ่มนับเวลาแก้ไขใหม่" />
    </Modal>
  )
}

export function RejectDialog({ open, onClose, busy, onSubmit }: Omit<DialogProps<string>, 'onSubmit'> & { onSubmit: (reason: string, duplicateNumber: string) => void }) {
  const [reason, setReason] = useState('')
  const [duplicate, setDuplicate] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="ไม่ดำเนินการต่อ"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="dark" loading={busy} disabled={!reason.trim()} onClick={() => onSubmit(reason.trim(), duplicate.trim())}>ยืนยัน</Button>
      </>}>
      <div className="space-y-4">
        <TextArea label="เหตุผล (ผู้แจ้งจะเห็นข้อความนี้)" rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)}
          placeholder="เช่น ซ้ำกับเรื่องที่แจ้งไว้แล้ว / ไม่ใช่ปัญหาของระบบนี้" />
        <TextInput label="ซ้ำกับเรื่องเลขที่ (ถ้ามี)" value={duplicate} onChange={(e) => setDuplicate(e.target.value)} placeholder="ISS-2569-0001" />
      </div>
    </Modal>
  )
}

export function ResolveDialog({ open, onClose, busy, onSubmit }: DialogProps<{
  resolution: string; rootCause: number; prevention?: string; fixReference?: string; timeSpentMinutes?: number | null
}>) {
  const [resolution, setResolution] = useState('')
  const [rootCause, setRootCause] = useState<number>(IssueRootCause.None)
  const [prevention, setPrevention] = useState('')
  const [fixReference, setFixReference] = useState('')
  const [minutes, setMinutes] = useState('')
  const valid = resolution.trim().length >= 5 && rootCause !== IssueRootCause.None

  return (
    <Modal open={open} onClose={onClose} title="บันทึกวิธีแก้ไข" width="lg"
      footer={<>
        <Button onClick={onClose}>ยกเลิก</Button>
        <Button variant="primary" loading={busy} disabled={!valid}
          onClick={() => onSubmit({
            resolution: resolution.trim(), rootCause,
            prevention: prevention.trim() || undefined,
            fixReference: fixReference.trim() || undefined,
            timeSpentMinutes: minutes ? Number(minutes) : null,
          })}>
          แก้ไขแล้ว แจ้งผู้แจ้ง
        </Button>
      </>}>
      <div className="space-y-4">
        <TextArea label="แก้ไขอย่างไร (ผู้แจ้งจะเห็นข้อความนี้)" required rows={4} maxLength={4000}
          value={resolution} onChange={(e) => setResolution(e.target.value)}
          placeholder="เช่น ปรับสิทธิ์ของปุ่มอนุมัติให้ตรงกับบทบาท ผู้ใช้ลองกดใหม่ได้เลย" />
        <SelectInput label="สาเหตุของปัญหา" required value={rootCause} placeholder="เลือกสาเหตุ"
          options={enumOptions(rootCauseLabel).filter((o) => o.value !== 0)}
          onChange={(e) => setRootCause(e.target.value ? Number(e.target.value) : IssueRootCause.None)} />
        <TextArea label="ป้องกันไม่ให้เกิดซ้ำอย่างไร" rows={2} maxLength={2000} value={prevention}
          onChange={(e) => setPrevention(e.target.value)} hint="ไม่บังคับ แต่ช่วยให้ทีมเรียนรู้ได้" />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextInput label="ลิงก์อ้างอิง (PR / commit)" value={fixReference} maxLength={500}
            onChange={(e) => setFixReference(e.target.value)} placeholder="https://github.com/..." />
          <TextInput label="เวลาที่ใช้ (นาที)" type="number" min={0} max={100000} value={minutes}
            onChange={(e) => setMinutes(e.target.value)} />
        </div>
      </div>
    </Modal>
  )
}
