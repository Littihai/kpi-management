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
`400` invalid input. Bodies are RFC 7807 problem details and also carry a `message` field.

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
