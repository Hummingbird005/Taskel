import { request } from './client'
import type { UserSummary } from './users'

export type TaskPriority = 'low' | 'medium' | 'high'

export interface TaskItem {
  id: string
  title: string
  description?: string | null
  isCompleted: boolean
  /**
   * First day of the period the task is worked on (YYYY-MM-DD), or null. With the due date it
   * forms the task's interval; a task without it falls on its due date alone.
   */
  startDate: string | null
  /** Calendar date as YYYY-MM-DD, or null when the task has no due date. */
  dueDate: string | null
  priority: TaskPriority
  tags: string[]
  createdAt: string
  /**
   * Sender of the email this task came from. When set, the task can only be completed with a
   * response, which Taskel emails to this address.
   */
  sourceEmailAddress: string | null
  /** The response written when the task was completed. */
  completionResponse: string | null
  completedAt: string | null
  /** Who created the task. Only they can delete or reassign it. */
  createdBy: UserSummary
  /** Who the task is assigned to, when that is someone other than its creator. */
  assignee: UserSummary | null
}

/** The editable fields of a task. */
export type TaskDraft = Pick<TaskItem, 'title' | 'startDate' | 'dueDate' | 'priority' | 'tags'> & {
  description?: string | null
  /** User to assign the task to; null keeps it with its creator. Left out when not being changed. */
  assigneeId?: string | null
}

export const tasksApi = {
  list: () => request<TaskItem[]>('/tasks'),
  create: (draft: TaskDraft) => request<TaskItem>('/tasks', { method: 'POST', body: JSON.stringify(draft) }),
  update: (task: TaskItem) =>
    request<TaskItem>(`/tasks/${task.id}`, {
      method: 'PUT',
      body: JSON.stringify({
        title: task.title,
        description: task.description,
        isCompleted: task.isCompleted,
        startDate: task.startDate,
        dueDate: task.dueDate,
        priority: task.priority,
        tags: task.tags,
      }),
    }),
  /** Assigns a task to a user, or back to its creator with null. Only the creator may do this. */
  assign: (id: string, assigneeId: string | null) =>
    request<TaskItem>(`/tasks/${id}/assignee`, { method: 'PUT', body: JSON.stringify({ assigneeId }) }),
  /** Completes a task. For email tasks the response is required and is emailed to the sender. */
  complete: (id: string, response: string | null) =>
    request<TaskItem>(`/tasks/${id}/complete`, { method: 'POST', body: JSON.stringify({ response }) }),
  remove: (id: string) => request<void>(`/tasks/${id}`, { method: 'DELETE' }),
}
