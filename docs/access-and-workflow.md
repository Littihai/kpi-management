# Access rules and KPI workflow

The agreed rules the backend enforces. Code of record:
`KPI.Domain/Constants/RolePermissionMap.cs` (who may do what),
`KPI.Domain/Kpis/KpiStateMachine.cs` (which action is valid in which status),
`KPI.Infrastructure/Services/KpiService.cs` (scope and guards).
Tests in `backend/KPI.Tests` fail if the code and this document drift apart.

## How a request is checked

1. **Permission** – each endpoint has `[Authorize(Policy = "<permission>")]`. Permissions are read from the
   database on every request, so role changes and deactivation apply immediately (not when the 7-day JWT expires).
2. **Scope** – SuperAdmin and Director see every department; everyone else only their own. A KPI outside the
   caller's scope is reported as *not found*. Staff may update progress only on KPIs they own.
3. **Status** – the action must be valid for the KPI's current status (table below), otherwise `409 Conflict`.
4. **Guards** – e.g. nobody approves their own KPI.

Errors: `401` not signed in · `403` not allowed · `404` not found / out of scope · `409` wrong status or stale data ·
`400` invalid input · `429` too many failed sign-ins (with a `Retry-After` header). Bodies are RFC 7807 problem details and also carry a `message` field.

## Roles × actions

🌐 all departments · 🏢 own department · 👤 own KPIs only · ✗ not allowed

| Action | SuperAdmin | Director | Manager | TeamLeader | Staff | Viewer |
|---|---|---|---|---|---|---|
| View KPIs / dashboard | 🌐 | 🌐 | 🏢 | 🏢 | 🏢 | 🏢 |
| Create / edit KPI (Draft) | 🌐 | ✗ | 🏢 | ✗ | ✗ | ✗ |
| Delete KPI (Draft) | 🌐 | ✗ | ✗ | ✗ | ✗ | ✗ |
| Submit for approval | 🌐 | ✗ | 🏢 | ✗ | ✗ | ✗ |
| Approve / reject | 🌐 | 🌐 | ✗ | ✗ | ✗ | ✗ |
| Update progress | 🌐 | ✗ | 🏢 | 🏢 | 👤 | ✗ |
| Close KPI | 🌐 | 🌐 | ✗ | ✗ | ✗ | ✗ |
| View users | 🌐 | 🌐 | 🏢 | ✗ | ✗ | ✗ |
| Create users, change role, deactivate | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ |
| List roles, trigger jobs | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Export PDF / Excel | 🌐 | 🌐 | 🏢 | ✗ | ✗ | ✗ |
| Audit log | ✓ | ✓ | ✗ | ✗ | ✗ | ✗ |

Other rules: nobody can change their own role or deactivate themselves; at least one active SuperAdmin must remain;
public self-registration is closed (`POST /api/auth/register` needs `user.create`); passwords are at least 8 characters.

## KPI status workflow

```
 [Draft] ──Submit──▶ [PendingApproval] ──Approve──▶ ┌──────── running ────────┐
    ▲                       │                       │ Active ⇄ AtRisk ⇄ Delayed │
    └──── Reject (reason) ──┘                       └───────────┬──────────────┘
                                          progress ≥ target │ ▲ progress corrected down
                                                            ▼ │
                                                       [Completed] ──Close──▶ [Closed]
```

| Action | Allowed in status | Who (permission) |
|---|---|---|
| Edit, Delete | Draft | `kpi.edit`, `kpi.delete` |
| Submit | Draft | `kpi.submit` |
| Approve, Reject | PendingApproval | `kpi.approve`, `kpi.reject` (reject needs a reason) |
| Update progress | Active, AtRisk, Delayed, Completed | `kpi.progress` |
| Close | Completed | `kpi.close` |
| anything | Closed | — (frozen) |

`Approved` (2) is unused and kept only so stored numbers stay valid.

**Automatic status** (one function, `KpiStateMachine.EvaluateHealth`, used on every progress update and by the
daily job): `progress ≥ target` → Completed; past the due date (the due date itself still counts) → Delayed;
progress more than 15 behind the expected pace (`elapsed / total × target`) → AtRisk; otherwise Active.
Progress must stay between 0 and the target.

## E-mail notifications

Sent through Resend, only when something actually happens (never as a daily repeat), and best effort: a mail
problem is logged and never fails or undoes the change. The person who performed the action is not e-mailed.

| Event | Recipients |
|---|---|
| KPI created | other active Managers of that department |
| Submitted for approval | all active Directors (SuperAdmins if there is no active Director) |
| Approved / rejected (with reason) | the KPI's owner |
| Becomes AtRisk or Delayed (progress update or daily job) | the owner and the department's Managers |
| Completed | the department's Managers and all Directors |

Settings (environment variables on Render): `Resend__ApiKey`; `Resend__From` – a sender on a **verified domain**
(the default `onboarding@resend.dev` can only deliver to the Resend account owner); `App__BaseUrl` – the frontend
URL, which adds an "Open KPI" button; `Email__Enabled=false` to switch e-mail off. Messages to several people are
spaced about half a second apart to stay under Resend's rate limit. All user-supplied text is HTML-encoded.

## Real-time updates (SignalR)

Hub: `/hubs/kpi` (JWT; browsers pass it as `?access_token=`, accepted on `/hubs` URLs only).
Server → client messages:

- `KpiChanged` – a KPI changed. Sent to its department, to SuperAdmin/Director, and to its owner. Pages refresh.
- `Notification` – a toast for specific people: approvers when a KPI is submitted; the owner on approve/reject;
  the owner and department managers when a KPI becomes AtRisk/Delayed; managers and approvers when Completed.

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
