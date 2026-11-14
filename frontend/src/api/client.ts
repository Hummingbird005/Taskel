export const API_BASE = `${import.meta.env.VITE_API_BASE_URL ?? ''}/api`

export class ApiError extends Error {
  readonly status: number
  /** The server's explanation of a failure, when it gave one (e.g. the mail server's answer). */
  readonly detail: string | null
  /** Validation errors by field name, as the server reports them (PascalCase names). */
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, detail: string | null = null, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.detail = detail
    this.fieldErrors = fieldErrors
  }
}

/** Calls the Taskel API. The session cookie is sent automatically. */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (!res.ok) {
    throw await apiError(res)
  }
  return (res.status === 204 ? undefined : await res.json()) as T
}

/** Builds the error from an ASP.NET Core problem-details response. */
async function apiError(res: Response): Promise<ApiError> {
  try {
    const problem = await res.json()
    const detail = typeof problem.detail === 'string' ? problem.detail : null
    const fieldErrors: Record<string, string[]> = problem.errors ?? {}
    const messages = Object.values(fieldErrors).flat()
    if (messages.length > 0) return new ApiError(res.status, messages.join(' '), detail, fieldErrors)
    if (typeof problem.title === 'string') return new ApiError(res.status, problem.title, detail)
  } catch {
    // Not JSON; fall through to the generic message.
  }
  return new ApiError(res.status, `Request failed (${res.status})`)
}
