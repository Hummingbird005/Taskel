import { request } from './client'

/** A user as shown to other users: for the assignee picker and "assigned to" labels. */
export interface UserSummary {
  id: string
  email: string
  displayName: string
}

export const usersApi = {
  list: () => request<UserSummary[]>('/users'),
}
