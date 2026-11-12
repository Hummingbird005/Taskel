import { request } from './client'
import type { UserRole } from './auth'

/** An account as the administrator sees it. */
export interface AdminUser {
  id: string
  email: string
  displayName: string
  role: UserRole
  department: string | null
  isDirectoryAccount: boolean
  /** True when the role comes from the server's configuration and cannot be changed here. */
  roleIsFixed: boolean
}

export const adminApi = {
  users: () => request<AdminUser[]>('/admin/users'),
  updateUser: (id: string, role: UserRole, department: string | null) =>
    request<AdminUser>(`/admin/users/${id}`, { method: 'PUT', body: JSON.stringify({ role, department }) }),
}
