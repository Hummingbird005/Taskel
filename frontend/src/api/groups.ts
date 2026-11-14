import { request } from './client'
import type { UserSummary } from './users'

/** Where a group comes from: made in Taskel, or mirrored from Active Directory. */
export type GroupKind = 'app' | 'directory' | 'keycloak'

/** How a group's origin is named in the interface. */
export const GROUP_KIND_LABEL: Record<GroupKind, string> = {
  app: 'Aplicație',
  directory: 'Active Directory',
  keycloak: 'Keycloak',
}

/** A group as shown on a task. */
export interface GroupSummary {
  id: string
  name: string
  kind: GroupKind
}

/** A group in the list of groups a task can be given to. */
export interface GroupListItem extends GroupSummary {
  memberCount: number
  /** Whether the signed-in user may rename it, change its members or delete it. */
  canManage: boolean
}

export interface GroupDetail extends GroupSummary {
  canManage: boolean
  members: UserSummary[]
}

export const groupsApi = {
  list: () => request<GroupListItem[]>('/groups'),
  get: (id: string) => request<GroupDetail>(`/groups/${id}`),
  /** The people the signed-in manager or administrator may put in a group. */
  candidates: () => request<UserSummary[]>('/groups/candidates'),
  create: (name: string, memberIds: string[]) =>
    request<GroupDetail>('/groups', { method: 'POST', body: JSON.stringify({ name, memberIds }) }),
  update: (id: string, name: string, memberIds: string[]) =>
    request<GroupDetail>(`/groups/${id}`, { method: 'PUT', body: JSON.stringify({ name, memberIds }) }),
  remove: (id: string) => request<void>(`/groups/${id}`, { method: 'DELETE' }),
}
