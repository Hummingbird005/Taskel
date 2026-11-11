import { ApiError, request } from './client'

export interface User {
  id: string
  email: string
}

const credentials = (email: string, password: string) => ({
  method: 'POST',
  body: JSON.stringify({ email, password }),
})

export const authApi = {
  /** Returns the signed-in user, or null when there is no session. */
  me: async (): Promise<User | null> => {
    try {
      return await request<User>('/auth/me')
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) return null
      throw err
    }
  },
  login: (email: string, password: string) => request<User>('/auth/login', credentials(email, password)),
  register: (email: string, password: string) => request<User>('/auth/register', credentials(email, password)),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
}
