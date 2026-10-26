# Access rules and workflow

The agreed rules the backend enforces. Code of record:
`KPI.Domain/Constants/RolePermissionMap.cs` (who may do what),
`KPI.Domain/Issues/IssueStateMachine.cs` (which action is valid in which status),
`KPI.Infrastructure/Services/IssueService.cs` (scope and guards).
Tests in `backend/KPI.Tests` fail if the code and this document drift apart.

## How a request is checked

1. **Permission** – each endpoint has `[Authorize(Policy = "<permission>")]`. Permissions are read from the
   database on every request, so role changes and deactivation apply immediately (not when the 7-day JWT expires).
2. **Scope** – SuperAdmin, Director and ITSupport see every issue; a Manager sees their own and their department's;
   everyone else only their own. An issue outside the caller's scope is reported as *not found*.
3. **Status** – the action must be valid for the issue's current status, otherwise `409 Conflict`.
4. **Guards** – e.g. an assignee must be an active person who works the queue.

Errors: `401` not signed in · `403` not allowed · `404` not found / out of scope · `409` wrong status or stale data ·
`400` invalid input · `429` too many failed sign-ins (with a `Retry-After` header). Bodies are RFC 7807 problem details and also carry a `message` field.

## Roles × actions

🌐 all departments · 🏢 own department · 👤 own issues only · ✗ not allowed

| Action | SuperAdmin | ITSupport | Director | Manager | TeamLeader / Staff / Viewer |
|---|---|---|---|---|---|
| Report a problem | ✓ | ✓ | ✓ | ✓ | ✓ |
| See issues | 🌐 | 🌐 | 🌐 | 🏢 | 👤 |
| Work the queue (assign, start, ask, resolve, reject, priority, internal notes) | ✓ | ✓ | ✗ | ✗ | ✗ |
| Comment, confirm, reopen own issue | ✓ | ✓ | ✓ | ✓ | 👤 |
| View users | 🌐 | ✗ | 🌐 | 🏢 | ✗ |
| Create users, change role, deactivate | ✓ | ✗ | ✗ | ✗ | ✗ |
| List roles, trigger jobs | ✓ | ✗ | ✗ | ✗ | ✗ |
| Audit log, request log, system overview | ✓ | ✗ | ✗ | ✗ | ✗ |

Other rules: nobody can change their own role or deactivate themselves; at least one active SuperAdmin must remain;
public self-registration is closed (`POST /api/auth/register` needs `user.create`); passwords are at least 8 characters.

## E-mail notifications

Sent through the SMTP server set up in Settings > E-mail, only when something actually happens (never as a daily repeat), and best effort: a mail
problem is logged and never fails or undoes the change. The person who performed the action is not e-mailed.
Which events send which e-mail is described under "Problem reports" below.

Server, port, encryption, login and sender are set in the app (Settings > E-mail), which also has a test send. Environment
variables: `App__BaseUrl` – the frontend URL, which adds an "open the issue" button; `Email__Enabled=false` to switch
e-mail off. Messages to several people are spaced about half a second apart to stay under a mail server's rate limit. All user-supplied text is HTML-encoded.

## Real-time updates (SignalR)

Hub: `/hubs/kpi` (the path is historical; JWT; browsers pass it as `?access_token=`, accepted on `/hubs` URLs only).
Server → client messages:

- `IssueChanged` – an issue changed. Sent to the queue (ITSupport, SuperAdmin), cross-department roles, the reporter,
  the assignee and the department's managers. Pages refresh.
- `Notification` – a toast for specific people (the assignee, the reporter, the queue).

Group membership is decided when a client connects, so a role or department change applies on the next reconnect.
Pushes never block or fail the business operation. CORS origins are fixed in `Program.cs`; add more with
`Cors__Origins__0`, `Cors__Origins__1`, … (needed for Vercel preview URLs).

## Sign-in protection and first deploy

**Failed sign-ins are throttled** (`POST /api/auth/login`). Only failures count, so colleagues behind one office
address sign in normally. Two limits apply per 15-minute window; once one is reached the answer is `429` with
`Retry-After` until the window ends, even for the right password:

| Limit | Default | Stops |
|---|---|---|
| failures for one e-mail from one client address | 5 | guessing one account's password |
| failures from one client address, any e-mail | 20 | trying one password against many accounts |

A success clears the account counter but not the address counter. The account limit includes the client address, so
a stranger cannot lock a colleague out from elsewhere. Counters are in memory: they reset on restart and are not
shared between instances, so bcrypt's cost remains the backstop against a distributed attack.

The API needs the client's real address. Behind Render's proxy the socket address is the proxy's, so set
`LoginThrottle__ClientIpHeader=CF-Connecting-IP` (Cloudflare overwrites that header on every request). Do not set it
where a caller can reach the API directly, because the header could then be forged. After deploying, a blocked sign-in
logs `Sign-in blocked for <ip>`; check that the address is a client's and not a proxy's. Tune with
`LoginThrottle__MaxFailuresPerAccount`, `LoginThrottle__MaxFailuresPerIp`, `LoginThrottle__WindowMinutes`
(a limit of `0` switches that check off).

**Signing key.** `Jwt__Key` must be a random secret of at least 32 characters. The API refuses to start if it is
missing, shorter, repetitive, or one of the placeholder values published in this repository.

