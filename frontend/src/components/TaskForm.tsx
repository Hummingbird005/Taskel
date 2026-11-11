import { useState, type FormEvent, type KeyboardEvent } from 'react'
import type { TaskDraft, TaskPriority } from '../api/tasks'
import Icon from './Icon'

const EMPTY: TaskDraft = { title: '', dueDate: null, priority: 'medium', tags: [] }

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
}

export default function TaskForm({
  initial = EMPTY,
  placeholder = 'Add a task',
  submitLabel,
  onSubmit,
  onCancel,
}: Props) {
  const [title, setTitle] = useState(initial.title)
  const [dueDate, setDueDate] = useState(initial.dueDate ?? '')
  const [priority, setPriority] = useState<TaskPriority>(initial.priority)
  const [tags, setTags] = useState(initial.tags.join(', '))
  const [description, setDescription] = useState(initial.description ?? '')
  const [submitting, setSubmitting] = useState(false)

  const isEditing = Boolean(onCancel)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    const trimmed = title.trim()
    if (!trimmed || submitting) return
    setSubmitting(true)
    const saved = await onSubmit({
      title: trimmed,
      dueDate: dueDate || null,
      priority,
      tags: parseTags(tags),
      // Notes are only editable in edit mode; new tasks start without any.
      ...(isEditing ? { description: description.trim() || null } : {}),
    })
    setSubmitting(false)
    if (saved && !isEditing) {
      setTitle('')
      setDueDate(initial.dueDate ?? '')
      setPriority(initial.priority)
      setTags(initial.tags.join(', '))
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape' && onCancel) onCancel()
  }

  return (
    <form onSubmit={handleSubmit} onKeyDown={handleKeyDown} className={`composer${isEditing ? ' composer-edit' : ''}`}>
      <input
        className="composer-title"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        placeholder={placeholder}
        maxLength={200}
        aria-label="Task title"
        autoFocus={isEditing}
      />
      {isEditing && (
        <textarea
          className="composer-notes"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Notes"
          maxLength={2000}
          rows={4}
          aria-label="Notes"
        />
      )}
      <div className="composer-bar">
        <label className="composer-field">
          <Icon name="calendar" />
          <span className="visually-hidden">Due date</span>
          <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} aria-label="Due date" />
        </label>
        <label className="composer-field">
          <Icon name="flag" />
          <span className="visually-hidden">Priority</span>
          <select
            value={priority}
            onChange={(e) => setPriority(e.target.value as TaskPriority)}
            aria-label="Priority"
          >
            <option value="low">Low</option>
            <option value="medium">Medium</option>
            <option value="high">High</option>
          </select>
        </label>
        <label className="composer-field composer-tags">
          <Icon name="hash" />
          <span className="visually-hidden">Tags</span>
          <input
            value={tags}
            onChange={(e) => setTags(e.target.value)}
            placeholder="tags, comma separated"
            aria-label="Tags, comma separated"
          />
        </label>
        <div className="composer-actions">
          {onCancel && (
            <button type="button" className="button ghost" onClick={onCancel}>
              Cancel
            </button>
          )}
          <button type="submit" className="button send" disabled={!title.trim() || submitting}>
            {isEditing ? null : <Icon name="send" />}
            {submitLabel}
          </button>
        </div>
      </div>
    </form>
  )
}
