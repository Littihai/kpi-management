# KPI Management System

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![React](https://img.shields.io/badge/React-18+-61DAFB?style=for-the-badge&logo=react&logoColor=black)
![TypeScript](https://img.shields.io/badge/TypeScript-5+-3178C6?style=for-the-badge&logo=typescript&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15+-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)
![TailwindCSS](https://img.shields.io/badge/Tailwind_CSS-4.x-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)
![Supabase](https://img.shields.io/badge/Supabase-Database-3ECF8E?style=for-the-badge&logo=supabase&logoColor=white)
![Render](https://img.shields.io/badge/Render-API-46E3B7?style=for-the-badge&logo=render&logoColor=white)
![Vercel](https://img.shields.io/badge/Vercel-Frontend-000000?style=for-the-badge&logo=vercel&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)

Enterprise KPI Management Platform สำหรับองค์กร ใช้แทน Excel ในการติดตาม KPI, Projects และ Tasks แบบ Real-time

---

## Features

- **KPI Management** — สร้าง ติดตาม และวิเคราะห์ KPI รายปี
- **Approval Workflow** — Draft → Pending Approval → Active
- **Progress Engine** — คำนวณ progress อัตโนมัติจาก weighted projects
- **Traffic Light System** — 🟢 On Track / 🟡 At Risk / 🔴 Delayed
- **Delay Detection** — ตรวจจับ delay อัตโนมัติทุกวันผ่าน Background Job
- **Email Notifications** — แจ้งเตือน approve, delay, task assigned
- **Executive Dashboard** — Charts, Department Ranking, Monthly Trend
- **PDF / Excel Export** — Export รายงานได้ทันที
- **RBAC** — Role-Based Access Control 6 ระดับ
- **Audit Logs** — Track ทุก action ในระบบ
- **JWT Authentication** — Secure API ด้วย JWT Token
- **แจ้งปัญหา (Issues)** — ผู้ใช้แจ้งปัญหา ทีม IT รับเรื่อง/แก้ไข/บันทึกวิธีแก้ มี SLA ตอบรับภายใน 2 ชั่วโมง และหน้าแจ้งปัญหาสำหรับผู้ที่เข้าสู่ระบบไม่ได้ (`/report`)
- **ภาพรวมระบบและ Audit** — SuperAdmin ดูความเคลื่อนไหวทั้งระบบ ใครทำอะไร จาก IP ไหน

---

## Tech Stack

### Backend
| Technology | Version | Purpose |
|-----------|---------|---------|
| ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet) | .NET 10 | Web API Framework |
| ![EF Core](https://img.shields.io/badge/EF_Core-10.x-512BD4?style=flat&logo=dotnet) | 10.x | ORM + Database Migration |
| ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15+-4169E1?style=flat&logo=postgresql) | 15+ | Primary Database |
| ![Hangfire](https://img.shields.io/badge/Hangfire-Latest-grey?style=flat) | Latest | Background Jobs |
| ![QuestPDF](https://img.shields.io/badge/QuestPDF-Latest-grey?style=flat) | Latest | PDF Generation |
| ![ClosedXML](https://img.shields.io/badge/ClosedXML-Latest-grey?style=flat) | Latest | Excel Generation |
| ![Resend](https://img.shields.io/badge/Resend-Latest-000000?style=flat) | Latest | Email Service |

### Frontend
| Technology | Version | Purpose |
|-----------|---------|---------|
| ![React](https://img.shields.io/badge/React-18+-61DAFB?style=flat&logo=react) | 18+ | UI Framework |
| ![TypeScript](https://img.shields.io/badge/TypeScript-5+-3178C6?style=flat&logo=typescript) | 5+ | Type Safety |
| ![Tailwind](https://img.shields.io/badge/Tailwind-4.x-06B6D4?style=flat&logo=tailwindcss) | 4.x | Styling |
| ![Vite](https://img.shields.io/badge/Vite-6.x-646CFF?style=flat&logo=vite) | 6.x | Build Tool |
| ![Zustand](https://img.shields.io/badge/Zustand-Latest-brown?style=flat) | Latest | State Management |
| ![Axios](https://img.shields.io/badge/Axios-Latest-5A29E4?style=flat&logo=axios) | Latest | HTTP Client |
| ![Recharts](https://img.shields.io/badge/Recharts-Latest-22b5bf?style=flat) | Latest | Charts |

### Infrastructure
| Service | Badge | Purpose |
|---------|-------|---------|
| Supabase | ![Supabase](https://img.shields.io/badge/Supabase-3ECF8E?style=flat&logo=supabase&logoColor=white) | PostgreSQL Database |
| Render | ![Render](https://img.shields.io/badge/Render-46E3B7?style=flat&logo=render&logoColor=white) | Backend API Hosting |
| Vercel | ![Vercel](https://img.shields.io/badge/Vercel-000000?style=flat&logo=vercel&logoColor=white) | Frontend Hosting |
| GitHub | ![GitHub](https://img.shields.io/badge/GitHub-181717?style=flat&logo=github&logoColor=white) | Version Control |
| Resend | ![Resend](https://img.shields.io/badge/Resend-000000?style=flat&logo=mail&logoColor=white) | Email Delivery |

---

## Architecture

```
kpi-management/
├── backend/
│   ├── KPI.Domain/           # Entities, Enums, Interfaces
│   ├── KPI.Application/      # Use Cases, DTOs, Service Interfaces
│   ├── KPI.Infrastructure/   # EF Core, Repositories, Services, Jobs
│   └── KPI.API/              # Controllers, Middleware, Program.cs
└── frontend/
    └── src/
        ├── pages/            # Login, Dashboard, KPI, Projects
        ├── components/       # Shared UI Components
        ├── services/         # API Service Layer
        ├── stores/           # Zustand State Management
        ├── hooks/            # Custom React Hooks
        └── types/            # TypeScript Type Definitions
```

### Clean Architecture Layers

```
API Layer
    ↓
Application Layer (Use Cases, DTOs)
    ↓
Domain Layer (Entities, Business Logic)
    ↑
Infrastructure Layer (EF Core, External Services)
```

---

## Database Schema

```
users ──────────── roles
  │                  │
  │              role_permissions
  │                  │
  └── departments  permissions

kpis ──────────── projects
  │
  └── kpi_progress_logs

audit_logs
```

---

## Role-Based Access Control

| Role | Access Level |
|------|-------------|
| SuperAdmin | Full system access |
| Director | View all departments + Approve KPI |
| Manager | Manage department KPI + Projects |
| TeamLeader | Manage projects |
| Staff | Update assigned tasks |
| Viewer | Read only |

---

## KPI Lifecycle

```
Draft → Pending Approval → Active → At Risk / Delayed → Completed → Closed
```

---

## Getting Started

### Prerequisites

![.NET](https://img.shields.io/badge/.NET_SDK-10.0-512BD4?style=flat&logo=dotnet)
![Node](https://img.shields.io/badge/Node.js-20+-339933?style=flat&logo=nodedotjs)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15+-4169E1?style=flat&logo=postgresql)

### Backend Setup

```bash
cd backend

# Restore packages
dotnet restore

# Create KPI.API/appsettings.Development.json (git-ignored; copy appsettings.Example.json)
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=postgres;Username=...;Password=..."
  },
  "Jwt": {
    "Key": "<random secret, 32+ characters>",
    "Issuer": "kpi-management",
    "Audience": "kpi-management"
  },
  "Resend": {
    "ApiKey": "re_your_api_key"
  },
  "Admin": {
    "Email": "you@example.com",
    "Password": "<at least 8 characters>"
  }
}

# Start API — applies pending migrations and creates the first SuperAdmin from "Admin" on a new database
cd KPI.API
dotnet run
```

`Jwt:Key` ต้องเป็นค่าสุ่มอย่างน้อย 32 ตัวอักษร แอปจะไม่สตาร์ทถ้าไม่ได้ตั้ง สั้นเกินไป หรือยังเป็นค่าตัวอย่างจาก repo
สร้างได้ด้วย PowerShell: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`

Migration จะรันอัตโนมัติตอนแอปสตาร์ท (ปิดได้ด้วย `Database__AutoMigrate=false` แล้วรัน
`dotnet ef database update --project KPI.Infrastructure --startup-project KPI.API` เอง)

API จะรันที่ `http://localhost:5079`

### Frontend Setup

```bash
cd frontend

# Install dependencies
npm install

# Create .env.local
VITE_API_URL=http://localhost:5079

# Start development server
npm run dev
```

Frontend จะรันที่ `http://localhost:5173`

---

## API Endpoints

### Authentication
```
POST /api/auth/login       # เข้าสู่ระบบ
GET  /api/auth/me          # ข้อมูล user ปัจจุบัน + permissions
POST /api/auth/register    # สร้างบัญชี Staff (ต้องมีสิทธิ์ user.create — ไม่เปิดสาธารณะแล้ว)
```

### KPI
```
GET    /api/kpi              # ดู KPI ทั้งหมด
GET    /api/kpi/{id}         # ดู KPI รายการ
POST   /api/kpi              # สร้าง KPI
PUT    /api/kpi/{id}         # แก้ KPI (เฉพาะ Draft)
DELETE /api/kpi/{id}         # ลบ KPI (เฉพาะ Draft, SuperAdmin)
POST   /api/kpi/{id}/submit  # ส่ง approve
POST   /api/kpi/{id}/approve # อนุมัติ KPI
POST   /api/kpi/{id}/reject  # ตีกลับเป็น Draft (ต้องระบุเหตุผล)
POST   /api/kpi/{id}/close   # ปิด KPI ที่ Completed
POST   /api/kpi/progress     # อัปเดต progress
```

สิทธิ์ของแต่ละ role และ workflow ของสถานะ KPI: [docs/access-and-workflow.md](docs/access-and-workflow.md)

### Real-time (SignalR)
```
/hubs/kpi    # ส่ง KpiChanged / Notification ไปยัง client ที่เกี่ยวข้อง
```

### Health
```
GET /health          # process ทำงานอยู่ (ไม่แตะ DB) — ใช้เป็น Health Check Path บน Render
GET /health/ready    # ตรวจการเชื่อมต่อ DB ด้วย (503 ถ้าต่อไม่ได้)
```

### Dashboard
```
GET /api/dashboard           # ข้อมูล Executive Dashboard
```

### Export
```
GET /api/export/kpi/pdf      # Export PDF
GET /api/export/kpi/excel    # Export Excel
```

### Audit
```
GET /api/audit               # ดู Audit Logs
```

### Jobs
```
POST /api/jobs/trigger-delay-detection  # Trigger delay detection
```

---

## Environment Variables

### Backend (appsettings.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "PostgreSQL connection string"
  },
  "Jwt": {
    "Key": "Secret key (min 32 chars)",
    "Issuer": "kpi-management",
    "Audience": "kpi-management"
  },
  "Resend": {
    "ApiKey": "Resend API Key"
  }
}
```
บน Render ใช้ environment variables แทน: `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Resend__ApiKey`
และ (ถ้าจำเป็น) `Cors__Origins__0` สำหรับ origin เพิ่มเติม เช่น Vercel preview

อีเมล: `Resend__From` (ต้องเป็นโดเมนที่ verify แล้ว ไม่งั้นส่งถึงเจ้าของบัญชี Resend เท่านั้น),
`App__BaseUrl` (URL ของ frontend สำหรับปุ่ม "Open KPI"), `Email__Enabled=false` เพื่อปิดการส่ง —
รายละเอียดใน [docs/access-and-workflow.md](docs/access-and-workflow.md)

การ deploy ครั้งแรก:
- `Admin__Email` + `Admin__Password` (และ `Admin__FirstName`, `Admin__LastName` ถ้าต้องการ) — สร้าง SuperAdmin คนแรกบนฐานข้อมูลใหม่
  ทำงานเฉพาะตอนยังไม่มี SuperAdmin ที่ active อยู่ ลบ `Admin__Password` ออกหลังล็อกอินครั้งแรกได้
- `Database__AutoMigrate=false` — ปิดการ migrate อัตโนมัติตอนสตาร์ท (ค่าเริ่มต้นคือเปิด)
- `LoginThrottle__ClientIpHeader=CF-Connecting-IP` — **ตั้งบน Render** เพื่อให้จำกัดการลองรหัสผ่านตาม IP จริงของผู้ใช้
  (ถ้าไม่ตั้ง จะเห็นเป็น IP ของ proxy เดียวกันทั้งหมด) ตั้งเฉพาะเมื่อ request ทุกอันผ่าน proxy ที่เขียนทับ header นี้
- `LoginThrottle__MaxFailuresPerAccount` (5), `LoginThrottle__MaxFailuresPerIp` (20), `LoginThrottle__WindowMinutes` (15)

ตั้ง Health Check Path บน Render เป็น `/health`

รายละเอียดใน [docs/access-and-workflow.md](docs/access-and-workflow.md#sign-in-protection-and-first-deploy)

### Frontend (.env.local)
```
VITE_API_URL=https://your-api.onrender.com
```

---

## Deployment

### Backend → Render
![Render](https://img.shields.io/badge/Deploy_on-Render-46E3B7?style=for-the-badge&logo=render)

1. Connect GitHub repository
2. Root Directory: `backend`
3. Dockerfile Path: `KPI.API/Dockerfile`
4. Add Environment Variables

### Frontend → Vercel
![Vercel](https://img.shields.io/badge/Deploy_on-Vercel-000000?style=for-the-badge&logo=vercel)

1. Import GitHub repository
2. Root Directory: `frontend`
3. Framework: Vite

---

## Background Jobs

ระบบใช้ Hangfire สำหรับ Background Jobs

| Job | Schedule | Description |
|-----|----------|-------------|
| Delay Detection | ทุกวัน | ตรวจ KPI ที่ล่าช้าและส่ง email แจ้งเตือน |

Hangfire Dashboard: `http://localhost:5079/hangfire`

---

## Roadmap

- [ ] Global Error Handling
- [ ] CI/CD GitHub Actions
- [ ] Project Management UI
- [ ] User Management
- [ ] Department Management
- [ ] Search & Filter
- [ ] Rate Limiting
- [ ] Multi-tenancy (SaaS)

---

## License

![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)

MIT License — feel free to use and modify
