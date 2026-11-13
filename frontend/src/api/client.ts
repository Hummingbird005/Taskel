export const API_BASE = `${import.meta.env.VITE_API_BASE_URL ?? ''}/api`

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
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
    throw new ApiError(res.status, await errorMessage(res))
  }
  return (res.status === 204 ? undefined : await res.json()) as T
}

/** Extracts a readable message from an ASP.NET Core problem-details response. */
async function errorMessage(res: Response): Promise<string> {
  try {
    const problem = await res.json()
    const fieldErrors = Object.values(problem.errors ?? {}).flat()
    if (fieldErrors.length > 0) return fieldErrors.join(' ')
    if (typeof problem.title === 'string') return problem.title
  } catch {
    // Not JSON; fall through to the generic message.
  }
  return `Request failed (${res.status})`
}
