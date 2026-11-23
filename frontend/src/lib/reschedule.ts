import { ApiError } from '../api/client'

/** The longest reason the API takes. */
export const REASON_MAX = 1000

/** What a failed move means for the manager, from the API's answer. */
export function rescheduleError(err: unknown): string {
  if (err instanceof ApiError && err.status === 400) return 'Termenul nu poate fi înaintea datei de început a task-ului.'
  if (err instanceof ApiError && err.status === 409)
    return 'În ziua aceea nu mai e loc la acest nivel: task-urile de sub el ar trece de nivelul 20. Alege alt nivel.'
  if (err instanceof ApiError && err.status === 404) return 'Task-ul nu mai este al cuiva din echipă.'
  return 'Nu am putut muta task-ul.'
}
