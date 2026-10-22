import { lazy, Suspense } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import AppShell from './components/AppShell'
import UpdateBanner from './components/UpdateBanner'
import ProtectedRoute, { RequirePermission } from './components/ProtectedRoute'
import { Page, PageSkeleton } from './components/ui/Layout'
import BrandLoader from './components/BrandLoader'
import PasswordGate from './components/PasswordGate'
import SetupGate from './components/SetupGate'
import SettingsWindow from './components/settings/SettingsWindow'
import { isSettingsPath, RealLocationContext, type SettingsLocationState } from './components/settings/settingsNav'
import LoginPage from './pages/LoginPage'
import { useAuthStore } from './stores/authStore'

// Each page is loaded when first visited, so the first screen downloads far less code
// (the charts library alone is large and only a few pages use it).
const DashboardPage = lazy(() => import('./pages/DashboardPage'))
const OverviewPage = lazy(() => import('./pages/admin/OverviewPage'))
const IssuesPage = lazy(() => import('./pages/issues/IssuesPage'))
const IssueNewPage = lazy(() => import('./pages/issues/IssueNewPage'))
const IssueDetailPage = lazy(() => import('./pages/issues/IssueDetailPage'))
const PublicReportPage = lazy(() => import('./pages/issues/PublicReportPage'))

function Home() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  return <Navigate to={isAuthenticated ? '/dashboard' : '/login'} replace />
}

/**
 * Settings open as a window over the page the person was on. While they are open the routes keep rendering that
 * page ("background location"); opened cold from a link, the dashboard is shown behind instead.
 */
function AppRoutes() {
  const location = useLocation()
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  const settingsOpen = isSettingsPath(location.pathname)
  const background = (location.state as SettingsLocationState | null)?.backgroundLocation
  const routesLocation = settingsOpen
    ? background ?? { ...location, pathname: '/dashboard', search: '', hash: '', state: null, key: 'settings-bg' }
    : location

  return (
    <RealLocationContext.Provider value={location}>
      <Suspense fallback={<Page><PageSkeleton /></Page>}>
        <Routes location={routesLocation}>
          <Route path="/login" element={<LoginPage />} />
          {/* Works without an account, for people who cannot sign in */}
          <Route path="/report" element={<PublicReportPage />} />

          <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
            <Route path="/dashboard" element={<RequirePermission permission="issue.view"><DashboardPage /></RequirePermission>} />
            <Route path="/issues" element={<RequirePermission permission="issue.view"><IssuesPage /></RequirePermission>} />
            <Route path="/issues/new" element={<RequirePermission permission="issue.create"><IssueNewPage /></RequirePermission>} />
            <Route path="/issues/:id" element={<RequirePermission permission="issue.view"><IssueDetailPage /></RequirePermission>} />

            <Route path="/admin/overview" element={<RequirePermission permission="overview.view"><OverviewPage /></RequirePermission>} />
          </Route>

          {/* The admin screens moved into the settings window; old links still land there */}
          <Route path="/admin/users" element={<Navigate to="/settings/users" replace />} />
          <Route path="/admin/departments" element={<Navigate to="/settings/departments" replace />} />
          <Route path="/admin/audit" element={<Navigate to="/settings/audit" replace />} />

          <Route path="/" element={<Home />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
        {settingsOpen && isAuthenticated && <SettingsWindow />}
        <PasswordGate />
      </Suspense>
    </RealLocationContext.Provider>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <SetupGate>
        <AppRoutes />
      </SetupGate>
      <BrandLoader />
      <UpdateBanner />
    </BrowserRouter>
  )
}
