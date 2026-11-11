import { request } from './client'

export type TaskPriority = 'low' | 'medium' | 'high'

export interface TaskItem {
  id: string
  title: string
  description?: string | null
  isCompleted: boolean
  /** Calendar date as YYYY-MM-DD, or null when the task has no due date. */
  dueDate: string | null
  priority: TaskPriority
  tags: string[]
  createdAt: string
}

/** The editable fields of a task. */
export type TaskDraft = Pick<TaskItem, 'title' | 'dueDate' | 'priority' | 'tags'>

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
        dueDate: task.dueDate,
        priority: task.priority,
        tags: task.tags,
      }),
    }),
  remove: (id: string) => request<void>(`/tasks/${id}`, { method: 'DELETE' }),
}
