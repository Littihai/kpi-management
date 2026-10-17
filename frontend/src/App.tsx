import { lazy, Suspense } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import AppShell from './components/AppShell'
import ProtectedRoute, { RequirePermission } from './components/ProtectedRoute'
import { Page, PageSkeleton } from './components/ui/Layout'
import LoginPage from './pages/LoginPage'
import { useAuthStore } from './stores/authStore'

// Each page is loaded when first visited, so the first screen downloads far less code
// (the charts library alone is large and only a few pages use it).
const DashboardPage = lazy(() => import('./pages/DashboardPage'))
const KpiListPage = lazy(() => import('./pages/KpiListPage'))
const KpiDetailPage = lazy(() => import('./pages/KpiDetailPage'))
const KpiCreatePage = lazy(() => import('./pages/KpiCreatePage'))
const UsersPage = lazy(() => import('./pages/admin/UsersPage'))
const OverviewPage = lazy(() => import('./pages/admin/OverviewPage'))
const AuditPage = lazy(() => import('./pages/admin/AuditPage'))
const IssuesPage = lazy(() => import('./pages/issues/IssuesPage'))
const IssueNewPage = lazy(() => import('./pages/issues/IssueNewPage'))
const IssueDetailPage = lazy(() => import('./pages/issues/IssueDetailPage'))
const PublicReportPage = lazy(() => import('./pages/issues/PublicReportPage'))

function Home() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  return <Navigate to={isAuthenticated ? '/dashboard' : '/login'} replace />
}

export default function App() {
  return (
    <BrowserRouter>
      <Suspense fallback={<Page><PageSkeleton /></Page>}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          {/* Works without an account, for people who cannot sign in */}
          <Route path="/report" element={<PublicReportPage />} />

          <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
            <Route path="/dashboard" element={<RequirePermission permission="kpi.view"><DashboardPage /></RequirePermission>} />
            <Route path="/kpi" element={<RequirePermission permission="kpi.view"><KpiListPage /></RequirePermission>} />
            <Route path="/kpi/create" element={<RequirePermission permission="kpi.create"><KpiCreatePage /></RequirePermission>} />
            <Route path="/kpi/:id/edit" element={<RequirePermission permission="kpi.edit"><KpiCreatePage /></RequirePermission>} />
            <Route path="/kpi/:id" element={<RequirePermission permission="kpi.view"><KpiDetailPage /></RequirePermission>} />

            <Route path="/issues" element={<RequirePermission permission="issue.view"><IssuesPage /></RequirePermission>} />
            <Route path="/issues/new" element={<RequirePermission permission="issue.create"><IssueNewPage /></RequirePermission>} />
            <Route path="/issues/:id" element={<RequirePermission permission="issue.view"><IssueDetailPage /></RequirePermission>} />

            <Route path="/admin/users" element={<RequirePermission permission="user.view"><UsersPage /></RequirePermission>} />
            <Route path="/admin/overview" element={<RequirePermission permission="overview.view"><OverviewPage /></RequirePermission>} />
            <Route path="/admin/audit" element={<RequirePermission permission="overview.view"><AuditPage /></RequirePermission>} />
          </Route>

          <Route path="/" element={<Home />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Suspense>
    </BrowserRouter>
  )
}
