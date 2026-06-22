import { BrowserRouter, Routes, Route, Navigate, useNavigate, useLocation } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import KpiListPage from './pages/KpiListPage'
import KpiDetailPage from './pages/KpiDetailPage'
import KpiCreatePage from './pages/KpiCreatePage'
import ProtectedRoute from './components/ProtectedRoute'
import { useAuthStore } from './stores/authStore'

function Sidebar() {
  const navigate = useNavigate()
  const location = useLocation()
  const { user, logout } = useAuthStore()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  const isActive = (path: string) =>
    location.pathname.startsWith(path)
      ? 'bg-white text-gray-900 font-medium'
      : 'text-gray-500 hover:text-gray-700'

  return (
    <div className="w-52 flex-shrink-0 bg-gray-50 border-r border-gray-100 min-h-screen flex flex-col">
      <div className="p-4 border-b border-gray-100">
        <h1 className="text-sm font-semibold text-indigo-600">KPI System</h1>
      </div>
      <nav className="flex-1 p-3 space-y-1">
        <button
          onClick={() => navigate('/dashboard')}
          className={`w-full text-left px-3 py-2 rounded-lg text-sm ${isActive('/dashboard')}`}
        >
          Dashboard
        </button>
        <button
          onClick={() => navigate('/kpi')}
          className={`w-full text-left px-3 py-2 rounded-lg text-sm ${isActive('/kpi')}`}
        >
          KPI
        </button>
      </nav>
      <div className="p-3 border-t border-gray-100">
        <p className="text-xs text-gray-500 mb-1">{user?.fullName}</p>
        <p className="text-xs text-gray-400 mb-2">{user?.role}</p>
        <button
          onClick={handleLogout}
          className="text-xs text-gray-400 hover:text-gray-600"
        >
          Sign out
        </button>
      </div>
    </div>
  )
}

function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen bg-gray-50">
      <Sidebar />
      <main className="flex-1 overflow-auto">
        {children}
      </main>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/dashboard" element={
          <ProtectedRoute>
            <Layout><DashboardPage /></Layout>
          </ProtectedRoute>
        } />
        <Route path="/kpi" element={
          <ProtectedRoute>
            <Layout><KpiListPage /></Layout>
          </ProtectedRoute>
        } />
        <Route path="/kpi/create" element={
          <ProtectedRoute>
            <Layout><KpiCreatePage /></Layout>
          </ProtectedRoute>
        } />
        <Route path="/kpi/:id" element={
          <ProtectedRoute>
            <Layout><KpiDetailPage /></Layout>
          </ProtectedRoute>
        } />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  )
}