**Database.** Pending EF Core migrations are applied when the API starts, before roles are seeded, so new code never
runs on old tables. Set `Database__AutoMigrate=false` to run `dotnet ef database update` yourself instead. A test
(`MigrationTests`) fails in CI when the entity model changes without a migration, which would otherwise stop the
API from starting.

**First SuperAdmin.** Public registration is closed, so a new database has nobody who can sign in. Set
`Admin__Email` and `Admin__Password` (optionally `Admin__FirstName`, `Admin__LastName`): at startup, if there is no
active SuperAdmin, that account is created. With an active SuperAdmin present the settings are ignored, so they can
stay or be removed; removing `Admin__Password` after the first sign-in is recommended. If the settings are present
but unusable (bad e-mail, short password, e-mail already used by someone else) the API stops with a message naming
the setting.

**Health checks.** `GET /health` answers 200 while the process is up and never touches the database (use it as
Render's health check path and for keep-alive pings). `GET /health/ready` also checks the database and answers 503
when it is unreachable; the response says only `Healthy` or `Unhealthy`, details go to the logs.


## Problem reports (issues)

People report problems with this program (or general IT trouble) and IT fixes them. Everything is Thai, and dates
are shown as 17/10/2569 (Buddhist year, Thai time).

**Who:** everyone who signs in can report (`issue.create`) and follow their own reports (`issue.view`).
A Manager also sees their department's reports. **IT Support** (new role `ITSupport`) works the queue
(`issue.manage`) and, with Director and SuperAdmin, sees every report. People who cannot sign in use
`/report` (no account; name plus e-mail or phone; limited to 5 reports per 15 minutes per address; a hidden
honeypot field catches bots).

**Status flow**

```
 New ──assign──▶ Assigned ──start──▶ InProgress ──resolve──▶ Resolved ──confirm──▶ Closed
  │                  │                  │  ▲                    │ ▲                 │
  │                  └──ask for info────┘  └─reporter replies─┐ │ └─ auto-close     └─reopen (reporter: 7 days)
  └──────────────── reject (reason, optional duplicate) ───────┘ └── not fixed → Reopened
```

| Action | Allowed in | Who |
|---|---|---|
| Assign (to active IT) | any open status | IT |
| Start | New, Assigned, Reopened | IT |
| Ask the reporter for information | Assigned, InProgress, Reopened | IT (stops the SLA clock) |
| Resolve (explanation and cause required) | Assigned, InProgress, Reopened | IT |
| Confirm / close | Resolved | reporter or IT |
| Reopen (reason required) | Resolved, Closed, Rejected | reporter (Resolved, or Closed within 7 days) or IT |
| Reject (reason required) | any open status | IT |

**Service levels** (calendar hours, configurable under `Issues:*`): first response within **2 hours**; resolution
within 4 h (Critical), 24 h (High), 72 h (Medium), 168 h (Low). The starting priority comes from the reporter's
impact answer (blocking → High, degraded → Medium, minor → Low); IT can change it. While the issue waits for the
reporter the clock stops and the deadline moves back by the waiting time. A resolved issue nobody answers is
closed automatically after 3 days.

**What is recorded:** title, description, module, category, impact, priority, reporter, department, assignee,
timestamps and deadlines, up to 5 screenshots (images only, 5 MB each, stored in the database and checked by
content, not file name), the conversation (IT can add internal notes the reporter never sees), and the
resolution: root cause, how it was fixed, how to prevent it, link to the change, time spent. Browser details
(page, device, IP, failed request id) are captured automatically for IT.

**Notifications:** real-time toasts (SignalR) and e-mail to the right people: IT when a report arrives, the reporter
when it is received, needs information, is answered, resolved or rejected, the assignee when assigned,
replied to or reopened. E-mail follows the settings in "E-mail notifications" above.

## Audit trail and system overview (SuperAdmin only)

- **Audit log** (`/admin/audit`, `GET /api/audit`): every business change with who (name, role, department),
  what (before/after), when, and the IP address and browser it came from. Stamped automatically on save.
- **Request log** (same page, second tab): every write, every refusal (401/403/429), every error and every
  sign-in attempt (including the e-mail that was tried), with IP and duration. Successful reads are not logged.
  Entries are saved in the background so requests never wait for it; they are purged after 90 days
  (`Audit:RequestLogRetentionDays`), audit rows are kept.
- **System overview** (`/admin/overview`): who is online (with IP), request volume, failed sign-ins, top IPs,
  recent security events, issue statistics (open, overdue, average time to fix, by status/department/module),
  user counts, and a real-time activity feed.

## Performance notes

- Indexes cover the filters and sort orders the app uses (issues by status/assignee/
  due date, audit and request logs by time/user/IP, `lower(Email)` for sign-in).
- The permissions of the signed-in user are loaded once per request instead of twice; list queries skip change
  tracking; responses are compressed; pages are loaded on demand (the first screen downloads about a third of
  the code it used to).
- Every response carries a `Server-Timing: app;dur=…` header and requests slower than 800 ms are logged, so
  "slow" can be measured: server time versus network time versus database distance.
- The biggest factor is usually distance: keep the Render service and the Supabase database in the same
  region (the pooler host in the connection string shows the database region).
