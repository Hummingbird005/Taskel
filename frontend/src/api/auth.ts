import { ApiError, request } from './client'

export type UserRole = 'user' | 'manager' | 'admin'

export interface User {
  id: string
  email: string
  /** Name shown to other users; the email when the account has no name. */
  displayName: string
  /** True for accounts that sign in through Active Directory. */
  isDirectoryAccount: boolean
  /** Managers see their department's workload; administrators also manage users. */
  role: UserRole
  department: string | null
}

/** What the sign-in screen should offer. */
export interface AuthOptions {
  localRegistration: boolean
  activeDirectory: boolean
  activeDirectoryDomain: string | null
}

export const DEFAULT_AUTH_OPTIONS: AuthOptions = {
  localRegistration: true,
  activeDirectory: false,
  activeDirectoryDomain: null,
}

const post = (body: object) => ({ method: 'POST', body: JSON.stringify(body) })

export const authApi = {
  options: () => request<AuthOptions>('/auth/options'),
  /** Returns the signed-in user, or null when there is no session. */
  me: async (): Promise<User | null> => {
    try {
      return await request<User>('/auth/me')
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) return null
      throw err
    }
  },
  login: (email: string, password: string) => request<User>('/auth/login', post({ email, password })),
  /** Signs in with an Active Directory account. */
  loginDirectory: (username: string, password: string) =>
    request<User>('/auth/login/directory', post({ username, password })),
  register: (email: string, password: string) => request<User>('/auth/register', post({ email, password })),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
}
