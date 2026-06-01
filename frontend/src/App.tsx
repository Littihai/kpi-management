import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import KpiListPage from './pages/KpiListPage'
import KpiDetailPage from './pages/KpiDetailPage'
import KpiCreatePage from './pages/KpiCreatePage'
import ProtectedRoute from './components/ProtectedRoute'

function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white border-b border-gray-100 px-8 py-4 flex gap-6">
        <a href="/dashboard" className="text-sm text-gray-600 hover:text-gray-900">Dashboard</a>
        <a href="/kpi" className="text-sm text-gray-600 hover:text-gray-900">KPI</a>
      </nav>
      {children}
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