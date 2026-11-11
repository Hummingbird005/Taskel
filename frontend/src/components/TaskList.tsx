import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import { tasksApi, type TaskItem } from '../api/tasks'

export default function TaskList({ onSessionExpired }: { onSessionExpired: () => void }) {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [title, setTitle] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  /** Shows the failure, or returns to the sign-in screen when the session is gone. */
  function handleFailure(err: unknown, message: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else setError(message)
  }

  useEffect(() => {
    tasksApi
      .list()
      .then(setTasks)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Could not load tasks. Is the backend running?')
      })
      .finally(() => setLoading(false))
  }, [onSessionExpired])

  async function handleAdd(e: FormEvent) {
    e.preventDefault()
    const trimmed = title.trim()
    if (!trimmed) return
    try {
      const created = await tasksApi.create(trimmed)
      setTasks((prev) => [...prev, created])
      setTitle('')
      setError(null)
    } catch (err) {
      handleFailure(err, 'Failed to add task.')
    }
  }

  async function toggle(task: TaskItem) {
    try {
      const updated = await tasksApi.update({ ...task, isCompleted: !task.isCompleted })
      setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
    } catch (err) {
      handleFailure(err, 'Failed to update task.')
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
      <form onSubmit={handleAdd} className="add-form">
        <input
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="What needs to be done?"
          maxLength={200}
          aria-label="New task title"
        />
        <button type="submit" disabled={!title.trim()}>
          Add
        </button>
      </form>

      {error && <p className="error">{error}</p>}
      {loading ? (
        <p>Loading…</p>
      ) : tasks.length === 0 ? (
        <p className="muted">No tasks yet.</p>
      ) : (
        <ul className="task-list">
          {tasks.map((task) => (
            <li key={task.id} className={task.isCompleted ? 'done' : ''}>
              <label>
                <input type="checkbox" checked={task.isCompleted} onChange={() => toggle(task)} />
                <span>{task.title}</span>
              </label>
              <button className="delete" onClick={() => remove(task.id)} aria-label={`Delete ${task.title}`}>
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
