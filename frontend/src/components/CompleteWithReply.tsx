import { useState, type FormEvent } from 'react'
import Icon from './Icon'

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
    <form onSubmit={handleSubmit} className="reply-form">
      <h3>Complete “{title}”</h3>
      <p className="muted">
        This task came from an email. Your response is sent to <strong>{recipient}</strong> as a reply to
        that email, and the task is marked as completed once it is sent.
      </p>
      <textarea
        value={response}
        onChange={(e) => setResponse(e.target.value)}
        placeholder="Write your response"
        maxLength={4000}
        rows={5}
        aria-label={`Response to ${recipient}`}
        autoFocus
        required
      />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="reply-form-actions">
        <button type="submit" className="button send" disabled={!response.trim() || sending}>
          <Icon name="send" />
          {sending ? 'Sending…' : 'Send reply and complete'}
        </button>
        <button type="button" className="button ghost" onClick={onCancel} disabled={sending}>
          Cancel
        </button>
      </div>
    </form>
  )
}
