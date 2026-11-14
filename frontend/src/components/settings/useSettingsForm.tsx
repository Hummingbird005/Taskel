import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { ApiError } from '../../api/client'
import type { IntegrationStatus } from '../../api/integrations'

/** Validation messages by form field. */
export type FieldErrors<F> = Partial<Record<keyof F, string>>

/** The outcome of a save, test or import, shown under the form. */
export interface Outcome {
  tone: 'ok' | 'error'
  text: ReactNode
}

/** A failed request as a sentence for the administrator, with the server's own explanation when it gave one. */
export function failure(err: unknown, lead: string): Outcome {
  if (!(err instanceof ApiError)) return { tone: 'error', text: `${lead} Nu am putut contacta serverul Taskel.` }
  const detail = err.detail ?? (err.status === 400 || err.status === 502 ? err.message : null)
  return {
    tone: 'error',
    text: (
      <>
        {lead}
        {detail && (
          <>
            {' '}
            <span className="outcome-detail">Detalii: {detail}</span>
          </>
        )}
      </>
    ),
  }
}

/**
 * Loads an integration's settings into an editable form, and keeps the two in step after a save.
 * `toForm` turns what the server sent into the form's values.
 */
export function useSettingsForm<S extends { status: IntegrationStatus }, F>(
  load: () => Promise<S>,
  toForm: (settings: S) => F,
  onSessionExpired: () => void,
) {
  const [settings, setSettings] = useState<S | null>(null)
  const [form, setForm] = useState<F | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [outcome, setOutcome] = useState<Outcome | null>(null)
  const [errors, setErrors] = useState<FieldErrors<F>>({})

  const accept = useCallback(
    (loaded: S) => {
      setSettings(loaded)
      setForm(toForm(loaded))
      setErrors({})
    },
    [toForm],
  )

  useEffect(() => {
    let cancelled = false
    load()
      .then((loaded) => {
        if (!cancelled) accept(loaded)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setLoadError(true)
      })
    return () => {
      cancelled = true
    }
  }, [load, accept, onSessionExpired])

  function update<K extends keyof F>(key: K, value: F[K]) {
    setForm((prev) => (prev ? { ...prev, [key]: value } : prev))
    setErrors((prev) => ({ ...prev, [key]: undefined }))
    setOutcome(null)
  }

  /** Runs one action at a time; `name` tells which button shows the spinner. */
  function run(name: string, action: () => Promise<Outcome | null>, failureLead: string) {
    if (busy) return
    setBusy(name)
    setOutcome(null)
    action()
      .then(setOutcome)
      .catch((err: unknown) => {
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setOutcome(failure(err, failureLead))
      })
      .finally(() => setBusy(null))
  }

  return { settings, form, loadError, busy, outcome, errors, setErrors, update, run, accept }
}
