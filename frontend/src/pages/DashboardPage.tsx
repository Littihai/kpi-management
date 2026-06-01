import { useAuthStore } from '../stores/authStore'
import { useNavigate } from 'react-router-dom'

export default function DashboardPage() {
  const { user, logout } = useAuthStore()
  const navigate = useNavigate()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  return (
    <div className="min-h-screen bg-gray-50 p-8">
      <div className="max-w-4xl mx-auto">
        <div className="flex justify-between items-center mb-8">
          <h1 className="text-2xl font-semibold text-gray-800">Dashboard</h1>
          <button
            onClick={handleLogout}
            className="text-sm text-gray-500 hover:text-gray-700"
          >
            Sign out
          </button>
        </div>

        <div className="bg-white rounded-xl shadow-sm p-6">
          <p className="text-gray-600">Welcome back,</p>
          <p className="text-xl font-medium text-gray-800 mt-1">{user?.fullName}</p>
          <span className="inline-block mt-2 px-3 py-1 bg-blue-50 text-blue-600 text-xs rounded-full">
            {user?.role}
          </span>
        </div>
      </div>
    </div>
  )
}