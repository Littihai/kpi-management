import { useEffect } from 'react'
import { BrowserRouter, Routes, Route, Navigate, Outlet } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import KpiListPage from './pages/KpiListPage'
import KpiDetailPage from './pages/KpiDetailPage'
import KpiCreatePage from './pages/KpiCreatePage'
import UsersPage from './pages/admin/UsersPage'
import ProtectedRoute from './components/ProtectedRoute'
import Sidebar from './components/Sidebar'
import { RealtimeProvider } from './components/RealtimeProvider'
import { authService } from './services/auth.service'
import { useAuthStore } from './stores/authStore'

// Placeholder pages ชั่วคราว
const ComingSoon = ({ title }: { title: string }) => (
  <div className="flex flex-col items-center justify-center h-full text-gray-400 gap-3">
    <i className="ti ti-tools text-4xl"></i>
    <p className="text-lg font-medium text-gray-600">{title}</p>
    <p className="text-sm">Coming soon</p>
  </div>
)

/** Signed-in area: sidebar, live updates, and a fresh copy of the user's permissions. */
function AppShell() {
  const updateUser = useAuthStore((s) => s.updateUser)

  // Permissions are decided by the server and can change between sign-ins, so refresh them on load.
  useEffect(() => {
    authService.me()
      .then((profile) => updateUser({
        fullName: profile.fullName,
        role: profile.role,
        departmentId: profile.departmentId,
        departmentName: profile.departmentName,
        permissions: profile.permissions,
      }))
      .catch(() => { /* 401 is handled by the API client; other errors keep the cached profile */ })
  }, [updateUser])

  return (
    <RealtimeProvider>
      <div className="flex min-h-screen bg-gray-50">
        <Sidebar />
        <main className="flex-1 overflow-auto">
          <Outlet />
        </main>
      </div>
    </RealtimeProvider>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/kpi" element={<KpiListPage />} />
          <Route path="/kpi/create" element={<KpiCreatePage />} />
          <Route path="/kpi/:id/edit" element={<KpiCreatePage />} />
          <Route path="/kpi/:id" element={<KpiDetailPage />} />
          <Route path="/projects" element={<ComingSoon title="Projects" />} />
          <Route path="/tasks" element={<ComingSoon title="Tasks" />} />
          <Route path="/analytics" element={<ComingSoon title="Analytics" />} />
          <Route path="/export" element={<ComingSoon title="Export" />} />
          <Route path="/admin/users" element={<UsersPage />} />
          <Route path="/admin/departments" element={<ComingSoon title="Departments" />} />
        </Route>
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
