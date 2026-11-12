import { useEffect, useState, type FormEvent, type KeyboardEvent } from 'react'
import type { GroupListItem } from '../api/groups'
import type { TaskDraft, TaskPriority } from '../api/tasks'
import { usersApi, type UserSummary } from '../api/users'
import { describePeriod } from '../lib/dates'
import { GROUP_PREFIX } from '../lib/taskViews'
import { tasksLabel } from '../lib/text'
import {
  firstError,
  LIMITS,
  validateDueDate,
  validateInterval,
  validateNotes,
  validateTags,
  validateTitle,
} from '../lib/validation'
import Icon from './Icon'
import TargetOptions from './TargetOptions'
import Button from './ui/Button'

const EMPTY: TaskDraft = { title: '', startDate: null, dueDate: null, priority: 'medium', tags: [] }

/** Splits "work, Home ,work" into ["work", "home"], matching the API's normalization. */
function parseTags(input: string): string[] {
  const tags = input
    .split(',')
    .map((t) => t.trim().toLowerCase())
    .filter((t) => t.length > 0)
  return [...new Set(tags)]
}

interface Props {
  /** Starting values. In add mode the form returns to these after each task. */
  initial?: TaskDraft
  placeholder?: string
  submitLabel: string
  /** Resolves to true when the task was saved. */
  onSubmit: (draft: TaskDraft) => Promise<boolean>
  /** Present in edit mode, which also shows the notes field. */
  onCancel?: () => void
  /**
   * The other users the task can be assigned to. Leave out (or pass an empty list) to hide the
   * assignee field, e.g. when the person editing is not the task's creator.
   */
  people?: UserSummary[]
  /** The groups the task can be given to, offered next to the people. */
  groups?: GroupListItem[]
  /**
   * The signed-in user. With it, the form warns when the person the task is for already has
   * other tasks in the same date or period.
   */
  currentUser?: { id: string }
  /** The task being edited, so it is not counted against itself in that warning. */
  taskId?: string
}

/** The result of asking how loaded someone is, with the question it answers. */
interface Workload {
  key: string
  count: number
}

