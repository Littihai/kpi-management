import { createContext, useCallback, useContext } from 'react'
import { useLocation, useNavigate, type Location } from 'react-router-dom'

/**
 * Settings are one window over the current page (as in Lotline) that is still a real route, /settings/<section>,
 * so a section can be linked and survives a refresh. The page underneath stays mounted: opening settings remembers
 * where the person was ("background location") and the routes keep rendering that page behind the window.
 */
export type SettingsSectionId = 'general' | 'account' | 'system' | 'security' | 'email' | 'database' | 'users' | 'roles' | 'departments' | 'audit' | 'about'
export type SettingsGroup = 'device' | 'personal' | 'admin'

export interface SettingsSectionDef {
  id: SettingsSectionId
  group: SettingsGroup
  label: string
  desc: string
  icon: string
  /** The permission that shows the section; none = everyone. The API enforces the same permission. */
  permission?: string
}

export const SETTINGS_SECTIONS: SettingsSectionDef[] = [
  { id: 'general', group: 'device', label: 'ทั่วไป', desc: 'ธีม สี ฟอนต์ การจัดวางหน้าจอ และการแจ้งเตือน', icon: 'adjustments-horizontal' },
  { id: 'account', group: 'personal', label: 'บัญชีของฉัน', desc: 'ข้อมูลของคุณและการเปลี่ยนรหัสผ่าน', icon: 'user-circle' },
  { id: 'system', group: 'admin', label: 'ตั้งค่าระบบ', desc: 'เป้าหมายเวลา SLA ขีดจำกัด อีเมล และการเก็บข้อมูล', icon: 'settings-cog', permission: 'settings.view' },
  { id: 'security', group: 'admin', label: 'ความปลอดภัย', desc: 'นโยบายรหัสผ่าน การล็อกบัญชี อายุการเข้าสู่ระบบ และบันทึกการเข้าสู่ระบบ', icon: 'shield-check', permission: 'settings.view' },
  { id: 'email', group: 'admin', label: 'อีเมล (SMTP)', desc: 'ช่องทางส่งอีเมลแจ้งเตือน เซิร์ฟเวอร์ SMTP และการทดสอบส่ง', icon: 'mail-cog', permission: 'settings.view' },
  { id: 'database', group: 'admin', label: 'ฐานข้อมูล', desc: 'ตรวจว่าฐานข้อมูลมีตารางและข้อมูลตั้งต้นครบหรือไม่ และสร้างสิ่งที่ขาด', icon: 'database', permission: 'settings.view' },
  { id: 'users', group: 'admin', label: 'ผู้ใช้งาน', desc: 'เพิ่ม แก้ไข ปิดใช้งาน และตั้งรหัสผ่านใหม่ให้ผู้ใช้', icon: 'users', permission: 'user.view' },
  { id: 'roles', group: 'admin', label: 'บทบาทและสิทธิ์', desc: 'กำหนดว่าใครทำอะไรได้ สร้างบทบาทใหม่ และดูตารางเทียบสิทธิ์', icon: 'shield-lock', permission: 'role.view' },
  { id: 'departments', group: 'admin', label: 'แผนก', desc: 'แผนกที่ใช้ในการแจ้งปัญหาและรายชื่อผู้ใช้', icon: 'building', permission: 'department.manage' },
  { id: 'audit', group: 'admin', label: 'บันทึกการใช้งาน', desc: 'ใครทำอะไร เมื่อไหร่ จากเครื่องไหน', icon: 'history', permission: 'overview.view' },
  { id: 'about', group: 'device', label: 'เกี่ยวกับ', desc: 'เวอร์ชันและสถานะการเชื่อมต่อ', icon: 'info-circle' },
]

/** `note` says where the choices of the group are kept, so nobody expects a look they set here to follow them to another computer. */
export const SETTINGS_GROUPS: { id: SettingsGroup; label: string; note: string }[] = [
  { id: 'device', label: 'เครื่องนี้', note: 'จำไว้ในเครื่องนี้เท่านั้น' },
  { id: 'personal', label: 'บัญชีของฉัน', note: 'เก็บกับบัญชีของคุณ' },
  { id: 'admin', label: 'ผู้ดูแลระบบ', note: 'ใช้กับทุกคน เก็บบนเซิร์ฟเวอร์' },
]

/**
 * What the settings search looks through, besides the section names. An item with an `anchor` is a row the window
 * scrolls to (the element with that id inside the section); one without opens the section. Admin sections load on
 * demand, so their rows are keywords only.
 */
export interface SettingsItem {
  section: SettingsSectionId
  label: string
  anchor?: string
  keywords?: string
}

