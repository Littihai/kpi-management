import { useNavigate, useLocation } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

interface NavItem {
  path: string
  icon: string
  label: string
  badge?: number
  roles?: string[]
}

const navItems: NavItem[] = [
  { path: '/dashboard', icon: 'ti-home', label: 'Dashboard' },
  { path: '/kpi', icon: 'ti-target', label: 'KPI' },
  { path: '/projects', icon: 'ti-folder', label: 'Projects' },
  { path: '/tasks', icon: 'ti-checkbox', label: 'Tasks' },
]

const reportItems: NavItem[] = [
  { path: '/analytics', icon: 'ti-chart-line', label: 'Analytics' },
  { path: '/export', icon: 'ti-file-export', label: 'Export' },
]

const adminItems: NavItem[] = [
  { path: '/admin/users', icon: 'ti-users', label: 'Users', roles: ['SuperAdmin', 'Director', 'Manager'] },
  { path: '/admin/departments', icon: 'ti-building', label: 'Departments', roles: ['SuperAdmin', 'Director'] },
]

export default function Sidebar() {
  const navigate = useNavigate()
  const location = useLocation()
  const { user, logout } = useAuthStore()

  const isActive = (path: string) => location.pathname.startsWith(path)

  const canSee = (roles?: string[]) => {
    if (!roles) return true
    return roles.includes(user?.role ?? '')
  }

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  const NavLink = ({ item }: { item: NavItem }) => (
    <button
      onClick={() => navigate(item.path)}
      className={`w-full flex items-center gap-2.5 px-2.5 py-2 rounded-lg text-sm transition-colors ${
        isActive(item.path)
          ? 'bg-indigo-50 text-indigo-600 font-medium'
          : 'text-gray-500 hover:text-gray-800 hover:bg-gray-50'
      }`}
    >
      <i className={`ti ${item.icon} text-base`}></i>
      <span className="flex-1 text-left">{item.label}</span>
      {item.badge !== undefined && item.badge > 0 && (
        <span className="text-xs px-1.5 py-0.5 rounded-full bg-red-50 text-red-600">
          {item.badge}
        </span>
      )}
    </button>
  )

  return (
    <div className="w-56 flex-shrink-0 bg-white border-r border-gray-100 flex flex-col">
      {/* Logo */}
      <div className="px-4 py-5 border-b border-gray-100">
        <div className="w-8 h-8 rounded-lg bg-indigo-600 flex items-center justify-center mb-2">
          <i className="ti ti-chart-bar text-white text-base"></i>
        </div>
        <div className="text-sm font-medium text-gray-800">KPI System</div>
        <div className="text-xs text-gray-400 mt-0.5">Enterprise Platform</div>
      </div>

      {/* Nav */}
      <nav className="flex-1 p-2 flex flex-col gap-0.5 overflow-y-auto">
        <div className="text-xs text-gray-400 px-2 pt-2 pb-1 uppercase tracking-wider">Main</div>
        {navItems.filter(i => canSee(i.roles)).map(item => (
          <NavLink key={item.path} item={item} />
        ))}

        <div className="text-xs text-gray-400 px-2 pt-4 pb-1 uppercase tracking-wider">Reports</div>
        {reportItems.filter(i => canSee(i.roles)).map(item => (
          <NavLink key={item.path} item={item} />
        ))}

        {adminItems.some(i => canSee(i.roles)) && (
          <>
            <div className="text-xs text-gray-400 px-2 pt-4 pb-1 uppercase tracking-wider">Admin</div>
            {adminItems.filter(i => canSee(i.roles)).map(item => (
              <NavLink key={item.path} item={item} />
            ))}
          </>
        )}
      </nav>

      {/* User */}
      <div className="p-2 border-t border-gray-100">
        <div className="flex items-center gap-2.5 px-2.5 py-2">
          <div className="w-8 h-8 rounded-full bg-indigo-50 flex items-center justify-center text-xs font-medium text-indigo-600 flex-shrink-0">
            {user?.fullName?.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2)}
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-xs font-medium text-gray-800 truncate">{user?.fullName}</div>
            <div className="text-xs text-gray-400">{user?.role}</div>
          </div>
          <button onClick={handleLogout} className="text-gray-400 hover:text-gray-600">
            <i className="ti ti-logout text-sm"></i>
          </button>
        </div>
      </div>
    </div>
  )
}