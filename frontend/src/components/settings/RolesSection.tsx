import { useCallback, useEffect, useMemo, useState } from 'react'
import Badge from '../ui/Badge'
import Button from '../ui/Button'
import { Card } from '../ui/Card'
import { SelectInput, TextInput } from '../ui/Field'
import { Alert, EmptyState, Segmented, Skeleton } from '../ui/Layout'
import Modal from '../ui/Modal'
import { IssueVisibility, rolesService, type IssueVisibilityValue, type PermissionInfo, type RoleDetail, type SaveRole } from '../../services/roles.service'
import { useCan } from '../../stores/authStore'
import { apiError } from '../../utils/errors'
import { roleLabel } from '../../utils/labels'

type Tab = 'roles' | 'permissions' | 'matrix'

const visibilityLabel: Record<number, string> = {
  [IssueVisibility.Own]: 'เฉพาะเรื่องของตัวเอง',
  [IssueVisibility.Department]: 'เรื่องของแผนกตัวเอง',
  [IssueVisibility.All]: 'ทุกเรื่อง',
}

/** The name people know a role by: Thai for the built-ins, the typed name for the ones an administrator made. */
const displayName = (r: Pick<RoleDetail, 'name' | 'isSystem'>) => (r.isSystem ? roleLabel[r.name] ?? r.name : r.name)

/** Roles, the catalogue of permissions, and a roles-by-permissions grid. Reads need role.view; changes need role.manage. */
export default function RolesSection() {
  const [tab, setTab] = useState<Tab>('roles')
  const [roles, setRoles] = useState<RoleDetail[] | null>(null)
  const [catalog, setCatalog] = useState<PermissionInfo[] | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    Promise.all([rolesService.details(), rolesService.catalog()])
      .then(([r, c]) => { setRoles(r); setCatalog(c); setError('') })
      .catch((err) => setError(apiError(err, 'โหลดบทบาทไม่สำเร็จ')))
  }, [])
  useEffect(() => { load() }, [load])

  return (
    <div className="space-y-4">
      <Segmented<Tab> label="มุมมอง" value={tab} onChange={setTab} options={[
        { value: 'roles', label: 'บทบาท', count: roles?.length },
        { value: 'permissions', label: 'สิทธิ์', count: catalog?.length },
        { value: 'matrix', label: 'ตารางเทียบ' },
      ]} />
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      {!roles || !catalog ? (
        !error && <div className="space-y-3" role="status" aria-label="กำลังโหลด">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-14" />)}</div>
      ) : tab === 'roles' ? <RolesTab roles={roles} catalog={catalog} onChanged={load} />
        : tab === 'permissions' ? <PermissionsTab roles={roles} catalog={catalog} />
        : <MatrixTab roles={roles} catalog={catalog} />}
    </div>
  )
}

// ───────────── roles ─────────────

