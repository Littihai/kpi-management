# KPI Management System

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

---

## Tech Stack

### Backend
| Technology | Version | Purpose |
|-----------|---------|---------|
| ASP.NET Core | .NET 10 | Web API Framework |
| Entity Framework Core | 10.x | ORM + Database Migration |
| PostgreSQL (Supabase) | 15+ | Primary Database |
| Hangfire | Latest | Background Jobs |
| QuestPDF | Latest | PDF Generation |
| ClosedXML | Latest | Excel Generation |
| Resend | Latest | Email Service |
| JWT Bearer | Latest | Authentication |

### Frontend
| Technology | Version | Purpose |
|-----------|---------|---------|
| React | 18+ | UI Framework |
| TypeScript | 5+ | Type Safety |
| Tailwind CSS | 4.x | Styling |
| Vite | 6.x | Build Tool |
| Zustand | Latest | State Management |
| Axios | Latest | HTTP Client |
| Recharts | Latest | Charts & Visualization |
| React Router | 6+ | Client-side Routing |

### Infrastructure
| Service | Purpose |
|---------|---------|
| Supabase | PostgreSQL Database |
| Render | Backend API Hosting |
| Vercel | Frontend Hosting |
| GitHub | Version Control |
| Resend | Email Delivery |

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
- .NET 10 SDK
- Node.js 20+
- PostgreSQL (or Supabase account)

### Backend Setup

```bash
cd backend

# Restore packages
dotnet restore

# Set connection string in appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=postgres;Username=...;Password=..."
  },
  "Jwt": {
    "Key": "your-secret-key-minimum-32-characters",
    "Issuer": "kpi-management",
    "Audience": "kpi-management"
  },
  "Resend": {
    "ApiKey": "re_your_api_key"
  }
}

# Run migrations
dotnet ef database update --project KPI.Infrastructure --startup-project KPI.API

# Start API
cd KPI.API
dotnet run
```

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
POST /api/auth/register    # สมัครสมาชิก
POST /api/auth/login       # เข้าสู่ระบบ
GET  /api/auth/me          # ข้อมูล user ปัจจุบัน
```

### KPI
```
GET    /api/kpi              # ดู KPI ทั้งหมด
GET    /api/kpi/{id}         # ดู KPI รายการ
POST   /api/kpi              # สร้าง KPI
POST   /api/kpi/{id}/submit  # ส่ง approve
POST   /api/kpi/{id}/approve # อนุมัติ KPI
POST   /api/kpi/progress     # อัปเดต progress
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

### Frontend (.env.local)
```
VITE_API_URL=https://your-api.onrender.com
```

---

## Deployment

### Backend → Render
1. Connect GitHub repository
2. Root Directory: `backend`
3. Dockerfile Path: `KPI.API/Dockerfile`
4. Add Environment Variables

### Frontend → Vercel
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

MIT License
