import type { Tone } from '../../types/issue'

export const stateLabel = { Ready: 'พร้อมใช้งาน', NeedsRepair: 'ขาดบางอย่าง', Blocked: 'ตรวจไม่ได้' } as const
export const stateTone: Record<keyof typeof stateLabel, Tone> = { Ready: 'green', NeedsRepair: 'amber', Blocked: 'red' }

export const problemText: Record<string, string> = {
  unreachable: 'เชื่อมต่อเซิร์ฟเวอร์ฐานข้อมูลไม่ได้ ตรวจชื่อหรือที่อยู่ พอร์ต ว่าเซิร์ฟเวอร์ทำงานอยู่ และ firewall',
  'login-failed': 'เซิร์ฟเวอร์ปฏิเสธการล็อกอิน ตรวจชื่อผู้ใช้และรหัสผ่าน (หรือบัญชี Windows)',
  'no-database': 'เซิร์ฟเวอร์ตอบ แต่ไม่มีฐานข้อมูลนี้ หรือบัญชีนี้เปิดไม่ได้ ให้สร้างด้วยสคริปต์ 01-create-database.sql',
  other: 'เชื่อมต่อไม่สำเร็จ',
}

export const stepLabel: Record<string, string> = {
  'apply-migrations': 'สร้างและอัปเดตตารางตามเวอร์ชันของโปรแกรม (migration ที่ค้างอยู่)',
  'create-tables': 'สร้างตารางที่หายไป',
  'add-columns': 'เพิ่มคอลัมน์ที่หายไป',
  seed: 'ใส่ข้อมูลตั้งต้น (บทบาทและสิทธิ์) ที่ขาด',
}
