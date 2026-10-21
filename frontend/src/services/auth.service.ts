import api from './api'
import type { LoginRequest, RegisterRequest, AuthResponse, UserProfile } from '../types/auth'

export const authService = {
  async login(data: LoginRequest): Promise<AuthResponse> {
    const res = await api.post<AuthResponse>('/api/auth/login', data)
    return res.data
  },

  async register(data: RegisterRequest): Promise<AuthResponse> {
    const res = await api.post<AuthResponse>('/api/auth/register', data)
    return res.data
  },

  async me(): Promise<UserProfile> {
    const res = await api.get<UserProfile>('/api/auth/me')
    return res.data
  },

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await api.post('/api/auth/change-password', { currentPassword, newPassword })
  },

  logout() {
    localStorage.removeItem('token')
    localStorage.removeItem('user')
  }
}
