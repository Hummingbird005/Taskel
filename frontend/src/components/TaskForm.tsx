import { useState, type FormEvent } from 'react'
import type { TaskDraft, TaskPriority } from '../api/tasks'

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
  initial?: TaskDraft
  submitLabel: string
  /** Resolves to true when the task was saved; the form then resets (add mode). */
  onSubmit: (draft: TaskDraft) => Promise<boolean>
  onCancel?: () => void
}

export default function TaskForm({ initial = EMPTY, submitLabel, onSubmit, onCancel }: Props) {
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
    if (!trimmed) return
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
      setDueDate('')
      setPriority('medium')
      setTags('')
    }
  }

  return (
    <form onSubmit={handleSubmit} className="task-form">
      <div className="task-form-main">
        <input
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="What needs to be done?"
          maxLength={200}
          aria-label="Task title"
          autoFocus={isEditing}
        />
        <button type="submit" disabled={!title.trim() || submitting}>
          {submitLabel}
        </button>
        {onCancel && (
          <button type="button" className="secondary" onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
      <div className="task-form-details">
        <label>
          Due
          <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} aria-label="Due date" />
        </label>
        <label>
          Priority
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
        <label className="tags-field">
          Tags
          <input
            value={tags}
            onChange={(e) => setTags(e.target.value)}
            placeholder="work, home"
            aria-label="Tags, comma separated"
          />
        </label>
      </div>
      {isEditing && (
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Notes"
          maxLength={2000}
          rows={4}
          aria-label="Notes"
        />
      )}
    </form>
  )
}
