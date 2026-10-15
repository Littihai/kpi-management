export interface LoginRequest {
  email: string
  password: string
}

export interface RegisterRequest {
  firstName: string
  lastName: string
  email: string
  password: string
}

export interface AuthResponse {
  token: string
  email: string
  fullName: string
  role: string
  departmentId: string | null
  permissions: string[]
}

export interface AuthUser {
  email: string
  fullName: string
  role: string
  departmentId?: string | null
  departmentName?: string | null
  permissions?: string[]
}

export interface UserProfile {
  id: string
  email: string
  fullName: string
  role: string
  departmentId: string | null
  departmentName: string | null
  permissions: string[]
}
