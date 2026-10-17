import type { Tone } from '../types/issue'

// Every user-facing word for an enum lives here, so the wording stays consistent across pages.

export const issueStatusLabel: Record<number, string> = {
  0: 'ใหม่',
  1: 'รับเรื่องแล้ว',
  2: 'กำลังแก้ไข',
  3: 'รอข้อมูลจากผู้แจ้ง',
  4: 'แก้ไขแล้ว',
  5: 'ปิดงาน',
  6: 'ไม่ดำเนินการ',
  7: 'เปิดใหม่',
}

export const issueStatusTone: Record<number, Tone> = {
  0: 'orange', 1: 'blue', 2: 'blue', 3: 'amber', 4: 'green', 5: 'gray', 6: 'gray', 7: 'orange',
}

export const priorityLabel: Record<number, string> = { 0: 'ต่ำ', 1: 'ปานกลาง', 2: 'สูง', 3: 'วิกฤต' }
export const priorityTone: Record<number, Tone> = { 0: 'gray', 1: 'blue', 2: 'amber', 3: 'red' }

export const impactLabel: Record<number, string> = {
  0: 'เล็กน้อย ไม่รีบ',
  1: 'ลำบาก แต่พอทำงานได้',
  2: 'ทำงานต่อไม่ได้',
}
export const impactHint: Record<number, string> = {
  0: 'ใช้งานได้ปกติ แค่อยากให้แก้',
  1: 'มีทางเลี่ยง แต่ช้าหรือไม่สะดวก',
  2: 'ทั้งแผนกหรือหลายคนใช้งานไม่ได้',
}

export const categoryLabel: Record<number, string> = {
  0: 'ใช้งานไม่ได้ / ขึ้น error',
  1: 'ข้อมูลไม่ถูกต้อง',
  2: 'ช้า / ค้าง',
  3: 'ขอปรับปรุง',
  4: 'สอบถามวิธีใช้',
}

export const moduleLabel: Record<number, string> = {
  0: 'KPI',
  1: 'เข้าสู่ระบบ',
  2: 'Dashboard',
  3: 'ส่งออกรายงาน',
  4: 'ผู้ใช้งาน',
  5: 'ระบบแจ้งปัญหา',
  6: 'ปัญหา IT ทั่วไป (คอมพิวเตอร์ เครือข่าย)',
  7: 'อื่น ๆ',
}
export const moduleShort: Record<number, string> = {
  0: 'KPI', 1: 'เข้าสู่ระบบ', 2: 'Dashboard', 3: 'ส่งออก', 4: 'ผู้ใช้งาน', 5: 'แจ้งปัญหา', 6: 'IT ทั่วไป', 7: 'อื่น ๆ',
}

export const rootCauseLabel: Record<number, string> = {
  0: '–',
  1: 'บั๊กในโปรแกรม',
  2: 'ข้อมูลผิดพลาด',
  3: 'ผู้ใช้ใช้งานผิดวิธี',
  4: 'การตั้งค่า',
  5: 'โครงสร้างพื้นฐาน / บริการภายนอก',
  6: 'จำลองปัญหาไม่ได้',
  7: 'อื่น ๆ',
}

export const kpiStatusLabel: Record<string, string> = {
  Draft: 'แบบร่าง',
  PendingApproval: 'รออนุมัติ',
  Approved: 'อนุมัติแล้ว',
  Active: 'กำลังดำเนินการ',
  AtRisk: 'เสี่ยงล่าช้า',
  Delayed: 'ล่าช้า',
  Completed: 'ครบเป้าหมาย',
  Closed: 'ปิดแล้ว',
}

export const kpiStatusTone: Record<string, Tone> = {
  Draft: 'gray', PendingApproval: 'amber', Approved: 'blue', Active: 'green',
  AtRisk: 'amber', Delayed: 'red', Completed: 'green', Closed: 'gray',
}

export const roleLabel: Record<string, string> = {
  SuperAdmin: 'ผู้ดูแลระบบ',
  Director: 'ผู้อำนวยการ',
  Manager: 'ผู้จัดการ',
  TeamLeader: 'หัวหน้าทีม',
  Staff: 'พนักงาน',
  Viewer: 'ผู้ชม',
  ITSupport: 'ทีม IT',
}

/** Names the audit log writes in its Action column → plain Thai. */
export const auditActionLabel: Record<string, string> = {
  Create: 'สร้าง',
  Update: 'แก้ไข',
  Delete: 'ลบ',
  Submit: 'ส่งขออนุมัติ',
  Approve: 'อนุมัติ',
  Reject: 'ไม่อนุมัติ / ไม่ดำเนินการ',
  Close: 'ปิดงาน',
  UpdateProgress: 'อัปเดตความคืบหน้า',
  AutoStatusChange: 'ระบบเปลี่ยนสถานะ',
  Assign: 'มอบหมาย',
  Start: 'เริ่มแก้ไข',
  RequestInfo: 'ขอข้อมูลเพิ่ม',
  Resolve: 'แก้ไขแล้ว',
  Confirm: 'ยืนยัน / ปิดงาน',
  Reopen: 'เปิดใหม่',
  SetPriority: 'เปลี่ยนความสำคัญ',
  StaffReply: 'IT ตอบกลับ',
  ReporterReply: 'ผู้แจ้งตอบกลับ',
  InternalNote: 'บันทึกภายใน',
  Attach: 'แนบไฟล์',
  AutoClose: 'ระบบปิดงานอัตโนมัติ',
}

export const toneClasses: Record<Tone, { chip: string; dot: string }> = {
  orange: { chip: 'bg-accent-soft text-accent-ink', dot: 'bg-accent' },
  blue: { chip: 'bg-blue-soft text-blue-ink', dot: 'bg-blue-dot' },
  green: { chip: 'bg-green-soft text-green-ink', dot: 'bg-green-dot' },
  amber: { chip: 'bg-amber-soft text-amber-ink', dot: 'bg-amber-dot' },
  red: { chip: 'bg-red-soft text-red-ink', dot: 'bg-red-dot' },
  gray: { chip: 'bg-gray-soft text-gray-ink', dot: 'bg-gray-dot' },
}

export function enumOptions(labels: Record<number, string>): { value: number; label: string }[] {
  return Object.entries(labels).map(([value, label]) => ({ value: Number(value), label }))
}
