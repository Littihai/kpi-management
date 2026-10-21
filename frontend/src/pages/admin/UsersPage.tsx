import { useCallback, useEffect, useRef, useState } from 'react'
import AddUserModal from '../../components/admin/AddUserModal'
import { EditUserModal, ResetPasswordModal } from '../../components/admin/UserDialogs'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { CompactSelect } from '../../components/ui/Field'
import { Alert, Avatar, EmptyState, Page, PageHeader, Pagination, Skeleton } from '../../components/ui/Layout'
import Modal from '../../components/ui/Modal'
import api from '../../services/api'
import { orgService, type UserRow } from '../../services/org.service'
import { useAuthStore, useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { roleLabel } from '../../utils/labels'
import { usePageSize } from '../../utils/paging'
import type { Paged, Tone } from '../../types/issue'

const roleTone: Record<string, Tone> = {
  SuperAdmin: 'orange', Director: 'blue', Manager: 'blue', TeamLeader: 'green', Staff: 'gray', Viewer: 'gray', ITSupport: 'amber',
}

export default function UsersPage() {
  const me = useAuthStore((s) => s.user)
  const canCreate = useCan('user.create')
  const canEdit = useCan('user.edit')
  const [pageSize, setPageSize] = usePageSize()

  const [data, setData] = useState<Paged<UserRow> | null>(null)
  const [roles, setRoles] = useState<{ id: string; name: string }[]>([])
  const [departments, setDepartments] = useState<{ id: string; name: string }[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const [searchText, setSearchText] = useState('')
  const [search, setSearch] = useState('')
  const [role, setRole] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)

  const [showAdd, setShowAdd] = useState(false)
  const [editing, setEditing] = useState<UserRow | null>(null)
  const [resetting, setResetting] = useState<UserRow | null>(null)
  const [confirmDeactivate, setConfirmDeactivate] = useState<UserRow | null>(null)

  // Typing searches after a short pause, and always from the first page.
  useEffect(() => {
    const id = window.setTimeout(() => { setSearch(searchText.trim()); setPage(1) }, 350)
    return () => window.clearTimeout(id)
  }, [searchText])

  // Only the newest request may show its result (a slow older one must not overwrite it).
  const latest = useRef(0)
  const load = useCallback(() => {
    const mine = ++latest.current
    return orgService.users({
      search: search || undefined, role: role || undefined, departmentId: departmentId || undefined,
      active: status === '' ? undefined : status === 'active', page, pageSize,
    })
      .then((result) => { if (mine === latest.current) { setData(result); setError('') } })
      .catch((err) => { if (mine === latest.current) setError(apiError(err, 'โหลดรายชื่อผู้ใช้ไม่สำเร็จ')) })
  }, [search, role, departmentId, status, page, pageSize])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    if (!canEdit) return
    api.get('/api/roles').then((r) => setRoles(r.data)).catch(() => {})
    orgService.departmentOptions().then(setDepartments).catch(() => {})
  }, [canEdit])

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    try {
      await action()
      await load()
    } catch (err) {
      setError(apiError(err))
    } finally {
      setBusy(false)
    }
  }

  const filtered = Boolean(search || role || departmentId || status)
  const clear = () => { setSearchText(''); setSearch(''); setRole(''); setDepartmentId(''); setStatus(''); setPage(1) }
  const pick = (setter: (v: string) => void) => (e: React.ChangeEvent<HTMLSelectElement>) => { setter(e.target.value); setPage(1) }

  return (
    <Page wide>
      <PageHeader title="ผู้ใช้งาน" subtitle={data ? `${data.total} บัญชี${filtered ? 'ตามเงื่อนไขที่เลือก' : 'ในระบบ'}` : undefined}
        actions={canCreate ? <Button variant="primary" icon="user-plus" onClick={() => setShowAdd(true)}>เพิ่มผู้ใช้งาน</Button> : undefined} />

      {error && <div className="mb-4"><Alert tone="error" onClose={() => setError('')}>{error}</Alert></div>}

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-4 py-3">
          <div className="relative w-full sm:w-72">
            <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
            <input type="search" value={searchText} onChange={(e) => setSearchText(e.target.value)} aria-label="ค้นหาผู้ใช้"
              placeholder="ค้นหาชื่อหรืออีเมล"
              className="h-9 w-full rounded-control border border-line-strong bg-card pl-9 pr-3 text-[13px]" />
          </div>
          <CompactSelect aria-label="กรองตามบทบาท" value={role} onChange={pick(setRole)}>
            <option value="">ทุกบทบาท</option>
            {Object.keys(roleLabel).map((name) => <option key={name} value={name}>{roleLabel[name]}</option>)}
          </CompactSelect>
          <CompactSelect aria-label="กรองตามแผนก" value={departmentId} onChange={pick(setDepartmentId)}>
            <option value="">ทุกแผนก</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </CompactSelect>
          <CompactSelect aria-label="กรองตามสถานะ" value={status} onChange={pick(setStatus)}>
            <option value="">ทุกสถานะ</option>
            <option value="active">ใช้งานอยู่</option>
            <option value="inactive">ปิดใช้งาน</option>
          </CompactSelect>
          {filtered && <Button size="sm" variant="ghost" icon="x" onClick={clear}>ล้างตัวกรอง</Button>}
        </div>

        {!data ? (
          <div className="space-y-3 p-4" role="status" aria-label="กำลังโหลด">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-14" />)}</div>
        ) : data.items.length === 0 ? (
          <EmptyState icon="users" title="ไม่พบผู้ใช้">{filtered ? 'ลองเปลี่ยนหรือล้างตัวกรอง' : 'ยังไม่มีผู้ใช้ในส่วนที่คุณดูได้'}</EmptyState>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] text-left">
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
                  {data.items.map((u) => {
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
                              onChange={(e) => run(() => orgService.changeRole(u.id, e.target.value))}>
                              {roles.map((r) => <option key={r.id} value={r.name}>{roleLabel[r.name] ?? r.name}</option>)}
                            </CompactSelect>
                          ) : <Badge tone={roleTone[u.role] ?? 'gray'}>{roleLabel[u.role] ?? u.role}</Badge>}
                        </td>
                        <td className="px-3 py-3.5 text-[13px] text-ink-2">{u.department ?? <span className="text-ink-3">ไม่ระบุ</span>}</td>
                        <td className="px-3 py-3.5">
                          <Badge tone={u.isActive ? 'green' : 'gray'} dot>{u.isActive ? 'ใช้งานอยู่' : 'ปิดใช้งาน'}</Badge>
                          {u.lockedUntil && <Badge tone="amber" icon="lock" className="ml-1.5" >ถูกล็อก</Badge>}
                        </td>
                        {canEdit && (
                          <td className="px-5 py-3.5 text-right">
                            <div className="flex flex-wrap items-center justify-end gap-x-3 gap-y-1">
                              <Button size="sm" variant="ghost" icon="pencil" onClick={() => setEditing(u)}>แก้ไข</Button>
                              <Button size="sm" variant="ghost" icon="key" onClick={() => setResetting(u)}>รหัสผ่าน</Button>
                              {u.isActive && !isMe && (
                                <Button size="sm" variant="ghost" icon="user-off" onClick={() => setConfirmDeactivate(u)}>ปิดใช้งาน</Button>
                              )}
                              {u.lockedUntil && (
                                <Button size="sm" variant="ghost" icon="lock-open" disabled={busy}
                                  onClick={() => run(() => orgService.unlock(u.id))}>ปลดล็อก</Button>
                              )}
                              {!u.isActive && (
                                <Button size="sm" variant="ghost" icon="user-check" disabled={busy}
                                  onClick={() => run(() => orgService.reactivate(u.id))}>เปิดใช้งาน</Button>
                              )}
                            </div>
                          </td>
                        )}
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onChange={setPage}
              onPageSizeChange={(size) => { setPageSize(size); setPage(1) }} />
          </>
        )}
      </Card>

      {showAdd && <AddUserModal onClose={() => setShowAdd(false)} onSuccess={load} />}
      {editing && <EditUserModal user={editing} onClose={() => setEditing(null)} onSaved={load} />}
      {resetting && <ResetPasswordModal user={resetting} onClose={() => setResetting(null)} onDone={load} />}

      <Modal open={confirmDeactivate !== null} onClose={() => setConfirmDeactivate(null)} width="sm" title="ปิดใช้งานบัญชีนี้?"
        footer={<>
          <Button onClick={() => setConfirmDeactivate(null)}>ยกเลิก</Button>
          <Button variant="danger" loading={busy} onClick={async () => {
            const target = confirmDeactivate
            setConfirmDeactivate(null)
            if (target) await run(() => orgService.deactivate(target.id))
          }}>ปิดใช้งาน</Button>
        </>}>
        <p className="text-sm text-ink-2">
          {confirmDeactivate?.firstName} {confirmDeactivate?.lastName} จะเข้าสู่ระบบไม่ได้ทันที เปิดกลับได้จากหน้านี้ภายหลัง
        </p>
      </Modal>
    </Page>
  )
}
