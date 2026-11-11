import { request } from './client'

export interface ApiToken {
  id: string
  name: string
  createdAt: string
  lastUsedAt: string | null
}

/** Returned once, when the token is created. The token cannot be read again later. */
export interface CreatedApiToken extends Omit<ApiToken, 'lastUsedAt'> {
  token: string
}

export const tokensApi = {
  list: () => request<ApiToken[]>('/auth/tokens'),
  create: (name: string) =>
    request<CreatedApiToken>('/auth/tokens', { method: 'POST', body: JSON.stringify({ name }) }),
  revoke: (id: string) => request<void>(`/auth/tokens/${id}`, { method: 'DELETE' }),
}
