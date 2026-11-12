import { useState, type FormEvent } from 'react'
import { validateNotes, LIMITS } from '../lib/validation'
import Icon from './Icon'
import Button from './ui/Button'

interface Props {
  title: string
  recipient: string
  /** Sends the reply and completes the task. Resolves to an error message, or null on success. */
  onSubmit: (response: string) => Promise<string | null>
  onCancel: () => void
}

/** Shown when an email task is checked off: the task is only completed once a reply is sent. */
export default function CompleteWithReply({ title, recipient, onSubmit, onCancel }: Props) {
  const [response, setResponse] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    const trimmed = response.trim()
    if (!trimmed) return
    const invalid = validateNotes(trimmed, LIMITS.response)
    if (invalid) {
      setError(invalid)
      return
    }
    setSending(true)
    setError(null)
    const failure = await onSubmit(trimmed)
    // On success the parent removes this form, so only failures need handling here.
    if (failure) {
      setError(failure)
      setSending(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="reply-form" noValidate>
      <h3>Finalizează „{title}”</h3>
      <p className="muted">
        Acest task vine dintr-un email. Răspunsul tău este trimis către <strong>{recipient}</strong>, ca reply la
        acel email, iar task-ul este marcat ca finalizat după ce răspunsul a fost trimis.
      </p>
      <textarea
        value={response}
        onChange={(e) => setResponse(e.target.value)}
        placeholder="Scrie răspunsul"
        maxLength={4000}
        rows={5}
        aria-label={`Răspuns către ${recipient}`}
        autoFocus
        required
      />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="reply-form-actions">
        <Button type="submit" variant="primary" disabled={!response.trim()} loading={sending}>
          <Icon name="send" />
          Trimite răspunsul și finalizează
        </Button>
        <button type="button" className="button ghost" onClick={onCancel} disabled={sending}>
          Anulează
        </button>
      </div>
    </form>
  )
}
