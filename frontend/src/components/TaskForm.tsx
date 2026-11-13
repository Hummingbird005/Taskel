import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { checkFiles, type AttachmentLimits } from '../api/attachments'
import type { GroupListItem } from '../api/groups'
import type { TaskDraft, TaskPriority } from '../api/tasks'
import { usersApi, type UserSummary } from '../api/users'
import { describePeriod } from '../lib/dates'
import { GROUP_PREFIX } from '../lib/taskViews'
import { formatBytes, tasksLabel } from '../lib/text'
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
import TargetPicker from './TargetPicker'
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
  /** Resolves to true when the task was saved. `files` are the ones picked to attach to it. */
  onSubmit: (draft: TaskDraft, files: File[]) => Promise<boolean>
  /**
   * With these, the form offers to attach files to the new task. Left out in edit mode, where
   * files are managed on the task itself.
   */
  attachmentLimits?: AttachmentLimits
  /** Called when the user gives up: closes the editor in the list, or the dialog. */
  onCancel?: () => void
  /**
   * Where the form is shown. `inline` (the default) is the compact editor in the task list;
   * `dialog` is the roomier layout of the new-task dialog, with a named field for each value
   * and a message.
   */
  variant?: 'inline' | 'dialog'
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
  attachmentLimits,
  variant = 'inline',
}: Props) {
  const inDialog = variant === 'dialog'
  const [files, setFiles] = useState<File[]>([])
  const fileInput = useRef<HTMLInputElement>(null)
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

  // Editing an existing task, as opposed to writing a new one.
  const isEditing = taskId !== undefined
  // The message can be written when adding from the dialog and when editing.
  const hasNotes = isEditing || inDialog

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
    if (!trimmed && !isEditing && !inDialog) return

    // The API applies the same rules; checking here gives a specific message.
    const parsedTags = parseTags(tags)
    const invalid = firstError(
      validateTitle(title),
      validateDueDate(dueDate),
      validateInterval(startDate, dueDate),
      validateTags(parsedTags),
      hasNotes ? validateNotes(description) : null,
    )
    if (invalid) {
      setFormError(invalid)
      return
    }
    setFormError(null)
    setSubmitting(true)
    const saved = await onSubmit(
    {
      title: trimmed,
      startDate: startDate || null,
      dueDate: dueDate || null,
      priority,
      tags: parsedTags,
      // Notes are only editable in edit mode; new tasks start without any.
      ...(hasNotes ? { description: description.trim() || null } : {}),
      // Only sent when the form offers the choice.
      ...(people.length > 0 ? { assigneeId: assigneeId || null, groupId } : {}),
    },
    files,
    )
    setSubmitting(false)
    if (saved && !isEditing) {
      setTitle('')
      setStartDate(initial.startDate ?? '')
      setDueDate(initial.dueDate ?? '')
      setPriority(initial.priority)
      setTags(initial.tags.join(', '))
      setTarget(initialTarget)
      setFiles([])
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    // In the dialog, Escape belongs to the dialog itself.
    if (e.key === 'Escape' && onCancel && !inDialog) onCancel()
  }

  const titleField = (
    <input
      className="composer-title"
      value={title}
      onChange={(e) => change(setTitle, e.target.value)}
      placeholder={placeholder}
      maxLength={LIMITS.title}
      aria-label="Titlul task-ului"
      autoFocus={isEditing || inDialog}
    />
  )
  const notesField = (
    <textarea
      className="composer-notes"
      value={description}
      onChange={(e) => change(setDescription, e.target.value)}
      placeholder={inDialog ? 'Detalii, context, pași de urmat (opțional)' : 'Note'}
      maxLength={LIMITS.notes}
      rows={inDialog ? 5 : 4}
      aria-label={inDialog ? 'Mesaj' : 'Note'}
    />
  )
  const startField = (
    <input
      type="date"
      value={startDate}
      min={LIMITS.dueDateMin}
      max={dueDate || LIMITS.dueDateMax}
      onChange={(e) => change(setStartDate, e.target.value)}
      aria-label="Început"
    />
  )
  const dueField = (
    <input
      type="date"
      value={dueDate}
      min={LIMITS.dueDateMin}
      max={LIMITS.dueDateMax}
      onChange={(e) => change(setDueDate, e.target.value)}
      aria-label="Termen"
    />
  )
  const priorityField = (
    <select value={priority} onChange={(e) => change(setPriority, e.target.value as TaskPriority)} aria-label="Prioritate">
      <option value="low">Scăzută</option>
      <option value="medium">Medie</option>
      <option value="high">Ridicată</option>
    </select>
  )
  const tagsField = (
    <input
      value={tags}
      onChange={(e) => change(setTags, e.target.value)}
      placeholder="etichete, separate prin virgulă"
      aria-label="Etichete, separate prin virgulă"
    />
  )
  const targetField = people.length > 0 && (
    <TargetPicker
      people={people}
      groups={groups}
      value={target}
      onChange={(next) => change(setTarget, next)}
      label="Alocat către"
      placeholder="Eu"
      emptyLabel="Eu"
    />
  )
  const fileList = files.length > 0 && (
    <ul className="composer-files">
      {files.map((file, index) => (
        <li key={`${file.name}-${index}`} className="chip">
          <Icon name="paperclip" size={13} />
          <span className="composer-file-name">{file.name}</span>
          <span className="muted">{formatBytes(file.size)}</span>
          <button
            type="button"
            onClick={() => change(setFiles, files.filter((_, i) => i !== index))}
            aria-label={`Renunță la fișierul ${file.name}`}
            title="Renunță la fișier"
          >
            <Icon name="close" size={13} />
          </button>
        </li>
      ))}
    </ul>
  )
  const filePicker = attachmentLimits && (
    <input
      ref={fileInput}
      type="file"
      multiple
      hidden
      onChange={(e) => {
        const next = [...files, ...(e.target.files ?? [])]
        e.target.value = ''
        const invalid = checkFiles(next, 0, attachmentLimits)
        if (invalid) setFormError(invalid)
        else change(setFiles, next)
      }}
    />
  )
  const messages = (
    <>
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
    </>
  )

  // The dialog: every field with its name above it, and room for a message.
  if (inDialog) {
    return (
      <form onSubmit={handleSubmit} onKeyDown={handleKeyDown} className="task-form" noValidate>
        <label className="field">
          <span>Titlu</span>
          {titleField}
        </label>
        <label className="field">
          <span>Mesaj</span>
          {notesField}
        </label>
        <div className="task-form-grid">
          <label className="field" title="Opțional: prima zi a intervalului în care se lucrează la task">
            <span>Început</span>
            {startField}
          </label>
          <label className="field" title="Ultima zi">
            <span>Termen</span>
            {dueField}
          </label>
          <label className="field">
            <span>Prioritate</span>
            {priorityField}
          </label>
          <label className="field">
            <span>Etichete</span>
            {tagsField}
          </label>
        </div>
        {targetField && (
          <div className="field">
            <span>Alocat către</span>
            {targetField}
          </div>
        )}
        {messages}
        {attachmentLimits && (
          <div className="field">
            <span>Fișiere</span>
            {fileList}
            {filePicker}
            <div className="task-form-attach">
              <button type="button" className="button ghost small" onClick={() => fileInput.current?.click()}>
                <Icon name="paperclip" />
                Atașează fișiere
              </button>
              <small className="muted">
                Cel mult {attachmentLimits.maxFileSizeMb} MB pe fișier, {attachmentLimits.maxFilesPerTask} fișiere.
              </small>
            </div>
          </div>
        )}
        <div className="task-form-actions">
          {onCancel && (
            <button type="button" className="button ghost" onClick={onCancel} disabled={submitting}>
              Anulează
            </button>
          )}
          <Button type="submit" variant="primary" loading={submitting}>
            <Icon name="plus" />
            {submitLabel}
          </Button>
        </div>
      </form>
    )
  }

  // In the list: a compact editor that replaces the task's row.
  return (
    <form
      onSubmit={handleSubmit}
      onKeyDown={handleKeyDown}
      className={`composer${isEditing ? ' composer-edit' : ''}`}
      // Validation is done in handleSubmit, so the messages are ours and in Romanian.
      noValidate
    >
      {titleField}
      {isEditing && notesField}
      {fileList}
      {messages}
      <div className="composer-bar">
        <label className="composer-field" title="Început (opțional): prima zi a intervalului în care se lucrează la task">
          <Icon name="calendar" />
          <span className="field-hint">De la</span>
          {startField}
        </label>
        <label className="composer-field" title="Termen: ultima zi">
          <span className="field-hint">până la</span>
          {dueField}
        </label>
        <label className="composer-field">
          <Icon name="flag" />
          <span className="visually-hidden">Prioritate</span>
          {priorityField}
        </label>
        <label className="composer-field composer-tags">
          <Icon name="hash" />
          <span className="visually-hidden">Etichete</span>
          {tagsField}
        </label>
        {targetField && <div className="composer-target">{targetField}</div>}
        {attachmentLimits && (
          <>
            {filePicker}
            <button
              type="button"
              className="composer-field composer-attach"
              onClick={() => fileInput.current?.click()}
              title={`Atașează fișiere (cel mult ${attachmentLimits.maxFileSizeMb} MB fiecare)`}
              aria-label="Atașează fișiere"
            >
              <Icon name="paperclip" />
              <span className="field-hint">Fișiere</span>
            </button>
          </>
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
