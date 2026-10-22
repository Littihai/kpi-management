import type { ReactNode } from 'react'
import { Card, CardHeader } from '../ui/Card'
import { Alert } from '../ui/Layout'
import type { DatabaseReport } from '../../services/database.service'
import { problemText } from './databaseText'

function Row({ ok, warn, title, children }: { ok: boolean; warn?: boolean; title: string; children?: ReactNode }) {
  const icon = ok ? 'circle-check' : warn ? 'alert-triangle' : 'circle-x'
  const color = ok ? 'text-green-ink' : warn ? 'text-amber-ink' : 'text-red-ink'
  return (
    <li className="flex items-start gap-3 px-5 py-3">
      <i className={`ti ti-${icon} mt-0.5 text-lg ${color}`} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-ink">{title}</p>
        {children && <div className="mt-0.5 text-[13px] text-ink-2">{children}</div>}
      </div>
    </li>
  )
}

/** What the server says about itself: engine, version, database name, collation. */
export function DatabaseFacts({ report }: { report: DatabaseReport }) {
  const r = report
  const details: [string, string][] = [
    ['ฐานข้อมูล', r.engine === 'SqlServer' ? 'SQL Server' : r.engine === 'PostgreSql' ? 'PostgreSQL' : r.engine],
    ...(r.serverVersion ? [['เวอร์ชันเซิร์ฟเวอร์', r.serverVersion] as [string, string]] : []),
    ...(r.databaseName ? [['ชื่อฐานข้อมูล', r.databaseName] as [string, string]] : []),
    ...(r.collation ? [['การเรียงลำดับตัวอักษร', r.collation] as [string, string]] : []),
  ]
  return (
    <dl className="divide-y divide-line pt-3 text-[13px]">
      {details.map(([label, value]) => (
        <div key={label} className="flex justify-between gap-4 px-5 py-2.5">
          <dt className="shrink-0 text-ink-3">{label}</dt>
          <dd className="min-w-0 break-all text-right font-medium text-ink">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

/** Why the database could not be reached, in words an administrator can act on. */
export function ConnectionProblem({ report }: { report: DatabaseReport }) {
  return (
    <Alert tone="error">
      <p className="font-medium">{problemText[report.connectionProblem ?? 'other']}</p>
      {report.connectionMessage && report.connectionProblem === 'other' && <p className="mt-1 break-all text-xs">{report.connectionMessage}</p>}
    </Alert>
  )
}

/** The list of what the program needs and whether the database has it. */
export function DatabaseChecklist({ report, adminHint }: { report: DatabaseReport; adminHint?: ReactNode }) {
  const r = report
  return (
    <Card>
      <CardHeader eyebrow="รายการตรวจ" title="โปรแกรมต้องการอะไร และมีครบหรือไม่" />
      <ul className="divide-y divide-line pt-3">
        <Row ok title="เชื่อมต่อฐานข้อมูลได้">บัญชีที่โปรแกรมใช้เปิดฐานข้อมูลนี้ได้</Row>
        {r.caseInsensitive !== null && (
          <Row ok={r.caseInsensitive} warn title="การเรียงลำดับตัวอักษรไม่สนตัวพิมพ์ (CI)">
            {r.caseInsensitive ? 'อีเมลที่ต่างกันแค่ตัวพิมพ์จะถือว่าซ้ำกัน ตามที่โปรแกรมต้องการ'
              : 'ฐานข้อมูลนี้สนตัวพิมพ์ อีเมลที่ต่างกันแค่ตัวพิมพ์เล็กใหญ่จะไม่ถูกมองว่าซ้ำ สร้างฐานข้อมูลใหม่ด้วยการเรียงลำดับแบบ CI (เช่น Thai_CI_AS) จะแก้ได้'}
          </Row>
        )}
        <Row ok={r.pendingMigrations.length === 0} title="โครงสร้างตารางเป็นเวอร์ชันล่าสุด">
          {r.pendingMigrations.length === 0
            ? `ใช้ migration แล้ว ${r.appliedMigrations.length} รายการ`
            : `ยังไม่ได้ใช้ ${r.pendingMigrations.length} รายการ: ${r.pendingMigrations.join(', ')}`}
        </Row>
        <Row ok={r.missingTables.length === 0} title="มีตารางครบ">
          {r.missingTables.length === 0 ? 'ทุกตารางที่โปรแกรมต้องการมีอยู่' : `ขาด ${r.missingTables.length} ตาราง: ${r.missingTables.join(', ')}`}
        </Row>
        <Row ok={r.missingColumns.length === 0} title="มีคอลัมน์ครบ">
          {r.missingColumns.length === 0 ? 'ทุกคอลัมน์ที่โปรแกรมต้องการมีอยู่' : (
            <ul className="list-inside list-disc">{r.missingColumns.map((c) => <li key={c.table + c.column}><span className="font-mono text-[12px]">{c.table}.{c.column}</span> ({c.storeType})</li>)}</ul>
          )}
        </Row>
        {r.seed && (
          <>
            <Row ok={r.seed.missingRoles.length === 0 && r.seed.permissionsFound >= r.seed.permissionsExpected} title="ข้อมูลตั้งต้น (บทบาทและสิทธิ์)">
              บทบาท {r.seed.rolesFound}/{r.seed.rolesExpected} · สิทธิ์ {r.seed.permissionsFound}/{r.seed.permissionsExpected}
              {r.seed.missingRoles.length > 0 && ` · ขาด ${r.seed.missingRoles.join(', ')}`}
            </Row>
            <Row ok={r.seed.hasActiveAdmin} warn title="มีผู้ดูแลระบบที่ใช้งานอยู่">
              {r.seed.hasActiveAdmin ? 'มีบัญชีผู้ดูแลระบบสูงสุดอย่างน้อยหนึ่งบัญชี'
                : adminHint ?? 'ยังไม่มีผู้ดูแลระบบ จึงเข้าสู่ระบบเพื่อตั้งค่าไม่ได้ ให้สร้างด้วยคำสั่ง create-admin (ดูคู่มือติดตั้ง)'}
            </Row>
          </>
        )}
      </ul>
    </Card>
  )
}
