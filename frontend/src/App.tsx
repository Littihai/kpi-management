import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import KpiListPage from './pages/KpiListPage'
import KpiDetailPage from './pages/KpiDetailPage'
import KpiCreatePage from './pages/KpiCreatePage'
import UsersPage from './pages/admin/UsersPage'
import ProtectedRoute from './components/ProtectedRoute'
import Sidebar from './components/Sidebar'

function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen bg-gray-50">
      <Sidebar />
      <main className="flex-1 overflow-auto">{children}</main>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/dashboard" element={<ProtectedRoute><Layout><DashboardPage /></Layout></ProtectedRoute>} />
        <Route path="/kpi" element={<ProtectedRoute><Layout><KpiListPage /></Layout></ProtectedRoute>} />
        <Route path="/kpi/create" element={<ProtectedRoute><Layout><KpiCreatePage /></Layout></ProtectedRoute>} />
        <Route path="/kpi/:id" element={<ProtectedRoute><Layout><KpiDetailPage /></Layout></ProtectedRoute>} />
        <Route path="/admin/users" element={<ProtectedRoute><Layout><UsersPage /></Layout></ProtectedRoute>} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  )
}