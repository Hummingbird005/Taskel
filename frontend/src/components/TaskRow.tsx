import type { TaskItem } from '../api/tasks'

/** Today's date as YYYY-MM-DD in the user's own time zone. */
function todayIso(): string {
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Formats YYYY-MM-DD for display without shifting it across time zones. */
function formatDueDate(iso: string): string {
  const [year, month, day] = iso.split('-').map(Number)
  const date = new Date(year, month - 1, day)
  const sameYear = year === new Date().getFullYear()
  return date.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: sameYear ? undefined : 'numeric',
  })
}

interface Props {
  task: TaskItem
  onToggle: () => void
  onEdit: () => void
  onDelete: () => void
  onTagClick: (tag: string) => void
}

export default function TaskRow({ task, onToggle, onEdit, onDelete, onTagClick }: Props) {
  const overdue = !task.isCompleted && task.dueDate !== null && task.dueDate < todayIso()

  return (
    <li className={task.isCompleted ? 'done' : ''}>
      <div className="task-body">
        <label>
          <input type="checkbox" checked={task.isCompleted} onChange={onToggle} />
          <span className="task-title">{task.title}</span>
        </label>
        <div className="task-meta">
          {task.priority !== 'medium' && (
            <span className={`badge priority-${task.priority}`}>
              {task.priority === 'high' ? 'High' : 'Low'} priority
            </span>
          )}
          {task.dueDate && (
            <span className={`badge due${overdue ? ' overdue' : ''}`}>
              {overdue ? 'Overdue: ' : 'Due '}
              {formatDueDate(task.dueDate)}
            </span>
          )}
          {task.tags.map((tag) => (
            <button
              key={tag}
              type="button"
              className="badge tag"
              onClick={() => onTagClick(tag)}
              title={`Show only "${tag}"`}
            >
              #{tag}
            </button>
          ))}
        </div>
      </div>
      <div className="task-actions">
        <button type="button" className="icon" onClick={onEdit} aria-label={`Edit ${task.title}`}>
          ✎
        </button>
        <button type="button" className="icon delete" onClick={onDelete} aria-label={`Delete ${task.title}`}>
          ✕
        </button>
      </div>
    </li>
  )
}
