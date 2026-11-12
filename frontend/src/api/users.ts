import { request } from './client'

/** A user as shown to other users: for the assignee picker and "assigned to" labels. */
export interface UserSummary {
  id: string
  email: string
  displayName: string
}

export const usersApi = {
  list: () => request<UserSummary[]>('/users'),
  /**
   * How many open tasks a user already has between two dates (inclusive), leaving out one task
   * when checking for the task being edited.
   */
  workload: (userId: string, from: string, to: string, excludeTaskId?: string) => {
    const query = new URLSearchParams({ from, to })
    if (excludeTaskId) query.set('excludeTaskId', excludeTaskId)
    return request<{ count: number }>(`/users/${userId}/workload?${query}`)
  },
}
