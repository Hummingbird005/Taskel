import { request } from './client'

export interface NotificationSettings {
  /** Whether the user wants an email when a task reaches them. */
  emailOnNewTask: boolean
  /** The address those emails go to. */
  address: string
  /** Whether the server can send them at all. */
  available: boolean
}

export const notificationsApi = {
  get: () => request<NotificationSettings>('/notifications'),
  save: (emailOnNewTask: boolean) =>
    request<NotificationSettings>('/notifications', { method: 'PUT', body: JSON.stringify({ emailOnNewTask }) }),
}
