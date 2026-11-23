import { useState, type FormEvent } from 'react'
import type { TaskItem } from '../api/tasks'
import type { Reschedule } from '../api/team'
import { formatLongDate } from '../lib/dates'
import { REASON_MAX } from '../lib/reschedule'
import { LIMITS, validateDueDate, validateNotes } from '../lib/validation'
import Icon from './Icon'
import Button from './ui/Button'

const LEVELS = Array.from({ length: 20 }, (_, i) => i + 1)

const dayText = (iso: string | null) => (iso ? formatLongDate(iso) : 'fără termen')
const levelText = (level: number | null) => (level == null ? 'fără nivel' : `nivelul ${level}`)

interface Props {
  task: TaskItem
  /** Where the task is going, e.g. where a card was dropped; the task's own day and level otherwise. */
  dueDate?: string | null
  priorityLevel?: number | null
  /** Moves the task. Resolves to an error message, or null on success. */
  onSubmit: (change: Reschedule) => Promise<string | null>
  onCancel?: () => void
  /** Focus the reason, for a form opened by dropping a card: the place is already chosen. */
  focusReason?: boolean
}

/**
 * For a manager: a task's day and level, and the reason for changing them, which is required. The reason stays in
 * the task's history and is emailed to the person responsible, with what changed.
 */
export default function RescheduleForm({ task, dueDate, priorityLevel, onSubmit, onCancel, focusReason = false }: Props) {
  const [due, setDue] = useState((dueDate === undefined ? task.dueDate : dueDate) ?? '')
  const [level, setLevel] = useState(String((priorityLevel === undefined ? task.priorityLevel : priorityLevel) ?? ''))
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  const newDue = due || null
  const newLevel = level ? Number(level) : null
  const changes = [
    newDue !== task.dueDate && `Termen: ${dayText(task.dueDate)} → ${dayText(newDue)}`,
    newLevel !== task.priorityLevel && `Nivel: ${levelText(task.priorityLevel)} → ${levelText(newLevel)}`,
  ].filter((c): c is string => Boolean(c))

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (busy || changes.length === 0) return
    const text = reason.trim()
    const invalid = !text
      ? 'Scrie motivul: îl primește persoana responsabilă și rămâne în istoricul task-ului.'
      : (validateDueDate(due) ?? validateNotes(text, REASON_MAX))
    if (invalid) {
      setError(invalid)
      return
    }
    setBusy(true)
    setError(null)
    const failure = await onSubmit({ dueDate: newDue, priorityLevel: newLevel, reason: text })
    setBusy(false)
    if (failure) setError(failure)
    else {
      setSaved(true)
      setReason('')
    }
  }

  const edit = <T,>(set: (value: T) => void) => (value: T) => {
    set(value)
    setError(null)
    setSaved(false)
  }

  return (
    <form className="reschedule-form" onSubmit={handleSubmit} noValidate>
      <div className="reschedule-fields">
        <label className="field">
          <span>Termen</span>
          <input
            type="date"
            value={due}
            min={task.startDate ?? LIMITS.dueDateMin}
            max={LIMITS.dueDateMax}
            onChange={(e) => edit(setDue)(e.target.value)}
            aria-label="Termen nou"
          />
        </label>
        <label className="field level-field">
          <span>Nivel</span>
          <select value={level} onChange={(e) => edit(setLevel)(e.target.value)} aria-label="Nivel nou">
            <option value="">—</option>
            {LEVELS.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        {due && (
          <button type="button" className="button ghost small" onClick={() => edit(setDue)('')} disabled={busy}>
            Fără termen
          </button>
        )}
      </div>
      {changes.length > 0 && (
        <ul className="reschedule-changes">
          {changes.map((change) => (
            <li key={change}>{change}</li>
          ))}
        </ul>
      )}
      <label className="field">
        <span>Motiv (obligatoriu)</span>
        <textarea
          value={reason}
          onChange={(e) => edit(setReason)(e.target.value)}
          placeholder="De ce se mută task-ul? Persoana responsabilă primește motivul pe email."
          maxLength={REASON_MAX}
          rows={3}
          required
          aria-label="Motivul mutării"
          // A form opened by dropping a card already has its place: what is left is the reason.
          autoFocus={focusReason}
          disabled={changes.length === 0 && !busy}
        />
      </label>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {saved && changes.length === 0 && <p className="muted">Task mutat. Persoana responsabilă a primit un email.</p>}
      {!error && !saved && changes.length === 0 && (
        <p className="muted">Alege alt termen sau alt nivel. Un nivel ocupat le mută pe celelalte cu o poziție mai jos.</p>
      )}
      <div className="reschedule-actions">
        <Button type="submit" variant="primary" small disabled={changes.length === 0} loading={busy}>
          <Icon name="check" />
          Mută task-ul
        </Button>
        {onCancel && (
          <button type="button" className="button ghost small" onClick={onCancel} disabled={busy}>
            Anulează
          </button>
        )}
      </div>
    </form>
  )
}
