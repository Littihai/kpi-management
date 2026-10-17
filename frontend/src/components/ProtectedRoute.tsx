import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { EmptyState, Page } from './ui/Layout'

export default function ProtectedRoute({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />
}

/**
 * Hides a page from people whose role lacks the permission. The server enforces this too;
 * this just explains instead of showing a broken page when someone types the address.
 */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const allowed = useAuthStore((s) => s.user?.permissions?.includes(permission) ?? false)
  const loaded = useAuthStore((s) => s.user?.permissions !== undefined)

  // Permissions arrive with the first profile refresh; do not flash "no access" before then.
  if (!loaded) return <Page><div className="py-20 text-center text-ink-3" role="status">กำลังตรวจสอบสิทธิ์…</div></Page>
  if (allowed) return <>{children}</>

  return (
    <Page>
      <EmptyState icon="lock" title="คุณไม่มีสิทธิ์เข้าหน้านี้">
        หน้านี้เปิดให้เฉพาะผู้ที่ได้รับอนุญาต หากคิดว่าควรเข้าได้ โปรดติดต่อผู้ดูแลระบบ
      </EmptyState>
    </Page>
  )
}
