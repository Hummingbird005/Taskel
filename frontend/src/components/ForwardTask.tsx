import { useEffect, useState, type FormEvent } from 'react'
import type { GroupListItem } from '../api/groups'
import type { TaskItem, TaskTarget } from '../api/tasks'
import { usersApi, type UserSummary } from '../api/users'
import { describePeriod } from '../lib/dates'
import { GROUP_PREFIX } from '../lib/taskViews'
import { tasksLabel } from '../lib/text'
import { LIMITS, validateNotes } from '../lib/validation'
import Icon from './Icon'
import Button from './ui/Button'
import TargetPicker from './TargetPicker'

interface Props {
  task: TaskItem
  people: UserSummary[]
  groups: GroupListItem[]
  /** Forwards the task. Resolves to an error message, or null on success. */
  onSubmit: (target: TaskTarget, note: string | null) => Promise<string | null>
  onCancel: () => void
}

/** Shown when someone passes a task they received on to another person or to a group. */
export default function ForwardTask({ task, people, groups, onSubmit, onCancel }: Props) {
  const [target, setTarget] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [busy, setBusy] = useState<{ userId: string; count: number } | null>(null)

  const userId = target && !target.startsWith(GROUP_PREFIX) ? target : null

  // The same heads-up as when assigning: the person may already be loaded in the task's period.
  useEffect(() => {
    if (!userId || !task.dueDate) return
    let cancelled = false
    usersApi
      .workload(userId, task.startDate ?? task.dueDate, task.dueDate, task.id)
      .then((result) => {
        if (!cancelled) setBusy({ userId, count: result.count })
      })
      .catch(() => {
        // A convenience only: without an answer there is simply no warning.
      })
    return () => {
      cancelled = true
    }
  }, [userId, task.id, task.startDate, task.dueDate])

  const person = people.find((p) => p.id === userId)
  const busyWarning =
    person && task.dueDate && busy && busy.userId === userId && busy.count > 0
      ? `${person.displayName} mai are ${tasksLabel(busy.count)} în ${describePeriod(task.startDate, task.dueDate)}`
      : null

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!target || sending) return
    const invalid = validateNotes(note.trim(), LIMITS.forwardNote)
    if (invalid) {
      setError(invalid)
      return
    }
    setSending(true)
    setError(null)
    const failure = await onSubmit(
      target.startsWith(GROUP_PREFIX)
        ? { assigneeId: null, groupId: target.slice(GROUP_PREFIX.length) }
        : { assigneeId: target, groupId: null },
      note.trim() || null,
    )
    // On success the parent removes this form.
    if (failure) {
      setError(failure)
      setSending(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="reply-form" noValidate>
      <h3>Direcționează „{task.title}”</h3>
      <p className="muted">
        Task-ul trece la persoana sau grupul ales. Tu îl vei vedea în continuare la „Direcționate”, doar pentru
        citire, ca să urmărești ce se întâmplă cu el.
      </p>
      <div className="forward-target">
        <span>Către</span>
        <TargetPicker
          people={people}
          groups={groups}
          value={target}
          onChange={(next) => {
            setError(null)
            setTarget(next)
          }}
          label="Direcționează către"
          placeholder="Caută o persoană sau un grup"
          autoFocus
        />
      </div>
      <textarea
        value={note}
        onChange={(e) => {
          setError(null)
          setNote(e.target.value)
        }}
        placeholder="Mesaj pentru cine îl primește (opțional)"
        maxLength={LIMITS.forwardNote}
        rows={3}
        aria-label="Mesaj pentru cine primește task-ul"
      />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {busyWarning && !error && (
        <output className="composer-warning">
          <Icon name="alert" size={15} />
          {busyWarning}
        </output>
      )}
      <div className="reply-form-actions">
        <Button type="submit" variant="primary" disabled={!target} loading={sending}>
          <Icon name="forward" />
          Direcționează
        </Button>
        <button type="button" className="button ghost" onClick={onCancel} disabled={sending}>
          Anulează
        </button>
      </div>
    </form>
  )
}