export const SETTINGS_ITEMS: SettingsItem[] = [
  { section: 'general', label: 'ธีม', anchor: 'theme', keywords: 'สว่าง มืด dark light โหมดกลางคืน ตามเครื่อง' },
  { section: 'general', label: 'สีหลัก', anchor: 'accent', keywords: 'สี accent น้ำเงิน เขียว ม่วง' },
  { section: 'general', label: 'ฟอนต์', anchor: 'font', keywords: 'font ตัวอักษร Inter Sarabun IBM Plex' },
  { section: 'general', label: 'โทนสีเทา', anchor: 'base', keywords: 'พื้นหลัง เส้นขอบ' },
  { section: 'general', label: 'ความโค้งของมุม', anchor: 'radius', keywords: 'radius ขอบมน เหลี่ยม' },
  { section: 'general', label: 'เมนูหลัก', anchor: 'nav', keywords: 'แถบด้านข้าง sidebar ไอคอน แถบด้านบน' },
  { section: 'general', label: 'ความกว้างของหน้า', anchor: 'container', keywords: 'แคบ กว้าง เต็มจอ' },
  { section: 'general', label: 'ขนาดตัวอักษร', anchor: 'size', keywords: 'ใหญ่ เล็ก ซูม' },
  { section: 'general', label: 'ความคมชัดสูง', anchor: 'contrast', keywords: 'contrast ตัวอักษรเข้ม' },
  { section: 'general', label: 'การเคลื่อนไหว', anchor: 'motion', keywords: 'animation แอนิเมชัน ลดการเคลื่อนไหว' },
  { section: 'general', label: 'การแจ้งเตือน', anchor: 'notify', keywords: 'notification แจ้งเตือนบนเดสก์ท็อป toast ป๊อปอัป เด้ง เบราว์เซอร์ Windows tray ถาดระบบ ปิดหน้าต่าง ย่อ เบื้องหลัง startup เริ่มต้นระบบ เปิดเอง อัตโนมัติ บูตเครื่อง' },
  { section: 'account', label: 'เปลี่ยนรหัสผ่าน', keywords: 'password รหัสผ่านใหม่' },
  { section: 'system', label: 'เป้าหมายเวลา SLA', keywords: 'sla เวลาตอบ เวลาแก้ ชั่วโมง ขีดจำกัด การเก็บข้อมูล ชื่อองค์กร' },
  { section: 'security', label: 'นโยบายรหัสผ่าน', keywords: 'ความยาวรหัสผ่าน หมดอายุ ประวัติรหัสผ่าน' },
  { section: 'security', label: 'การล็อกบัญชี', keywords: 'lock ล็อก ใส่รหัสผิด' },
  { section: 'security', label: 'บันทึกการเข้าสู่ระบบ', keywords: 'sign-in log ล็อกอิน ประวัติเข้าใช้' },
  { section: 'email', label: 'เซิร์ฟเวอร์ SMTP', keywords: 'smtp mail อีเมล ส่งเมล พอร์ต ทดสอบส่ง' },
  { section: 'database', label: 'ตรวจฐานข้อมูล', keywords: 'database sql server ตาราง ซ่อม สร้างที่ขาด เชื่อมต่อ' },
  { section: 'users', label: 'เพิ่มผู้ใช้', keywords: 'user พนักงาน ปิดใช้งาน ปลดล็อก ตั้งรหัสผ่านใหม่' },
  { section: 'roles', label: 'บทบาทและสิทธิ์', keywords: 'role permission สิทธิ์ ตารางเทียบสิทธิ์ ใครทำอะไรได้' },
  { section: 'departments', label: 'แผนก', keywords: 'department ฝ่าย ทีม' },
  { section: 'audit', label: 'บันทึกการใช้งาน', keywords: 'audit log ใครทำอะไร ประวัติ' },
  { section: 'about', label: 'เวอร์ชัน', keywords: 'version อัปเดต update สถานะการเชื่อมต่อ' },
]

export function visibleSettingsSections(permissions: readonly string[] | undefined): SettingsSectionDef[] {
  return SETTINGS_SECTIONS.filter((s) => !s.permission || permissions?.includes(s.permission))
}

export function isSettingsPath(pathname: string): boolean {
  return pathname === '/settings' || pathname.startsWith('/settings/')
}

export interface SettingsLocationState {
  backgroundLocation?: Location
}

/**
 * The browser's real location. Pages rendered by the routes see the background location while settings are open
 * (that is how the page stays mounted), so they cannot tell from useLocation() that settings are already up.
 */
export const RealLocationContext = createContext<Location | null>(null)

/** Opens settings over the current page, or switches section in place if they are already open. */
export function useOpenSettings() {
  const navigate = useNavigate()
  const routed = useLocation()
  const real = useContext(RealLocationContext) ?? routed
  return useCallback((section: SettingsSectionId = 'general') => {
    const target = '/settings/' + section
    if (isSettingsPath(real.pathname)) {
      navigate(target, { replace: true, state: real.state })  // replace, so "close" does not step back into settings
      return
    }
    navigate(target, { state: { backgroundLocation: real } satisfies SettingsLocationState })
  }, [navigate, real])
}
