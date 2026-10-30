// Walks the main flows of the API against whatever database it is running on and prints one line per call.
const base = process.env.API || 'http://localhost:5290'
const adminEmail = process.env.SMOKE_EMAIL || 'admin@example.com'
const adminPassword = process.env.SMOKE_PASSWORD || 'Smoke-Test-Pass-1'
const results = []
let token = ''
async function call(method, url, body, label) {
  const headers = {}
  if (token) headers.Authorization = 'Bearer ' + token
  let payload
  if (body instanceof FormData) payload = body
  else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body) }
  const res = await fetch(base + url, { method, headers, body: payload })
  const text = await res.text()
  let json = null
  try { json = JSON.parse(text) } catch { /* not json */ }
  results.push({ label: label || `${method} ${url}`, status: res.status })
  return { status: res.status, json, text }
}
const expect = (label, got, want) => { const ok = Array.isArray(want) ? want.includes(got) : got === want; results.push({ label: 'CHECK ' + label, status: ok ? 'ok' : `FAIL (got ${got}, want ${want})` }) }

;(async () => {
  let r = await call('POST', '/api/auth/login', { email: adminEmail, password: adminPassword })
  expect('login', r.status, 200)
  token = r.json.token
  me: { r = await call('GET', '/api/auth/me'); expect('me', r.status, 200) }

  for (const u of ['/api/roles/details', '/api/roles/permissions', '/api/settings', '/api/users?pageSize=5', '/api/departments/all',
    '/api/auth-logs', '/api/issues/summary', '/api/issues?pageSize=5', '/api/admin/overview', '/api/audit?pageSize=5', '/api/client-config']) {
    r = await call('GET', u); expect(u, r.status, [200, 404])
  }

  // department + user
  r = await call('POST', '/api/departments', { code: 'IT', name: 'ฝ่ายไอที' }); expect('create department', r.status, 201)
  const dept = r.json?.id
  const roles = (await call('GET', '/api/roles')).json
  const staffRole = roles.find((x) => x.name === 'Staff').id
  const itRole = roles.find((x) => x.name === 'ITSupport').id
  r = await call('POST', '/api/users', { firstName: 'สมชาย', lastName: 'ใจดี', email: 'somchai@example.com', password: 'Somchai-Pass-1', roleId: staffRole, departmentId: dept }); expect('create staff', r.status, 200)
  r = await call('POST', '/api/users', { firstName: 'ไอที', lastName: 'ซัพพอร์ต', email: 'it@example.com', password: 'Support-Pass-1', roleId: itRole, departmentId: dept }); expect('create it', r.status, 200)
  r = await call('GET', '/api/users?search=SOMCHAI'); expect('user search is case-insensitive', r.json?.total, 1)
  r = await call('POST', '/api/users', { firstName: 'dup', lastName: 'x', email: 'SomChai@Example.com', password: 'Somchai-Pass-1', roleId: staffRole }); expect('duplicate e-mail refused (any case)', r.status, 400)

  // staff reports an issue with a picture
  const staff = await (await fetch(base + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'somchai@example.com', password: 'Somchai-Pass-1' }) })).json()
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')
  const form = new FormData()
  form.set('Title', 'เครื่องพิมพ์ชั้น 2 ไม่ออก'); form.set('Description', 'สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง')
  form.set('Category', 'Bug'); form.set('Module', 'Application'); form.set('Impact', 'Degraded')
  form.append('files', new Blob([png], { type: 'image/png' }), 'shot.png')
  const adminToken = token
  token = staff.token
  r = await call('POST', '/api/issues', form, 'POST /api/issues (multipart + picture)'); expect('create issue', r.status, 201)
  const issueId = r.json?.id
  expect('issue number', /^ISS-\d{4}-\d{4}$/.test(r.json?.number || ''), true)
  expect('attachment stored', r.json?.attachments?.length, 1)
  if (issueId && r.json?.attachments?.[0]) {
    const att = await fetch(`${base}/api/issues/${issueId}/attachments/${r.json.attachments[0].id}`, { headers: { Authorization: 'Bearer ' + token } })
    expect('attachment downloads', att.status, 200)
    expect('attachment bytes round-trip', Buffer.from(await att.arrayBuffer()).equals(png), true)
  }
  for (let i = 0; i < 3; i++) { // numbering under repeated creates
    const f = new FormData(); f.set('Title', 'เรื่องที่ ' + i + ' ทดสอบ'); f.set('Description', 'รายละเอียดที่ยาวพอสำหรับการทดสอบ'); f.set('Category', 'Bug'); f.set('Module', 'Login'); f.set('Impact', 'Minor')
    r = await call('POST', '/api/issues', f, 'POST /api/issues #' + (i + 2)); expect('create issue ' + (i + 2), r.status, 201)
  }
  token = adminToken

  // staff work
  const it = await (await fetch(base + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'it@example.com', password: 'Support-Pass-1' }) })).json()
  token = it.token
  r = await call('GET', '/api/issues?pageSize=10'); expect('IT sees all issues', r.json?.total, 4)
  r = await call('GET', '/api/issues?search=%E0%B9%80%E0%B8%84%E0%B8%A3%E0%B8%B7%E0%B9%88%E0%B8%AD%E0%B8%87%E0%B8%9E%E0%B8%B4%E0%B8%A1%E0%B8%9E%E0%B9%8C'); expect('search (Thai)', r.json?.total, 1)
  r = await call('GET', '/api/issues?page=2&pageSize=3'); expect('paging', r.json?.items?.length, 1)
  r = await call('GET', '/api/issues?status=0&priority=1'); expect('filter status+priority', r.status, 200)
  r = await call('GET', '/api/issues?overdue=true'); expect('overdue filter', r.status, 200)
  r = await call('POST', `/api/issues/${issueId}/start`); expect('start', r.status, 200)
  r = await call('POST', `/api/issues/${issueId}/hold`, { reason: 'รออะไหล่' }); expect('hold', r.status, 200)
  r = await call('POST', `/api/issues/${issueId}/resume`, { message: 'อะไหล่มาแล้ว' }); expect('resume', r.status, 200)
  r = await call('POST', `/api/issues/${issueId}/comments`, { body: 'โน้ตภายใน', internalNote: true }); expect('internal note', r.status, 200)
  r = await call('POST', `/api/issues/${issueId}/resolve`, { resolution: 'เปลี่ยนตลับหมึกแล้ว', rootCause: 7 }); expect('resolve', r.status, 200)
  r = await call('GET', `/api/issues/${issueId}`); expect('detail', r.status, 200)
  expect('detail status Resolved', r.json?.status, 4)
  token = adminToken
  r = await call('GET', '/api/issues/summary'); expect('summary', r.status, 200)

  // settings + audit + auth log + roles
  r = await call('PUT', '/api/settings/issues.autoCloseDays', { value: '5' }); expect('update setting', r.status, 200)
  r = await call('GET', '/api/settings/issues.autoCloseDays/history'); expect('setting history', r.json?.length, 1)
  r = await call('PUT', '/api/settings/smtp.password', { value: 'x-secret' }); expect('encrypted setting', r.status, 200)
  r = await call('POST', '/api/roles', { name: 'หัวหน้าไอที', description: 'ทดสอบ', permissions: ['issue.create', 'issue.view', 'issue.manage'], issueVisibility: 2, seesAllDepartments: false }); expect('create role', r.status, 201)
  r = await call('GET', '/api/auth-logs?search=somchai'); expect('auth log search', r.json?.total >= 1, true)
  r = await call('GET', '/api/audit?pageSize=5'); expect('audit log', r.status, 200)
  r = await call('POST', '/api/auth/change-password', { currentPassword: adminPassword, newPassword: adminPassword + '-2' }); expect('change password', r.status, 200)

  const bad = results.filter((x) => x.status !== 'ok' && typeof x.status === 'string')
  for (const x of results) console.log(String(x.status).padEnd(8), x.label)
  console.log(bad.length ? `\n${bad.length} FAILED` : '\nall checks passed')
  process.exit(bad.length ? 1 : 0)
})().catch((e) => {
  for (const x of results) console.log(String(x.status).padEnd(8), x.label)   // what happened up to the crash
  console.error(e); process.exit(2)
})
