import { useEffect, useState, type FormEvent } from 'react'
import { tasksApi, type TaskItem } from './api/tasks'

export default function App() {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [title, setTitle] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    tasksApi
      .list()
      .then(setTasks)
      .catch(() => setError('Could not reach the Taskel API. Is the backend running?'))
      .finally(() => setLoading(false))
  }, [])

  async function handleAdd(e: FormEvent) {
    e.preventDefault()
    const trimmed = title.trim()
    if (!trimmed) return
    try {
      const created = await tasksApi.create(trimmed)
      setTasks((prev) => [...prev, created])
      setTitle('')
      setError(null)
    } catch {
      setError('Failed to add task.')
    }
  }

  async function toggle(task: TaskItem) {
    try {
      const updated = await tasksApi.update({ ...task, isCompleted: !task.isCompleted })
      setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
    } catch {
      setError('Failed to update task.')
    }
  }

  async function remove(id: string) {
    try {
      await tasksApi.remove(id)
      setTasks((prev) => prev.filter((t) => t.id !== id))
    } catch {
      setError('Failed to delete task.')
    }
  }

  return (
    <main className="container">
      <h1>Taskel</h1>

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
    </main>
  )
}
