import { useCallback, useEffect, useMemo, useState } from 'react'
import AddUserModal from '../../components/admin/AddUserModal'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card, Stat } from '../../components/ui/Card'
import { CompactSelect } from '../../components/ui/Field'
import { Alert, Avatar, EmptyState, Page, PageHeader, Pagination, Skeleton } from '../../components/ui/Layout'
import Modal from '../../components/ui/Modal'
import api from '../../services/api'
import { useAuthStore, useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { roleLabel } from '../../utils/labels'
import { usePaging } from '../../utils/paging'
import type { Tone } from '../../types/issue'

interface UserDto {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  department: string | null
  isActive: boolean
}

const roleTone: Record<string, Tone> = {
  SuperAdmin: 'orange', Director: 'blue', Manager: 'blue', TeamLeader: 'green', Staff: 'gray', Viewer: 'gray', ITSupport: 'amber',
}

export default function UsersPage() {
  const me = useAuthStore((s) => s.user)
  const canCreate = useCan('user.create')
  const canEdit = useCan('user.edit')
  const [users, setUsers] = useState<UserDto[]>([])
  const [roles, setRoles] = useState<{ id: string; name: string }[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [showModal, setShowModal] = useState(false)
  const [error, setError] = useState('')
  const [confirmDeactivate, setConfirmDeactivate] = useState<UserDto | null>(null)
  const [busy, setBusy] = useState(false)

  // State is only set in the promise callbacks (never synchronously inside the effect).
  const loadUsers = useCallback(() => (
    api.get('/api/users')
      .then((res) => { setUsers(res.data); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดรายชื่อผู้ใช้ไม่สำเร็จ')))
      .finally(() => setLoading(false))
  ), [])

  useEffect(() => { loadUsers() }, [loadUsers])
  useEffect(() => { if (canEdit) api.get('/api/roles').then((r) => setRoles(r.data)).catch(() => {}) }, [canEdit])

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    return users.filter((u) => !term || `${u.firstName} ${u.lastName} ${u.email}`.toLowerCase().includes(term))
  }, [users, search])

  const paging = usePaging(filtered, search)

  const changeRole = async (user: UserDto, roleName: string) => {
    setBusy(true)
    try {
      await api.put(`/api/users/${user.id}/role`, { roleName })
      await loadUsers()
    } catch (err) {
      setError(apiError(err))
    } finally {
      setBusy(false)
    }
  }

  const deactivate = async () => {
    if (!confirmDeactivate) return
    setBusy(true)
    try {
      await api.put(`/api/users/${confirmDeactivate.id}/deactivate`)
      setConfirmDeactivate(null)
      await loadUsers()
    } catch (err) {
      setError(apiError(err))
      setConfirmDeactivate(null)
    } finally {
      setBusy(false)
    }
  }

  const active = users.filter((u) => u.isActive).length

  return (
    <Page wide>
      <PageHeader title="ผู้ใช้งาน" subtitle={`${users.length} บัญชีในระบบ`}
        actions={canCreate ? <Button variant="primary" icon="user-plus" onClick={() => setShowModal(true)}>เพิ่มผู้ใช้งาน</Button> : undefined} />

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      <section aria-label="สรุป" className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
        <Stat dark label="ทั้งหมด" value={users.length} icon="users" />
        <Stat label="ใช้งานอยู่" value={active} icon="circle-check" tone="green" />
        <Stat label="ปิดใช้งาน" value={users.length - active} icon="user-off" tone={users.length - active ? 'red' : 'default'} />
        <Stat label="แผนก" value={new Set(users.map((u) => u.department).filter(Boolean)).size} icon="building" />
      </section>

      <Card>
        <div className="flex items-center border-b border-line p-4">
          <div className="relative w-full sm:ml-auto sm:w-72">
            <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
            <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="ค้นหาผู้ใช้"
              placeholder="ค้นหาชื่อหรืออีเมล"
              className="h-9 w-full rounded-control border border-line bg-white pl-9 pr-3 text-[13px] focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
          </div>
        </div>

        {loading ? (
          <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}</div>
        ) : filtered.length === 0 ? (
          <EmptyState icon="users" title="ไม่พบผู้ใช้">{search ? 'ลองเปลี่ยนคำค้นหา' : 'ยังไม่มีผู้ใช้ในส่วนที่คุณดูได้'}</EmptyState>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] text-left">
                <thead>
                  <tr className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                    <th scope="col" className="px-5 py-3">ผู้ใช้</th>
                    <th scope="col" className="px-3 py-3">บทบาท</th>
                    <th scope="col" className="px-3 py-3">แผนก</th>
                    <th scope="col" className="px-3 py-3">สถานะ</th>
                    {canEdit && <th scope="col" className="px-5 py-3 text-right">จัดการ</th>}
                  </tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {paging.pageItems.map((u) => {
                    const isMe = u.email.toLowerCase() === me?.email.toLowerCase()
                    return (
                      <tr key={u.id} className={u.isActive ? '' : 'bg-surface'}>
                        <td className="px-5 py-3.5">
                          <div className="flex items-center gap-3">
                            <Avatar name={`${u.firstName} ${u.lastName}`} size={36} />
                            <div className="min-w-0">
                              <p className="truncate text-sm font-medium">{u.firstName} {u.lastName}{isMe && <span className="ml-1.5 text-xs font-normal text-ink-3">(คุณ)</span>}</p>
                              <p className="truncate text-xs text-ink-3">{u.email}</p>
                            </div>
                          </div>
                        </td>
                        <td className="px-3 py-3.5">
                          {canEdit && !isMe && u.isActive && roles.length > 0 ? (
                            <CompactSelect aria-label={`บทบาทของ ${u.firstName}`} value={u.role} disabled={busy}
                              onChange={(e) => changeRole(u, e.target.value)}>
                              {roles.map((r) => <option key={r.id} value={r.name}>{roleLabel[r.name] ?? r.name}</option>)}
                            </CompactSelect>
                          ) : <Badge tone={roleTone[u.role] ?? 'gray'}>{roleLabel[u.role] ?? u.role}</Badge>}
                        </td>
                        <td className="px-3 py-3.5 text-[13px] text-ink-2">{u.department ?? <span className="text-ink-3">ไม่ระบุ</span>}</td>
                        <td className="px-3 py-3.5">
                          <Badge tone={u.isActive ? 'green' : 'gray'} dot>{u.isActive ? 'ใช้งานอยู่' : 'ปิดใช้งาน'}</Badge>
                        </td>
                        {canEdit && (
                          <td className="px-5 py-3.5 text-right">
                            {u.isActive && !isMe && (
                              <Button size="sm" variant="ghost" icon="user-off" onClick={() => setConfirmDeactivate(u)}>ปิดใช้งาน</Button>
                            )}
                          </td>
                        )}
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <Pagination page={paging.page} pageSize={paging.pageSize} total={paging.total}
              onChange={paging.setPage} onPageSizeChange={paging.setPageSize} />
          </>
        )}
      </Card>

      {showModal && <AddUserModal onClose={() => setShowModal(false)} onSuccess={loadUsers} />}

      <Modal open={confirmDeactivate !== null} onClose={() => setConfirmDeactivate(null)} width="sm" title="ปิดใช้งานบัญชีนี้?"
        footer={<>
          <Button onClick={() => setConfirmDeactivate(null)}>ยกเลิก</Button>
          <Button variant="danger" loading={busy} onClick={deactivate}>ปิดใช้งาน</Button>
        </>}>
        <p className="text-sm text-ink-2">
          {confirmDeactivate?.firstName} {confirmDeactivate?.lastName} จะเข้าสู่ระบบไม่ได้ทันที (เปิดกลับต้องให้ผู้พัฒนาแก้ในฐานข้อมูล)
        </p>
      </Modal>
    </Page>
  )
}
