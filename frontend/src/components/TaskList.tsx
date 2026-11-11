import { useEffect, useMemo, useState } from 'react'
import { ApiError } from '../api/client'
import { tasksApi, type TaskDraft, type TaskItem, type TaskPriority } from '../api/tasks'
import TaskForm from './TaskForm'
import TaskRow from './TaskRow'

type SortBy = 'created' | 'dueDate' | 'priority'

const PRIORITY_RANK: Record<TaskPriority, number> = { high: 0, medium: 1, low: 2 }

/** Returns a sorted copy. Ties keep creation order; tasks without a due date go last. */
function sortTasks(tasks: TaskItem[], sortBy: SortBy): TaskItem[] {
  if (sortBy === 'created') return tasks
  return [...tasks].sort((a, b) =>
    sortBy === 'priority'
      ? PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority]
      : (a.dueDate ?? '9999-12-31').localeCompare(b.dueDate ?? '9999-12-31'),
  )
}

export default function TaskList({ onSessionExpired }: { onSessionExpired: () => void }) {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [sortBy, setSortBy] = useState<SortBy>('created')
  const [tagFilter, setTagFilter] = useState<string | null>(null)

  /** Shows the failure, or returns to the sign-in screen when the session is gone. */
  function handleFailure(err: unknown, fallback: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else setError(err instanceof ApiError && err.status === 400 ? err.message : fallback)
  }

  useEffect(() => {
    function load() {
      tasksApi
        .list()
        .then((loaded) => {
          setTasks(loaded)
          setError(null)
        })
        .catch((err) => {
          if (err instanceof ApiError && err.status === 401) onSessionExpired()
          else setError('Could not load tasks. Is the backend running?')
        })
        .finally(() => setLoading(false))
    }

    load()
    // Tasks can be added from outside this page (e.g. the Outlook macro),
    // so pick them up when the user comes back to it.
    window.addEventListener('focus', load)
    return () => window.removeEventListener('focus', load)
  }, [onSessionExpired])

  const visibleTasks = useMemo(() => {
    const filtered = tagFilter ? tasks.filter((t) => t.tags.includes(tagFilter)) : tasks
    return sortTasks(filtered, sortBy)
  }, [tasks, sortBy, tagFilter])

  async function add(draft: TaskDraft): Promise<boolean> {
    try {
      const created = await tasksApi.create(draft)
      setTasks((prev) => [...prev, created])
      setError(null)
      return true
    } catch (err) {
      handleFailure(err, 'Failed to add task.')
      return false
    }
  }

  async function save(task: TaskItem, changes: Partial<TaskItem>): Promise<boolean> {
    try {
      const updated = await tasksApi.update({ ...task, ...changes })
      setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
      setError(null)
      return true
    } catch (err) {
      handleFailure(err, 'Failed to update task.')
      return false
    }
  }

  async function remove(id: string) {
    try {
      await tasksApi.remove(id)
      setTasks((prev) => prev.filter((t) => t.id !== id))
    } catch (err) {
      handleFailure(err, 'Failed to delete task.')
    }
  }

  return (
    <>
      <TaskForm submitLabel="Add" onSubmit={add} />

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {tasks.length > 0 && (
        <div className="list-controls">
          <label>
            Sort by{' '}
            <select value={sortBy} onChange={(e) => setSortBy(e.target.value as SortBy)} aria-label="Sort tasks by">
              <option value="created">Created</option>
              <option value="dueDate">Due date</option>
              <option value="priority">Priority</option>
            </select>
          </label>
          {tagFilter && (
            <button type="button" className="badge tag active" onClick={() => setTagFilter(null)}>
              #{tagFilter} ✕
            </button>
          )}
        </div>
      )}

      {loading ? (
        <p>Loading…</p>
      ) : tasks.length === 0 ? (
        <p className="muted">No tasks yet.</p>
      ) : visibleTasks.length === 0 ? (
        <p className="muted">No tasks tagged #{tagFilter}.</p>
      ) : (
        <ul className="task-list">
          {visibleTasks.map((task) =>
            editingId === task.id ? (
              <li key={task.id} className="editing">
                <TaskForm
                  initial={task}
                  submitLabel="Save"
                  onCancel={() => setEditingId(null)}
                  onSubmit={async (draft) => {
                    const saved = await save(task, draft)
                    if (saved) setEditingId(null)
                    return saved
                  }}
                />
              </li>
            ) : (
              <TaskRow
                key={task.id}
                task={task}
                onToggle={() => save(task, { isCompleted: !task.isCompleted })}
                onEdit={() => setEditingId(task.id)}
                onDelete={() => remove(task.id)}
                onTagClick={setTagFilter}
              />
            ),
          )}
        </ul>
      )}
    </>
  )
}
