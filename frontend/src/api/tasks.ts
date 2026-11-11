export interface TaskItem {
  id: string
  title: string
  description?: string | null
  isCompleted: boolean
  createdAt: string
}

const BASE = `${import.meta.env.VITE_API_BASE_URL ?? ''}/api/tasks`

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (!res.ok) throw new Error(`Request failed: ${res.status}`)
  return (res.status === 204 ? undefined : await res.json()) as T
}

export const tasksApi = {
  list: () => request<TaskItem[]>(BASE),
  create: (title: string, description?: string) =>
    request<TaskItem>(BASE, { method: 'POST', body: JSON.stringify({ title, description }) }),
  update: (task: TaskItem) =>
    request<TaskItem>(`${BASE}/${task.id}`, {
      method: 'PUT',
      body: JSON.stringify({
        title: task.title,
        description: task.description,
        isCompleted: task.isCompleted,
      }),
    }),
  remove: (id: string) => request<void>(`${BASE}/${id}`, { method: 'DELETE' }),
}
