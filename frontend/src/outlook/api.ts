// The pane talks to Taskel with a personal API token, like the macro. A cookie session would not do: inside
// Outlook on the web the pane is a third-party frame, where browsers do not send Taskel's cookie.

import type { OutlookEmail } from './office'

const TOKEN_KEY = 'taskel.outlook.token'

export function savedToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function saveToken(token: string | null) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    // Storage blocked: the token then lasts until the pane closes.
  }
}

export class PaneError extends Error {
  readonly status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function call<T>(token: string, path: string, init: RequestInit = {}): Promise<T> {
  let res: Response
  try {
    res = await fetch(`/api${path}`, {
      ...init,
      credentials: 'omit',
      headers: { Accept: 'application/json', Authorization: `Bearer ${token}`, ...(init.headers ?? {}) },
    })
  } catch {
    throw new PaneError(0, 'Taskel nu răspunde. Verifică legătura la rețea.')
  }
  if (!res.ok) throw new PaneError(res.status, await problemText(res))
  return (res.status === 204 ? undefined : await res.json()) as T
}

async function problemText(res: Response): Promise<string> {
  if (res.status === 401) return 'Token-ul nu este valabil sau a fost revocat. Creează altul în Taskel, la Integrări.'
  try {
    const problem = await res.json()
    const messages = Object.values((problem.errors ?? {}) as Record<string, string[]>).flat()
    if (messages.length > 0) return messages.join(' ')
    if (typeof problem.title === 'string') return problem.title
  } catch {
    // Not JSON.
  }
  return `Taskel a răspuns cu o eroare (${res.status}).`
}

export interface Me {
  id: string
  email: string
  displayName: string
}

export interface Person {
  id: string
  email: string
  displayName: string
}

export interface Group {
  id: string
  name: string
  memberCount: number
}

export const paneApi = {
  me: (token: string) => call<Me>(token, '/auth/me'),
  people: (token: string) => call<Person[]>(token, '/users'),
  groups: (token: string) => call<Group[]>(token, '/groups'),
  limits: (token: string) =>
    call<{ maxFileSizeBytes: number; maxFilesPerTask: number }>(token, '/attachments/limits').catch(() => null),
  distribute: (token: string, email: OutlookEmail, recipients: string[], instructions: string) =>
    call<{ taskIds: string[] }>(token, '/tasks/from-email/distribute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        subject: email.subject,
        sender: email.sender,
        senderEmail: email.senderEmail,
        body: email.body,
        importance: email.importance,
        receivedAt: email.receivedAt,
        messageId: email.messageId,
        recipients,
        instructions: instructions || null,
      }),
    }),
  upload: (token: string, taskId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return call<unknown>(token, `/tasks/${taskId}/attachments`, { method: 'POST', body: form })
  },
}
