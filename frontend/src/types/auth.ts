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
  passwordChangeRequired?: boolean
  passwordChangeReason?: string | null
}

export interface AuthUser {
  email: string
  fullName: string
  role: string
  departmentId?: string | null
  departmentName?: string | null
  permissions?: string[]
  /** The person must choose a new password before anything else works ("forced" or "expired"). */
  passwordChangeRequired?: boolean
  passwordChangeReason?: string | null
}

export interface UserProfile {
  id: string
  email: string
  fullName: string
  role: string
  departmentId: string | null
  departmentName: string | null
  permissions: string[]
  passwordChangeRequired?: boolean
  passwordChangeReason?: string | null
}
