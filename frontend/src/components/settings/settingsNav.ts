import { createContext, useCallback, useContext } from 'react'
import { useLocation, useNavigate, type Location } from 'react-router-dom'

/**
 * Settings are one window over the current page (as in Lotline) that is still a real route, /settings/<section>,
 * so a section can be linked and survives a refresh. The page underneath stays mounted: opening settings remembers
 * where the person was ("background location") and the routes keep rendering that page behind the window.
 */
export type SettingsSectionId = 'general' | 'account' | 'system' | 'security' | 'email' | 'users' | 'roles' | 'departments' | 'audit' | 'about'
export type SettingsGroup = 'personal' | 'admin' | 'other'

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
  { id: 'general', group: 'personal', label: 'ทั่วไป', desc: 'ธีม สี ฟอนต์ และการจัดวางหน้าจอ', icon: 'adjustments-horizontal' },
  { id: 'account', group: 'personal', label: 'บัญชีของฉัน', desc: 'ข้อมูลของคุณและการเปลี่ยนรหัสผ่าน', icon: 'user-circle' },
  { id: 'system', group: 'admin', label: 'ตั้งค่าระบบ', desc: 'เป้าหมายเวลา SLA ขีดจำกัด อีเมล และการเก็บข้อมูล', icon: 'settings-cog', permission: 'settings.view' },
  { id: 'security', group: 'admin', label: 'ความปลอดภัย', desc: 'นโยบายรหัสผ่าน การล็อกบัญชี อายุการเข้าสู่ระบบ และบันทึกการเข้าสู่ระบบ', icon: 'shield-check', permission: 'settings.view' },
  { id: 'email', group: 'admin', label: 'อีเมล (SMTP)', desc: 'ช่องทางส่งอีเมลแจ้งเตือน เซิร์ฟเวอร์ SMTP และการทดสอบส่ง', icon: 'mail-cog', permission: 'settings.view' },
  { id: 'users', group: 'admin', label: 'ผู้ใช้งาน', desc: 'เพิ่ม แก้ไข ปิดใช้งาน และตั้งรหัสผ่านใหม่ให้ผู้ใช้', icon: 'users', permission: 'user.view' },
  { id: 'roles', group: 'admin', label: 'บทบาทและสิทธิ์', desc: 'กำหนดว่าใครทำอะไรได้ สร้างบทบาทใหม่ และดูตารางเทียบสิทธิ์', icon: 'shield-lock', permission: 'role.view' },
  { id: 'departments', group: 'admin', label: 'แผนก', desc: 'แผนกที่ใช้ในการแจ้งปัญหาและรายชื่อผู้ใช้', icon: 'building', permission: 'department.manage' },
  { id: 'audit', group: 'admin', label: 'บันทึกการใช้งาน', desc: 'ใครทำอะไร เมื่อไหร่ จากเครื่องไหน', icon: 'history', permission: 'overview.view' },
  { id: 'about', group: 'other', label: 'เกี่ยวกับ', desc: 'เวอร์ชันและสถานะการเชื่อมต่อ', icon: 'info-circle' },
]

export const SETTINGS_GROUPS: { id: SettingsGroup; label: string }[] = [
  { id: 'personal', label: 'ส่วนตัว' },
  { id: 'admin', label: 'ผู้ดูแลระบบ' },
  { id: 'other', label: 'อื่น ๆ' },
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
