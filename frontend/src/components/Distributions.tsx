import { useState } from 'react'
import type { Distribution, TaskItem } from '../api/tasks'
import { formatDueDate, formatTimestamp } from '../lib/dates'
import { STATE_ICON } from '../lib/taskStates'
import { LIMITS, validateNotes } from '../lib/validation'
import Icon from './Icon'

const COPY_STATE: Record<Distribution['state'], string> = { open: 'Lucrează', waiting: 'În așteptare', completed: 'A răspuns' }

/** Who has a copy: the person, or the group while nobody in it took the task. */
function holder(copy: Distribution): string {
  return copy.assignee?.displayName ?? copy.group?.name ?? '—'
}

/**
 * The copies of a distributed task, for whoever distributed it: who has each, where it stands, their answer, and
 * a way to send an answer back when it is not enough.
 */
export function DistributionPanel({
  task,
  onSendBack,
}: {
  task: TaskItem
  /** Sends one answered copy back with a message; left out when the person cannot. */
  onSendBack?: (copyId: string, message: string) => Promise<string | null>
}) {
  const copies = task.distributions ?? []
  const [sendingBack, setSendingBack] = useState<string | null>(null)
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  if (copies.length === 0) return null
  const answered = copies.filter((c) => c.state === 'completed').length

  async function submit(copyId: string) {
    const message = text.trim()
    const invalid = message ? validateNotes(message, LIMITS.response) : 'Scrie ce lipsește din răspuns.'
    if (invalid || !onSendBack) {
      setError(invalid)
      return
    }
    setBusy(true)
    const failure = await onSendBack(copyId, message)
    setBusy(false)
    if (failure) setError(failure)
    else {
      setSendingBack(null)
      setText('')
    }
  }

  return (
    <section className="detail-section distributions" aria-label="Distribuit către">
      <h3>
        Distribuit către <span className="muted">· {answered} din {copies.length} au răspuns</span>
      </h3>
      <ul>
        {copies.map((copy) => (
          <li key={copy.taskId} className={`distribution ${copy.state}`}>
            <div className="distribution-head">
              <Icon name={copy.assignee ? 'user' : 'users'} size={15} />
              <strong>{holder(copy)}</strong>
              <span className={`state-pill small ${copy.state}`}>
                <Icon name={STATE_ICON[copy.state]} size={12} />
                {COPY_STATE[copy.state]}
              </span>
              {copy.state !== 'completed' && copy.dueDate && (
                <span className="muted distribution-due">termen {formatDueDate(copy.dueDate)}</span>
              )}
              {copy.state === 'completed' && copy.completedAt && (
                <span className="muted distribution-due">{formatTimestamp(copy.completedAt)}</span>
              )}
            </div>
            {copy.answer && <p className="distribution-answer">{copy.answer}</p>}
            {copy.state === 'completed' && !copy.answer && <p className="muted distribution-answer">Închis fără răspuns.</p>}
            {onSendBack && copy.state === 'completed' && copy.answer && sendingBack !== copy.taskId && (
              <button
                type="button"
                className="button ghost small"
                onClick={() => {
                  setSendingBack(copy.taskId)
                  setText('')
                  setError(null)
                }}
              >
                <Icon name="reply" />
                Revino
              </button>
            )}
            {sendingBack === copy.taskId && (
              <form
                className="send-back"
                onSubmit={(e) => {
                  e.preventDefault()
                  submit(copy.taskId)
                }}
                noValidate
              >
                <textarea
                  value={text}
                  onChange={(e) => {
                    setError(null)
                    setText(e.target.value)
                  }}
                  rows={2}
                  maxLength={LIMITS.response}
                  placeholder={`Ce mai trebuie? Task-ul se redeschide la ${holder(copy)}.`}
                  aria-label={`Mesaj pentru ${holder(copy)}`}
                  autoFocus
                />
                {error && (
                  <p className="error" role="alert">
                    {error}
                  </p>
                )}
                <div className="send-back-actions">
                  <button type="submit" className="button primary small" disabled={busy}>
                    {busy ? <span className="spinner" aria-hidden="true" /> : <Icon name="reply" />}
                    Trimite înapoi
                  </button>
                  <button type="button" className="button ghost small" onClick={() => setSendingBack(null)} disabled={busy}>
                    Anulează
                  </button>
                </div>
              </form>
            )}
          </li>
        ))}
      </ul>
    </section>
  )
}

/** On a copy: where it came from, and who the answer goes to. */
export function DistributedFrom({ task }: { task: TaskItem }) {
  if (!task.distributedFrom) return null
  return (
    <section className="detail-callout distributed-from" role="note">
      <h3>
        <Icon name="users" size={15} />
        Distribuit de {task.createdBy.displayName}
      </h3>
      <p>
        Din task-ul „{task.distributedFrom.title}”.{' '}
        {task.state === 'completed'
          ? 'Răspunsul tău a ajuns la ' + task.createdBy.displayName + '.'
          : `Când îl finalizezi, răspunsul tău ajunge la ${task.createdBy.displayName}.`}
      </p>
    </section>
  )
}
