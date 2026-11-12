import { request } from './client'
import type { TaskItem } from './tasks'
import type { UserSummary } from './users'
import type { UserRole } from './auth'

/** Workload figures for one person, or summed for a department. */
export interface TeamStats {
  open: number
  overdue: number
  dueToday: number
  dueNext7Days: number
  highPriorityOpen: number
  completedLast7Days: number
  completedLast30Days: number
}

export interface TeamMember {
  user: UserSummary
  role: UserRole
  stats: TeamStats
}

export interface Team {
  /** The department shown; null when the manager has none set. */
  department: string | null
  /** The departments the caller may look at. */
  departments: string[]
  members: TeamMember[]
  totals: TeamStats
}

export const teamApi = {
  /** @param today The viewer's local date (YYYY-MM-DD), so "overdue" matches what they see elsewhere. */
  get: (today: string, department?: string | null) => {
    const query = new URLSearchParams({ today })
    if (department) query.set('department', department)
    return request<Team>(`/team?${query}`)
  },
  /** The tasks a member is responsible for, in full, for reading. */
  memberTasks: (userId: string) => request<TaskItem[]>(`/team/members/${userId}/tasks`),
}
