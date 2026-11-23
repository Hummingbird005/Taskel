import { request } from './client'
import type { TaskItem, TaskPriority } from './tasks'
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

/** A task as placed on the team calendar. */
export interface CalendarTask {
  id: string
  title: string
  startDate: string | null
  dueDate: string
  priority: TaskPriority
}

export interface CalendarMember {
  user: UserSummary
  /** Open tasks with a due date that fall, at least partly, inside the period. */
  tasks: CalendarTask[]
  /** Open tasks without a due date, which the calendar cannot place. */
  unscheduled: number
}

export interface TeamCalendar {
  department: string | null
  from: string
  to: string
  members: CalendarMember[]
}

export const teamApi = {
  /** The department's open, dated tasks per person, for the days from..to (YYYY-MM-DD, inclusive). */
  calendar: (from: string, to: string, department?: string | null) => {
    const query = new URLSearchParams({ from, to })
    if (department) query.set('department', department)
    return request<TeamCalendar>(`/team/calendar?${query}`)
  },
  /** @param today The viewer's local date (YYYY-MM-DD), so "overdue" matches what they see elsewhere. */
  get: (today: string, department?: string | null) => {
    const query = new URLSearchParams({ today })
    if (department) query.set('department', department)
    return request<Team>(`/team?${query}`)
  },
  /** Every task the department's people are responsible for: open ones, and those completed in the last two weeks. */
  tasks: (department?: string | null) =>
    request<TaskItem[]>(`/team/tasks${department ? `?${new URLSearchParams({ department })}` : ''}`),
  /** Moves the due date of a task one of the manager's people is responsible for; null removes it. */
  changeDueDate: (taskId: string, dueDate: string | null) =>
    request<TaskItem>(`/team/tasks/${taskId}/due-date`, { method: 'PUT', body: JSON.stringify({ dueDate }) }),
  /** The tasks a member is responsible for, in full, for reading. */
  memberTasks: (userId: string) => request<TaskItem[]>(`/team/members/${userId}/tasks`),
}