export default function TaskForm({
  initial = EMPTY,
  placeholder = 'Adaugă un task',
  submitLabel,
  onSubmit,
  onCancel,
  people = [],
  groups = [],
  currentUser,
  taskId,
}: Props) {
  const [title, setTitle] = useState(initial.title)
  const [startDate, setStartDate] = useState(initial.startDate ?? '')
  const [dueDate, setDueDate] = useState(initial.dueDate ?? '')
  const [priority, setPriority] = useState<TaskPriority>(initial.priority)
  const [tags, setTags] = useState(initial.tags.join(', '))
  const [description, setDescription] = useState(initial.description ?? '')
  // Who the task is for: '' for the user themselves, a user id, or a prefixed group id.
  const initialTarget = initial.assigneeId ?? (initial.groupId ? GROUP_PREFIX + initial.groupId : '')
  const [target, setTarget] = useState(initialTarget)
  const groupId = target.startsWith(GROUP_PREFIX) ? target.slice(GROUP_PREFIX.length) : null
  const assigneeId = groupId ? '' : target
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [workload, setWorkload] = useState<Workload | null>(null)

  // Who the task is for, and when: the question behind the "already has N tasks" warning.
  // A task given to a group is nobody's until someone takes it, so there is nobody to check.
  const responsibleId = groupId ? '' : assigneeId || currentUser?.id || ''
  const datesAreValid = dueDate !== '' && !validateDueDate(dueDate) && !validateInterval(startDate, dueDate)
  const workloadKey = responsibleId && datesAreValid ? `${responsibleId}|${startDate || dueDate}|${dueDate}` : null

  useEffect(() => {
    if (!workloadKey) return
    const [userId, from, to] = workloadKey.split('|')
    let cancelled = false
    // Wait for a pause in typing before asking.
    const timer = setTimeout(() => {
      usersApi
        .workload(userId, from, to, taskId)
        .then((result) => {
          if (!cancelled) setWorkload({ key: workloadKey, count: result.count })
        })
        .catch(() => {
          // The warning is a convenience; without an answer the form simply shows none.
        })
    }, 300)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [workloadKey, taskId])

  // Only an answer to the current question counts; an older one is ignored.
  const busyCount = workload && workload.key === workloadKey ? workload.count : 0
  const busyPerson = people.find((person) => person.id === assigneeId)
  // The period ends in an abbreviated month ("nov."), which already closes the sentence.
  const busyWarning =
    busyCount > 0 && dueDate
      ? `${busyPerson ? `${busyPerson.displayName} mai are` : 'Mai ai'} ${tasksLabel(busyCount)} în ${describePeriod(startDate || null, dueDate)}`
      : null

  const isEditing = Boolean(onCancel)

  /** Updates a field and drops the last validation message, which no longer describes the form. */
  function change<T>(setter: (value: T) => void, value: T) {
    setFormError(null)
    setter(value)
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    const trimmed = title.trim()
    if (submitting) return
    // An empty composer is not an error worth announcing; in edit mode an empty title is.
    if (!trimmed && !isEditing) return

    // The API applies the same rules; checking here gives a specific message.
    const parsedTags = parseTags(tags)
    const invalid = firstError(
      validateTitle(title),
      validateDueDate(dueDate),
      validateInterval(startDate, dueDate),
      validateTags(parsedTags),
      isEditing ? validateNotes(description) : null,
    )
    if (invalid) {
      setFormError(invalid)
      return
    }
    setFormError(null)
    setSubmitting(true)
    const saved = await onSubmit({
      title: trimmed,
      startDate: startDate || null,
      dueDate: dueDate || null,
      priority,
      tags: parsedTags,
      // Notes are only editable in edit mode; new tasks start without any.
      ...(isEditing ? { description: description.trim() || null } : {}),
      // Only sent when the form offers the choice.
      ...(people.length > 0 ? { assigneeId: assigneeId || null, groupId } : {}),
    })
    setSubmitting(false)
    if (saved && !isEditing) {
      setTitle('')
      setStartDate(initial.startDate ?? '')
      setDueDate(initial.dueDate ?? '')
      setPriority(initial.priority)
      setTags(initial.tags.join(', '))
      setTarget(initialTarget)
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape' && onCancel) onCancel()
  }

  return (
    <form
      onSubmit={handleSubmit}
      onKeyDown={handleKeyDown}
      className={`composer${isEditing ? ' composer-edit' : ''}`}
      // Validation is done in handleSubmit, so the messages are ours and in Romanian.
      noValidate
    >
      <input
        className="composer-title"
        value={title}
        onChange={(e) => change(setTitle, e.target.value)}
        placeholder={placeholder}
        maxLength={LIMITS.title}
        aria-label="Titlul task-ului"
        autoFocus={isEditing}
      />
      {isEditing && (
        <textarea
          className="composer-notes"
          value={description}
          onChange={(e) => change(setDescription, e.target.value)}
          placeholder="Note"
          maxLength={LIMITS.notes}
          rows={4}
          aria-label="Note"
        />
      )}
      {formError && (
        <p className="composer-error" role="alert">
          {formError}
        </p>
      )}
      {busyWarning && !formError && (
        <p className="composer-warning" role="status">
          <Icon name="alert" size={15} />
          {busyWarning}
        </p>
      )}
      <div className="composer-bar">
        <label className="composer-field" title="Început (opțional): prima zi a intervalului în care se lucrează la task">
          <Icon name="calendar" />
          <span className="field-hint">De la</span>
          <input
            type="date"
            value={startDate}
            min={LIMITS.dueDateMin}
            max={dueDate || LIMITS.dueDateMax}
            onChange={(e) => change(setStartDate, e.target.value)}
            aria-label="Început"
          />
        </label>
        <label className="composer-field" title="Termen: ultima zi">
          <span className="field-hint">până la</span>
          <input
            type="date"
            value={dueDate}
            min={LIMITS.dueDateMin}
            max={LIMITS.dueDateMax}
            onChange={(e) => change(setDueDate, e.target.value)}
            aria-label="Termen"
          />
        </label>
        <label className="composer-field">
          <Icon name="flag" />
          <span className="visually-hidden">Prioritate</span>
          <select
            value={priority}
            onChange={(e) => change(setPriority, e.target.value as TaskPriority)}
            aria-label="Prioritate"
          >
            <option value="low">Scăzută</option>
            <option value="medium">Medie</option>
            <option value="high">Ridicată</option>
          </select>
        </label>
        <label className="composer-field composer-tags">
          <Icon name="hash" />
          <span className="visually-hidden">Etichete</span>
          <input
            value={tags}
            onChange={(e) => change(setTags, e.target.value)}
            placeholder="etichete, separate prin virgulă"
            aria-label="Etichete, separate prin virgulă"
          />
        </label>
        {people.length > 0 && (
          <label className="composer-field">
            <Icon name="user" />
            <span className="visually-hidden">Alocat către</span>
            <select value={target} onChange={(e) => change(setTarget, e.target.value)} aria-label="Alocat către">
              <option value="">Eu</option>
              <TargetOptions people={people} groups={groups} />
            </select>
          </label>
        )}
        <div className="composer-actions">
          {onCancel && (
            <button type="button" className="button ghost" onClick={onCancel}>
              Anulează
            </button>
          )}
          <Button type="submit" variant="primary" disabled={!title.trim() && !isEditing} loading={submitting}>
            {isEditing ? null : <Icon name="send" />}
            {submitLabel}
          </Button>
        </div>
      </div>
    </form>
  )
}
