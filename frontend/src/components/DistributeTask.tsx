import { useState, type FormEvent } from 'react'
import type { GroupListItem } from '../api/groups'
import type { TaskItem } from '../api/tasks'
import type { UserSummary } from '../api/users'
import { GROUP_PREFIX } from '../lib/taskViews'
import { LIMITS, validateNotes } from '../lib/validation'
import Icon from './Icon'
import Button from './ui/Button'
import TargetPicker from './TargetPicker'

interface Props {
  task: TaskItem
  people: UserSummary[]
  groups: GroupListItem[]
  /** The signed-in user, who cannot distribute to themselves. */
  currentUserId?: string
  /** Distributes the task. Resolves to an error message, or null on success. */
  onSubmit: (recipients: string[], message: string | null, dueDate: string | null) => Promise<string | null>
  onCancel: () => void
}

const MAX_RECIPIENTS = 50

/**
 * Gives a copy of the task to several people or groups. The task stays with the person who distributes it, on
 * hold until everyone has answered; the answers come back into its conversation.
 */
export default function DistributeTask({ task, people, groups, currentUserId, onSubmit, onCancel }: Props) {
  const [chosen, setChosen] = useState<string[]>([])
  const [message, setMessage] = useState('')
  const [dueDate, setDueDate] = useState(task.dueDate ?? '')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)

  // People who already have an open copy, and the user, are not offered again.
  const taken = new Set(
    (task.distributions ?? [])
      .filter((d) => d.state !== 'completed')
      .map((d) => (d.assignee ? d.assignee.id : `${GROUP_PREFIX}${d.group?.id}`)),
  )
  const offeredPeople = people.filter((p) => p.id !== currentUserId && !taken.has(p.id) && !chosen.includes(p.id))
  const offeredGroups = groups.filter((g) => !taken.has(`${GROUP_PREFIX}${g.id}`) && !chosen.includes(`${GROUP_PREFIX}${g.id}`))

  function nameOf(value: string): string {
    if (value.startsWith(GROUP_PREFIX)) return groups.find((g) => `${GROUP_PREFIX}${g.id}` === value)?.name ?? 'Grup'
    return people.find((p) => p.id === value)?.displayName ?? 'Persoană'
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (chosen.length === 0 || sending) return
    const invalid = validateNotes(message.trim(), LIMITS.notes)
    if (invalid) {
      setError(invalid)
      return
    }
    setSending(true)
    const failure = await onSubmit(chosen, message.trim() || null, dueDate || null)
    setSending(false)
    if (failure) setError(failure)
  }

  return (
    <form onSubmit={handleSubmit} className="reply-form distribute-form" noValidate>
      <h3>Distribuie „{task.title}”</h3>
      <p className="muted">
        Fiecare persoană aleasă primește task-ul ei, cu mesajul tău și cu fișierele. Task-ul tău rămâne la tine, în
        așteptare, până răspund toți. Răspunsurile apar în conversația lui, iar tu îl închizi la final.
      </p>

      <div className="distribute-field">
        <span>Către</span>
        {chosen.length > 0 && (
          <ul className="recipient-chips">
            {chosen.map((value) => (
              <li key={value}>
                <Icon name={value.startsWith(GROUP_PREFIX) ? 'users' : 'user'} size={14} />
                {nameOf(value)}
                <button
                  type="button"
                  onClick={() => setChosen((prev) => prev.filter((v) => v !== value))}
                  aria-label={`Scoate ${nameOf(value)}`}
                >
                  <Icon name="close" size={12} />
                </button>
              </li>
            ))}
          </ul>
        )}
        <TargetPicker
          // Always empty: each choice becomes a chip, and the field is ready for the next one.
          key={chosen.length}
          people={offeredPeople}
          groups={offeredGroups}
          value=""
          onChange={(value) => {
            if (!value) return
            setError(null)
            setChosen((prev) => (prev.length >= MAX_RECIPIENTS || prev.includes(value) ? prev : [...prev, value]))
          }}
          label="Distribuie către"
          placeholder={chosen.length === 0 ? 'Caută o persoană sau un grup' : 'Adaugă încă una'}
          autoFocus
        />
      </div>

      <label className="field">
        <span>Mesaj pentru ei</span>
        <textarea
          value={message}
          onChange={(e) => {
            setError(null)
            setMessage(e.target.value)
          }}
          placeholder="Ce aștepți de la fiecare (opțional)"
          maxLength={LIMITS.notes}
          rows={3}
        />
      </label>

      <label className="field distribute-due">
        <span>Termen pentru ei</span>
        <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} min="2000-01-01" max="2100-12-31" />
      </label>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="reply-form-actions">
        <Button type="submit" variant="primary" loading={sending} disabled={chosen.length === 0}>
          <Icon name="users" />
          {chosen.length > 1 ? `Distribuie la ${chosen.length}` : 'Distribuie'}
        </Button>
        <button type="button" className="button ghost" onClick={onCancel} disabled={sending}>
          Anulează
        </button>
      </div>
    </form>
  )
}