function RolesTab({ roles, catalog, onChanged }: { roles: RoleDetail[]; catalog: PermissionInfo[]; onChanged: () => void }) {
  const canManage = useCan('role.manage')
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<RoleDetail | 'new' | null>(null)
  const [deleting, setDeleting] = useState<RoleDetail | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const shown = useMemo(() => {
    const q = search.trim().toLowerCase()
    return roles.filter((r) => !q || r.name.toLowerCase().includes(q) || displayName(r).toLowerCase().includes(q) || r.description.toLowerCase().includes(q))
  }, [roles, search])

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      await rolesService.remove(deleting.id)
      setDeleting(null)
      onChanged()
    } catch (err) {
      setError(apiError(err, 'ลบบทบาทไม่สำเร็จ'))
      setDeleting(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3">
      {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="relative w-full sm:w-72">
          <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="ค้นหาบทบาท" placeholder="ค้นหาบทบาท"
            className="h-9 w-full rounded-control border border-line-strong bg-card pl-9 pr-3 text-[13px]" />
        </div>
        {canManage && <Button variant="primary" icon="plus" onClick={() => setEditing('new')}>เพิ่มบทบาท</Button>}
      </div>

      <Card>
        {shown.length === 0 ? (
          <EmptyState icon="shield-lock" title="ไม่พบบทบาท">ลองเปลี่ยนคำค้นหา</EmptyState>
        ) : (
          <ul className="divide-y divide-line">
            {shown.map((r) => (
              <li key={r.id} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3.5">
                <div className="min-w-0 flex-1 basis-56">
                  <p className="flex items-center gap-2 text-sm font-semibold text-ink">
                    <span className="truncate">{displayName(r)}</span>
                    <Badge tone={r.isSystem ? 'gray' : 'blue'}>{r.isSystem ? 'ของระบบ' : 'ตั้งเอง'}</Badge>
                  </p>
                  <p className="mt-0.5 truncate text-xs text-ink-3">{r.isSystem && displayName(r) !== r.name ? `${r.name} · ` : ''}{r.description || 'ไม่มีคำอธิบาย'}</p>
                </div>
                <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-[13px] text-ink-2">
                  <span title="จำนวนสิทธิ์"><i className="ti ti-key mr-1 text-ink-3" aria-hidden="true" />{r.permissions.length} สิทธิ์</span>
                  <span title="ผู้ใช้ที่ใช้บทบาทนี้"><i className="ti ti-users mr-1 text-ink-3" aria-hidden="true" />{r.userCount} คน</span>
                  <span title="ขอบเขตการเห็นเรื่อง"><i className="ti ti-eye mr-1 text-ink-3" aria-hidden="true" />{visibilityLabel[r.issueVisibility]}</span>
                </div>
                <div className="flex items-center gap-1">
                  <Button size="sm" variant="ghost" icon={r.isSystem || !canManage ? 'eye' : 'pencil'} onClick={() => setEditing(r)}>
                    {r.isSystem || !canManage ? 'ดูสิทธิ์' : 'แก้ไข'}
                  </Button>
                  {canManage && !r.isSystem && (
                    <Button size="sm" variant="ghost" icon="trash" onClick={() => setDeleting(r)}>ลบ</Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      {editing && (
        <RoleDialog role={editing === 'new' ? null : editing} catalog={catalog} readOnly={editing !== 'new' && (editing.isSystem || !canManage)}
          onClose={() => setEditing(null)} onSaved={() => { setEditing(null); onChanged() }} />
      )}

      <Modal open={deleting !== null} onClose={() => setDeleting(null)} width="sm" title="ลบบทบาทนี้?"
        footer={<>
          <Button onClick={() => setDeleting(null)}>ยกเลิก</Button>
          <Button variant="danger" loading={busy} onClick={remove}>ลบบทบาท</Button>
        </>}>
        <p className="text-sm text-ink-2">
          บทบาท "{deleting?.name}" จะถูกลบถาวร{deleting && deleting.userCount > 0 ? ` แต่ยังมีผู้ใช้ ${deleting.userCount} คนใช้อยู่ จึงลบไม่ได้จนกว่าจะย้ายไปบทบาทอื่น` : ''}
        </p>
      </Modal>
    </div>
  )
}

function RoleDialog({ role, catalog, readOnly, onClose, onSaved }: {
  role: RoleDetail | null
  catalog: PermissionInfo[]
  readOnly: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const [form, setForm] = useState<SaveRole>({
    name: role?.name ?? '',
    description: role?.description ?? '',
    permissions: role?.permissions ?? ['issue.create', 'issue.view'],
    issueVisibility: role?.issueVisibility ?? IssueVisibility.Own,
    seesAllDepartments: role?.seesAllDepartments ?? false,
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const groups = useMemo(() => {
    const map = new Map<string, PermissionInfo[]>()
    for (const p of catalog) map.set(p.group, [...(map.get(p.group) ?? []), p])
    return [...map.entries()]
  }, [catalog])

  const toggle = (name: string) => setForm((f) => ({
    ...f, permissions: f.permissions.includes(name) ? f.permissions.filter((p) => p !== name) : [...f.permissions, name],
  }))

  const nameProblem = form.name.trim().length > 0 && form.name.trim().length < 2 ? 'ชื่อบทบาทต้องยาวอย่างน้อย 2 ตัวอักษร' : ''
  const ready = form.name.trim().length >= 2 && form.permissions.length > 0

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (readOnly || !ready) return
    setSaving(true)
    setError('')
    try {
      if (role) await rolesService.update(role.id, form)
      else await rolesService.create(form)
      onSaved()
    } catch (err) {
      setError(apiError(err, 'บันทึกบทบาทไม่สำเร็จ'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} width="lg" title={readOnly ? `สิทธิ์ของ ${role ? displayName(role) : ''}` : role ? 'แก้ไขบทบาท' : 'เพิ่มบทบาท'}
      footer={<>
        <Button onClick={onClose}>{readOnly ? 'ปิด' : 'ยกเลิก'}</Button>
        {!readOnly && <Button type="submit" form="role-form" variant="primary" loading={saving} disabled={!ready}>บันทึก</Button>}
      </>}>
      <form id="role-form" onSubmit={submit} className="space-y-5">
        {error && <Alert tone="error" onClose={() => setError('')}>{error}</Alert>}
        {readOnly && role?.isSystem && (
          <Alert tone="info">บทบาทของระบบมีสิทธิ์ที่ตั้งตายตัวในโค้ด แก้ไขหรือลบไม่ได้ ถ้าต้องการแบบอื่นให้เพิ่มบทบาทใหม่</Alert>
        )}
        <div className="grid gap-4 sm:grid-cols-2">
          <TextInput label="ชื่อบทบาท" required value={form.name} maxLength={50} disabled={readOnly} error={nameProblem || undefined}
            onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <TextInput label="คำอธิบาย" value={form.description} maxLength={200} disabled={readOnly}
            onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <SelectInput label="เห็นเรื่องแจ้งปัญหาของ" value={form.issueVisibility} disabled={readOnly}
            hint={'ใช้ร่วมกับสิทธิ์ ดูเรื่องแจ้งปัญหา'}
            options={Object.entries(visibilityLabel).map(([value, label]) => ({ value, label }))}
            onChange={(e) => setForm({ ...form, issueVisibility: Number(e.target.value) as IssueVisibilityValue })} />
          <label className="flex items-start gap-2.5 self-end pb-2 text-[13px] text-ink">
            <input type="checkbox" className="mt-0.5 h-4 w-4 accent-[var(--lt-accent)]" checked={form.seesAllDepartments} disabled={readOnly}
              onChange={(e) => setForm({ ...form, seesAllDepartments: e.target.checked })} />
            <span>เห็นรายชื่อผู้ใช้ทุกแผนก<span className="block text-xs text-ink-3">ถ้าไม่เลือก เห็นเฉพาะแผนกตัวเอง</span></span>
          </label>
        </div>

        <fieldset className="space-y-4">
          <legend className="mb-1 text-[13px] font-semibold text-ink">สิทธิ์ที่บทบาทนี้ได้รับ <span className="font-normal text-ink-3">({form.permissions.length})</span></legend>
          {groups.map(([group, items]) => (
            <div key={group}>
              <p className="border-b border-line pb-1 text-xs font-bold text-ink-3">{group}</p>
              <ul className="divide-y divide-line">
                {items.map((p) => (
                  <li key={p.name}>
                    <label className="flex cursor-pointer items-start gap-3 py-2.5">
                      <input type="checkbox" className="mt-0.5 h-4 w-4 accent-[var(--lt-accent)]" checked={form.permissions.includes(p.name)} disabled={readOnly}
                        onChange={() => toggle(p.name)} />
                      <span className="min-w-0 text-[13px]">
                        <span className="font-medium text-ink">{p.label}</span>
                        <span className="ml-2 font-mono text-[11px] text-ink-3">{p.name}</span>
                        <span className="block text-xs text-ink-3">{p.description}</span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            </div>
          ))}
          {!readOnly && <p className="text-xs text-ink-3">ให้ได้เฉพาะสิทธิ์และขอบเขตที่คุณมีเอง ระบบจะตรวจอีกครั้งตอนบันทึก การเปลี่ยนมีผลกับผู้ใช้ทันที</p>}
        </fieldset>
      </form>
    </Modal>
  )
}

// ───────────── permissions ─────────────

function PermissionsTab({ roles, catalog }: { roles: RoleDetail[]; catalog: PermissionInfo[] }) {
  const [search, setSearch] = useState('')
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return catalog.filter((p) => !q || p.name.includes(q) || p.label.toLowerCase().includes(q) || p.description.toLowerCase().includes(q) || p.group.toLowerCase().includes(q))
  }, [catalog, search])

  return (
    <div className="space-y-3">
      <div className="relative w-full sm:w-72">
        <i className="ti ti-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-3" aria-hidden="true" />
        <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="ค้นหาสิทธิ์" placeholder="ค้นหาสิทธิ์"
          className="h-9 w-full rounded-control border border-line-strong bg-card pl-9 pr-3 text-[13px]" />
      </div>
      <Card>
        {rows.length === 0 ? <EmptyState icon="key" title="ไม่พบสิทธิ์">ลองเปลี่ยนคำค้นหา</EmptyState> : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-left text-[13px]">
              <thead>
                <tr className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                  <th scope="col" className="px-5 py-3">สิทธิ์</th>
                  <th scope="col" className="px-3 py-3">กลุ่ม</th>
                  <th scope="col" className="px-3 py-3">บทบาทที่ได้รับ</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {rows.map((p) => {
                  const holders = roles.filter((r) => r.permissions.includes(p.name))
                  return (
                    <tr key={p.name}>
                      <td className="px-5 py-3">
                        <p className="font-medium text-ink">{p.label}</p>
                        <p className="font-mono text-[11px] text-ink-3">{p.name}</p>
                        <p className="mt-0.5 text-xs text-ink-3">{p.description}</p>
                      </td>
                      <td className="px-3 py-3 text-ink-2">{p.group}</td>
                      <td className="px-3 py-3">
                        <div className="flex flex-wrap gap-1">
                          {holders.map((r) => <Badge key={r.id} tone={r.isSystem ? 'gray' : 'blue'}>{displayName(r)}</Badge>)}
                          {holders.length === 0 && <span className="text-ink-3">ยังไม่มีบทบาทใดได้รับ</span>}
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}

// ───────────── matrix ─────────────

function MatrixTab({ roles, catalog }: { roles: RoleDetail[]; catalog: PermissionInfo[] }) {
  const [onlyHeld, setOnlyHeld] = useState(false)
  const rows = catalog.filter((p) => !onlyHeld || roles.some((r) => r.permissions.includes(p.name)))

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label className="flex items-center gap-2 text-[13px] text-ink">
          <input type="checkbox" className="h-4 w-4 accent-[var(--lt-accent)]" checked={onlyHeld} onChange={(e) => setOnlyHeld(e.target.checked)} />
          แสดงเฉพาะสิทธิ์ที่มีบทบาทใช้
        </label>
        <span className="text-xs text-ink-3">{rows.length} สิทธิ์ × {roles.length} บทบาท · ดูอย่างเดียว แก้ที่แท็บ "บทบาท"</span>
      </div>
      <Card>
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-[13px]">
            <thead>
              <tr className="border-b border-line bg-surface text-xs font-semibold text-ink-3">
                <th scope="col" className="sticky left-0 z-10 min-w-[220px] bg-surface px-5 py-3 text-left">สิทธิ์</th>
                {roles.map((r) => (
                  <th key={r.id} scope="col" className="min-w-[84px] px-2 py-3 text-center" title={r.description}>{displayName(r)}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {rows.map((p) => (
                <tr key={p.name}>
                  <th scope="row" className="sticky left-0 z-10 bg-card px-5 py-2.5 text-left font-normal">
                    <span className="font-medium text-ink">{p.label}</span>
                    <span className="block font-mono text-[11px] text-ink-3">{p.name}</span>
                  </th>
                  {roles.map((r) => {
                    const has = r.permissions.includes(p.name)
                    return (
                      <td key={r.id} className={`px-2 py-2.5 text-center ${has ? 'bg-green-soft' : ''}`}
                        aria-label={`${displayName(r)}: ${has ? 'มี' : 'ไม่มี'}`}>
                        {has ? <i className="ti ti-check text-base text-green-ink" aria-hidden="true" /> : <span className="text-ink-3" aria-hidden="true">·</span>}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}
