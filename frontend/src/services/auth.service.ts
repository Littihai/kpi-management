import api from './api'
import type { LoginRequest, RegisterRequest, AuthResponse } from '../types/auth'

export const authService = {
  async login(data: LoginRequest): Promise<AuthResponse> {
    const res = await api.post<AuthResponse>('/api/auth/login', data)
    return res.data
  },

  async register(data: RegisterRequest): Promise<AuthResponse> {
    const res = await api.post<AuthResponse>('/api/auth/register', data)
    return res.data
  },

  async me(): Promise<{ email: string; role: string }> {
    const res = await api.get('/api/auth/me')
    return res.data
  },

  logout() {
    localStorage.removeItem('token')
    localStorage.removeItem('user')
  }
}