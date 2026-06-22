import { useEffect, useState } from 'react'
import api from '../../services/api'
import AddUserModal from '../../components/admin/AddUserModal'

interface UserDto {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  department: string | null
  isActive: boolean
}

const roleColor: Record<string, string> = {
  SuperAdmin: 'bg-purple-50 text-purple-700',
  Director: 'bg-blue-50 text-blue-700',
  Manager: 'bg-indigo-50 text-indigo-700',
  TeamLeader: 'bg-teal-50 text-teal-700',
  Staff: 'bg-gray-100 text-gray-600',
  Viewer: 'bg-gray-50 text-gray-500',
}

export default function UsersPage() {
  const [users, setUsers] = useState<UserDto[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [showModal, setShowModal] = useState(false)

  const loadUsers = () => {
    setLoading(true)
    api.get('/api/users')
      .then(res => setUsers(res.data))
      .finally(() => setLoading(false))
  }

  useEffect(() => { loadUsers() }, [])

  const filtered = users.filter(u =>
    `${u.firstName} ${u.lastName} ${u.email}`.toLowerCase().includes(search.toLowerCase())
  )

  const initials = (u: UserDto) =>
    `${u.firstName?.[0] ?? ''}${u.lastName?.[0] ?? ''}`.toUpperCase()

  const handleDeactivate = async (id: string) => {
    if (!confirm('Deactivate this user?')) return
    await api.put(`/api/users/${id}/deactivate`)
    loadUsers()
  }

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Topbar */}
      <div className="bg-white border-b border-gray-100 px-8 py-4 flex justify-between items-center flex-shrink-0">
        <div>
          <h1 className="text-base font-medium text-gray-800">User Management</h1>
          <p className="text-xs text-gray-400 mt-0.5">{users.length} users in system</p>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="flex items-center gap-1.5 px-3 py-2 bg-indigo-600 text-white rounded-lg text-xs hover:bg-indigo-700"
        >
          <i className="ti ti-user-plus text-sm"></i> Add User
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-8">
        {/* Metrics */}
        <div className="grid grid-cols-4 gap-4 mb-6">
          {[
            { label: 'Total Users', value: users.length, icon: 'ti-users', bg: 'bg-indigo-50', color: 'text-indigo-600' },
            { label: 'Active', value: users.filter(u => u.isActive).length, icon: 'ti-circle-check', bg: 'bg-green-50', color: 'text-green-600' },
            { label: 'Inactive', value: users.filter(u => !u.isActive).length, icon: 'ti-user-off', bg: 'bg-red-50', color: 'text-red-500' },
            { label: 'Departments', value: new Set(users.map(u => u.department).filter(Boolean)).size, icon: 'ti-building', bg: 'bg-purple-50', color: 'text-purple-600' },
          ].map(m => (
            <div key={m.label} className="bg-white border border-gray-100 rounded-xl p-4">
              <div className={`w-8 h-8 ${m.bg} rounded-lg flex items-center justify-center mb-3`}>
                <i className={`ti ${m.icon} ${m.color} text-base`}></i>
              </div>
              <div className="text-xs text-gray-400 mb-1">{m.label}</div>
              <div className={`text-2xl font-medium ${m.color}`}>{m.value}</div>
            </div>
          ))}
        </div>

        {/* Search */}
        <div className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-100 rounded-lg w-72 mb-4">
          <i className="ti ti-search text-gray-400 text-sm"></i>
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search users..."
            className="flex-1 text-xs outline-none bg-transparent text-gray-700"
          />
        </div>

        {/* Table */}
        <div className="bg-white border border-gray-100 rounded-xl overflow-hidden">
          <table className="w-full">
            <thead>
              <tr className="border-b border-gray-100">
                <th className="text-left text-xs text-gray-400 font-medium px-5 py-3">User</th>
                <th className="text-left text-xs text-gray-400 font-medium px-5 py-3">Role</th>
                <th className="text-left text-xs text-gray-400 font-medium px-5 py-3">Department</th>
                <th className="text-left text-xs text-gray-400 font-medium px-5 py-3">Status</th>
                <th className="text-left text-xs text-gray-400 font-medium px-5 py-3">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={5} className="text-center py-12 text-sm text-gray-400">Loading...</td></tr>
              ) : filtered.length === 0 ? (
                <tr><td colSpan={5} className="text-center py-12 text-sm text-gray-400">No users found</td></tr>
              ) : filtered.map(user => (
                <tr key={user.id} className="border-b border-gray-50 hover:bg-gray-50 transition-colors">
                  <td className="px-5 py-3">
                    <div className="flex items-center gap-3">
                      <div className="w-8 h-8 rounded-full bg-indigo-50 flex items-center justify-center text-xs font-medium text-indigo-600 flex-shrink-0">
                        {initials(user)}
                      </div>
                      <div>
                        <div className="text-sm font-medium text-gray-800">{user.firstName} {user.lastName}</div>
                        <div className="text-xs text-gray-400">{user.email}</div>
                      </div>
                    </div>
                  </td>
                  <td className="px-5 py-3">
                    <span className={`text-xs px-2 py-1 rounded-full font-medium ${roleColor[user.role] ?? 'bg-gray-100 text-gray-600'}`}>
                      {user.role}
                    </span>
                  </td>
                  <td className="px-5 py-3 text-sm text-gray-600">{user.department ?? '—'}</td>
                  <td className="px-5 py-3">
                    <span className={`text-xs px-2 py-1 rounded-full font-medium ${user.isActive ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-600'}`}>
                      {user.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-5 py-3">
                    <div className="flex items-center gap-3">
                      <button className="text-gray-400 hover:text-indigo-600 transition-colors">
                        <i className="ti ti-edit text-sm"></i>
                      </button>
                      {user.isActive && (
                        <button
                          onClick={() => handleDeactivate(user.id)}
                          className="text-gray-400 hover:text-red-500 transition-colors"
                        >
                          <i className="ti ti-user-off text-sm"></i>
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {showModal && (
        <AddUserModal
          onClose={() => setShowModal(false)}
          onSuccess={loadUsers}
        />
      )}
    </div>
  )
}