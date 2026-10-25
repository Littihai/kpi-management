# Helpdesk

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![React](https://img.shields.io/badge/React-18+-61DAFB?style=for-the-badge&logo=react&logoColor=black)
![TypeScript](https://img.shields.io/badge/TypeScript-5+-3178C6?style=for-the-badge&logo=typescript&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15+-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)
![TailwindCSS](https://img.shields.io/badge/Tailwind_CSS-4.x-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)
![Supabase](https://img.shields.io/badge/Supabase-Database-3ECF8E?style=for-the-badge&logo=supabase&logoColor=white)
![Render](https://img.shields.io/badge/Render-API-46E3B7?style=for-the-badge&logo=render&logoColor=white)
![Vercel](https://img.shields.io/badge/Vercel-Frontend-000000?style=for-the-badge&logo=vercel&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)

ระบบ Helpdesk สำหรับองค์กร: ผู้ใช้แจ้งปัญหา ทีมช่วยเหลือรับเรื่อง แก้ไข และบันทึกวิธีแก้ ติดตามด้วย SLA และอัปเดตแบบ Real-time

> ระบบนี้เริ่มจากโปรแกรมติดตาม KPI ฟีเจอร์ KPI ถูกถอดออกแล้ว ข้อมูลเดิมในฐานข้อมูลถูกเก็บไว้ในตาราง `Archive_Kpis`, `Archive_Projects`, `Archive_KpiProgressLogs` (ไม่ได้ลบ) ชื่อ namespace `KPI.*` ในโค้ดเก็บไว้เหมือนเดิม

---

## Features

- **แจ้งปัญหา** — ผู้ใช้แจ้งพร้อมแนบรูป (วางหรือลากภาพหน้าจอได้) ระบบเก็บข้อมูลเครื่อง/หน้าที่เกิดปัญหาให้อัตโนมัติ
- **หน้าแจ้งปัญหาสำหรับคนที่เข้าสู่ระบบไม่ได้** (`/report`) ไม่ต้องล็อกอิน มีกันสแปม
- **สถานะงาน** — ใหม่ → มอบหมาย → กำลังดำเนินการ → รอผู้แจ้ง → แก้ไขแล้ว → ปิด (หรือไม่ดำเนินการ / เปิดใหม่)
- **SLA** — กำหนดตอบรับและแก้ไขตามความเร่งด่วน หยุดนับตอนรอผู้แจ้ง และเตือนเมื่อเกินกำหนด
- **บันทึกวิธีแก้** — สาเหตุ วิธีแก้ วิธีป้องกัน ลิงก์อ้างอิง เวลาที่ใช้
- **คอมเมนต์ภายใน** — ทีมช่วยเหลือคุยกันเองโดยผู้แจ้งไม่เห็น
- **อีเมลแจ้งเตือน** (ภาษาไทย) และ **อัปเดตสดผ่าน SignalR**
- **แดชบอร์ด** — ภาพรวมงานค้าง เรื่องเกินกำหนด และเรื่องที่ควรทำต่อ
- **RBAC** — สิทธิ์ตามบทบาท ตรวจจากฐานข้อมูลทุกคำขอ
- **ภาพรวมระบบและ Audit** — SuperAdmin ดูความเคลื่อนไหวทั้งระบบ ใครทำอะไร จาก IP ไหน
- **ปรับแต่งหน้าตา** — ธีมสว่าง/มืด สีหลัก ฟอนต์ มุมโค้ง เมนู ขนาดตัวอักษร

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
        ├── pages/            # Login, Dashboard, Issues, Admin
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

issues ──── issue_comments
  │
  └── issue_attachments

audit_logs, request_logs
Archive_Kpis, Archive_Projects, Archive_KpiProgressLogs   (ข้อมูลเดิมของฟีเจอร์ KPI ที่ถอดออก — อ่านอย่างเดียว)
```

---

## Role-Based Access Control

| Role | Access Level |
|------|-------------|
| SuperAdmin | ทุกอย่าง รวมผู้ใช้ ภาพรวมระบบ และ Audit |
| ITSupport | รับเรื่อง มอบหมาย แก้ไข และบันทึกวิธีแก้ ของทุกแผนก |
| Director | ดูเรื่องและผู้ใช้ของทุกแผนก |
| Manager | ดูเรื่องของแผนกตัวเอง |
| TeamLeader / Staff / Viewer | แจ้งปัญหาและติดตามเรื่องของตัวเอง |

---

## Ticket Lifecycle

```
New → Assigned → InProgress ⇄ WaitingForReporter → Resolved → Closed
              ↘ Rejected (ไม่ดำเนินการ / ซ้ำ)        ↘ Reopened
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

> **ไม่มีฐานข้อมูลหรือเซิร์ฟเวอร์ตั้งต้นในโปรเจกต์** `appsettings.json` ไม่มีสตริงเชื่อมต่อหรือ API key ใด ๆ ถ้าไม่ตั้งค่า
> (ไฟล์ `appsettings.Development.json`, ตัวแปร environment หรือหน้า "เชื่อมต่อฐานข้อมูล") backend จะเริ่มในโหมด setup
> รอให้ผู้ดูแลเชื่อมต่อเอง แต่ละบริษัทใช้ฐานข้อมูลและเซิร์ฟเวอร์ของตัวเอง

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

ที่อยู่เซิร์ฟเวอร์ที่หน้าเว็บเรียก: ถ้าไม่ตั้ง `VITE_API_URL` ตอน build เว็บจะใช้ที่อยู่ของตัวเอง (ให้เซิร์ฟเวอร์เสิร์ฟหน้าเว็บเอง)
ส่วนแอป Windows ตอนเปิดครั้งแรกจะให้เลือก 2 ทาง: ใส่ที่อยู่เซิร์ฟเวอร์ Helpdesk ของบริษัท หรือ "ตั้งค่า SQL Server เอง" เมื่อเซิร์ฟเวอร์อยู่ในเครื่องนี้ (แอปเปิดฟอร์ม Server Name / ล็อกอิน / รหัสผ่านให้ ตั้งค่าได้เฉพาะเซิร์ฟเวอร์ในเครื่องเดียวกัน หรือเครื่องอื่นที่ใส่รหัสตั้งค่าถูกต้อง) (ฝ่าย IT กำหนดล่วงหน้าได้ด้วย `HELPDESK_SERVER_URL` หรือไฟล์
`%ProgramData%\Helpdesk\server.txt`) โปรเจกต์ไม่มีเซิร์ฟเวอร์ตั้งต้นที่ชี้ไประบบของใคร

---

## API Endpoints

### Authentication
```
POST /api/auth/login       # เข้าสู่ระบบ
GET  /api/auth/me          # ข้อมูล user ปัจจุบัน + permissions
POST /api/auth/register    # สร้างบัญชี Staff (ต้องมีสิทธิ์ user.create — ไม่เปิดสาธารณะแล้ว)
```

### Issues
```
GET    /api/issues                      # รายการ (กรอง ค้นหา เรียง แบ่งหน้า) ตามสิทธิ์
GET    /api/issues/summary              # ตัวเลขสรุป (เปิดอยู่ / ใหม่ / เกินกำหนด ...)
GET    /api/issues/{id}                 # รายละเอียด + ไทม์ไลน์ + สิ่งที่ทำได้
POST   /api/issues                      # แจ้งปัญหา (multipart + รูป)
POST   /api/issues/{id}/comments        # ตอบกลับ / โน้ตภายใน
POST   /api/issues/{id}/assign|start|request-info|resolve|confirm|reopen|reject
PUT    /api/issues/{id}/priority
POST   /api/issues/{id}/attachments     # แนบรูปเพิ่ม
GET    /api/issues/{id}/attachments/{attachmentId}
GET    /api/public/departments          # ไม่ต้องล็อกอิน
POST   /api/public/issues               # แจ้งโดยไม่ต้องล็อกอิน
```

สิทธิ์ของแต่ละ role และ workflow: [docs/access-and-workflow.md](docs/access-and-workflow.md)

### Real-time (SignalR)
```
/hubs/kpi    # ส่ง IssueChanged / Notification ไปยัง client ที่เกี่ยวข้อง (ชื่อเส้นทางเป็นของเดิม)
```

### Health
```
GET /health          # process ทำงานอยู่ (ไม่แตะ DB) — ใช้เป็น Health Check Path บน Render
GET /health/ready    # ตรวจการเชื่อมต่อ DB ด้วย (503 ถ้าต่อไม่ได้)
```

### Audit
```
GET /api/audit               # ดู Audit Logs
```

### Jobs
```
POST /api/jobs/trigger-maintenance  # รันงานดูแลประจำวันทันที (ปิดเรื่องที่ตอบแล้ว ล้าง log เก่า)
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
`App__BaseUrl` (URL ของ frontend สำหรับปุ่ม "เปิดเรื่อง" ในอีเมล), `Email__Enabled=false` เพื่อปิดการส่ง —
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
| Maintenance | ทุกวัน | ปิดเรื่องที่แก้ไขแล้วและไม่มีใครตอบ ล้าง request log เก่า |

Hangfire Dashboard: `http://localhost:5079/hangfire`

---

## Roadmap

กำลังพัฒนาเป็น Helpdesk เต็มรูปแบบ: หมวดหมู่/แคตตาล็อกบริการที่แอดมินกำหนดเอง, SLA ตามนโยบายและเวลาทำการ,
Flow การอนุมัติที่ผู้ดูแลระบบตั้งเองได้, ทีม/คิวงาน, ฐานความรู้, ติดตามเรื่องโดยไม่ต้องล็อกอิน, แบบประเมินความพึงพอใจ, รายงาน

---

## License

![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)

MIT License — feel free to use and modify
