import { useEffect, useState } from 'react'
import api from '../../services/api'

interface UserDto {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  department: string
  isActive: boolean
}

export default function UsersPage() {
  const [users, setUsers] = useState<UserDto[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')

  useEffect(() => {
    api.get('/api/users')
      .then(res => setUsers(res.data))
      .finally(() => setLoading(false))
  }, [])

  const filtered = users.filter(u =>
    `${u.firstName} ${u.lastName} ${u.email}`.toLowerCase().includes(search.toLowerCase())
  )

  const initials = (u: UserDto) =>
    `${u.firstName[0]}${u.lastName[0]}`.toUpperCase()

  const roleColor: Record<string, string> = {
    SuperAdmin: 'bg-purple-50 text-purple-700',
    Director: 'bg-blue-50 text-blue-700',
    Manager: 'bg-indigo-50 text-indigo-700',
    TeamLeader: 'bg-teal-50 text-teal-700',
    Staff: 'bg-gray-100 text-gray-600',
    Viewer: 'bg-gray-50 text-gray-500',
  }

  return (
    <div className="p-8">
      {/* Header */}
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-lg font-medium text-gray-800">User Management</h1>
          <p className="text-sm text-gray-400 mt-0.5">{users.length} users in system</p>
        </div>
        <button className="flex items-center gap-2 bg-indigo-600 text-white px-4 py-2 rounded-lg text-sm hover:bg-indigo-700">
          <i className="ti ti-plus text-base"></i> Invite User
        </button>
      </div>

      {/* Search */}
      <div className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-100 rounded-lg w-72 mb-4">
        <i className="ti ti-search text-gray-400 text-sm"></i>
        <input
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder="Search users..."
          className="flex-1 text-sm outline-none bg-transparent text-gray-700"
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
                <td className="px-5 py-3 text-sm text-gray-600">{user.department || '—'}</td>
                <td className="px-5 py-3">
                  <span className={`text-xs px-2 py-1 rounded-full font-medium ${user.isActive ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-600'}`}>
                    {user.isActive ? 'Active' : 'Inactive'}
                  </span>
                </td>
                <td className="px-5 py-3">
                  <div className="flex items-center gap-2">
                    <button className="text-gray-400 hover:text-indigo-600 transition-colors">
                      <i className="ti ti-edit text-sm"></i>
                    </button>
                    <button className="text-gray-400 hover:text-red-500 transition-colors">
                      <i className="ti ti-trash text-sm"></i>
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}