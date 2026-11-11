import { request } from './client'

export interface TaskItem {
  id: string
  title: string
  description?: string | null
  isCompleted: boolean
  createdAt: string
}

export const tasksApi = {
  list: () => request<TaskItem[]>('/tasks'),
  create: (title: string, description?: string) =>
    request<TaskItem>('/tasks', { method: 'POST', body: JSON.stringify({ title, description }) }),
  update: (task: TaskItem) =>
    request<TaskItem>(`/tasks/${task.id}`, {
      method: 'PUT',
      body: JSON.stringify({
        title: task.title,
        description: task.description,
        isCompleted: task.isCompleted,
      }),
    }),
  remove: (id: string) => request<void>(`/tasks/${id}`, { method: 'DELETE' }),
}
