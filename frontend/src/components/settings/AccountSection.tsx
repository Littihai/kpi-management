import { useState } from 'react'
import { Card, CardHeader } from '../ui/Card'
import { Alert, Avatar } from '../ui/Layout'
import ChangePasswordForm from './ChangePasswordForm'
import { useAuthStore } from '../../stores/authStore'
import { roleLabel } from '../../utils/labels'

/** Who the signed-in person is, and the one thing they may change about themselves: their password. */
export default function AccountSection() {
  const user = useAuthStore((s) => s.user)
  const updateUser = useAuthStore((s) => s.updateUser)
  const [done, setDone] = useState(false)

  if (!user) return null

  const rows: [string, string][] = [
    ['อีเมล (ใช้เข้าสู่ระบบ)', user.email],
    ['บทบาท', roleLabel[user.role] ?? user.role],
    ['แผนก', user.departmentName ?? 'ไม่ระบุ'],
    ['สิทธิ์ที่ได้รับ', `${user.permissions?.length ?? 0} รายการ`],
  ]

  return (
    <div className="space-y-5">
      <Card>
        <div className="flex items-center gap-4 px-5 py-4">
          <Avatar name={user.fullName} size={52} />
          <div className="min-w-0">
            <p className="truncate text-base font-semibold text-ink">{user.fullName}</p>
            <p className="truncate text-[13px] text-ink-3">{roleLabel[user.role] ?? user.role}</p>
          </div>
        </div>
        <dl className="divide-y divide-line border-t border-line text-[13px]">
          {rows.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-4 px-5 py-2.5">
              <dt className="shrink-0 text-ink-3">{label}</dt>
              <dd className="min-w-0 truncate text-right font-medium text-ink">{value}</dd>
            </div>
          ))}
        </dl>
        <p className="border-t border-line px-5 py-3 text-xs text-ink-3">ชื่อ อีเมล แผนก และบทบาท แก้ไขโดยผู้ดูแลระบบ</p>
      </Card>

      <Card>
        <CardHeader eyebrow="ความปลอดภัย" title="เปลี่ยนรหัสผ่าน" />
        <div className="space-y-4 px-5 pb-5 pt-4">
          {done && <Alert tone="success" onClose={() => setDone(false)}>เปลี่ยนรหัสผ่านแล้ว ใช้รหัสใหม่ในการเข้าสู่ระบบครั้งถัดไป</Alert>}
          <ChangePasswordForm onChanged={() => { setDone(true); updateUser({ passwordChangeRequired: false, passwordChangeReason: null }) }} />
        </div>
      </Card>
    </div>
  )
}
