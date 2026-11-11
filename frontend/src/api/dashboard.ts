import { request } from './client'

export interface DashboardSettings {
  /** Ids of the visible widgets, in order. Null means the default layout. */
  widgets: string[] | null
  /** Show the overdue / due-today reminder when the app is opened. */
  showDueAlert: boolean
}

export const DEFAULT_DASHBOARD_SETTINGS: DashboardSettings = { widgets: null, showDueAlert: true }

export const dashboardApi = {
  get: () => request<DashboardSettings>('/dashboard'),
  save: (settings: DashboardSettings) =>
    request<DashboardSettings>('/dashboard', { method: 'PUT', body: JSON.stringify(settings) }),
}
