import { create } from 'zustand'
import type { AuthUser } from '../types/auth'

interface AuthState {
  token: string | null
  user: AuthUser | null
  isAuthenticated: boolean
  setAuth: (token: string, user: AuthUser) => void
  updateUser: (patch: Partial<AuthUser>) => void
  logout: () => void
}

export const useAuthStore = create<AuthState>((set, get) => ({
  token: localStorage.getItem('token'),
  user: JSON.parse(localStorage.getItem('user') || 'null'),
  isAuthenticated: !!localStorage.getItem('token'),

  setAuth: (token, user) => {
    localStorage.setItem('token', token)
    localStorage.setItem('user', JSON.stringify(user))
    set({ token, user, isAuthenticated: true })
  },

  updateUser: (patch) => {
    const current = get().user
    if (!current) return
    const user = { ...current, ...patch }
    localStorage.setItem('user', JSON.stringify(user))
    set({ user })
  },

  logout: () => {
    localStorage.removeItem('token')
    localStorage.removeItem('user')
    set({ token: null, user: null, isAuthenticated: false })
  },
}))

/** True when the signed-in user holds the permission. The server still enforces it on every call. */
export function useCan(permission: string): boolean {
  return useAuthStore((s) => s.user?.permissions?.includes(permission) ?? false)
}
