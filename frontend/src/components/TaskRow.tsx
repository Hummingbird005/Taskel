import type { TaskItem } from '../api/tasks'
import { formatDueDate } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import Icon from './Icon'

interface Props {
  task: TaskItem
  today: string
  onToggle: () => void
  onEdit: () => void
  onDelete: () => void
  onTagClick: (tag: string) => void
}

export default function TaskRow({ task, today, onToggle, onEdit, onDelete, onTagClick }: Props) {
  const overdue = isOverdue(task, today)
  const dueLabel = task.dueDate === today ? 'Today' : task.dueDate ? formatDueDate(task.dueDate) : null

  return (
    <li className={`task${task.isCompleted ? ' done' : ''}`}>
      <input
        type="checkbox"
        className="task-check"
        checked={task.isCompleted}
        onChange={onToggle}
        aria-label={`${task.isCompleted ? 'Reopen' : 'Complete'} ${task.title}`}
      />
      <div className="task-body">
        <span className="task-title">{task.title}</span>
        <div className="task-meta">
          {task.priority !== 'medium' && (
            <span className={`chip priority-${task.priority}`}>
              <Icon name="flag" size={13} />
              {task.priority === 'high' ? 'High' : 'Low'} priority
            </span>
          )}
          {dueLabel && (
            <span className={`chip due${overdue ? ' overdue' : ''}`}>
              <Icon name="calendar" size={13} />
              {overdue ? 'Overdue: ' : 'Due '}
              {dueLabel}
            </span>
          )}
          {task.sourceEmailAddress && (
            <span className="chip" title={`From an email sent by ${task.sourceEmailAddress}`}>
              <Icon name="mail" size={13} />
              {task.sourceEmailAddress}
            </span>
          )}
          {task.sourceEmailAddress && !task.isCompleted && (
            <span className="chip reply" title={`Completing this task sends a reply to ${task.sourceEmailAddress}`}>
              <Icon name="reply" size={13} />
              Reply required
            </span>
          )}
          {task.tags.map((tag) => (
            <button
              key={tag}
              type="button"
              className="chip tag"
              onClick={() => onTagClick(tag)}
              title={`Show only #${tag}`}
            >
              #{tag}
            </button>
          ))}
        </div>
        {task.isCompleted && task.completionResponse && (
          <details className="task-details">
            <summary>
              {task.sourceEmailAddress ? `Reply sent to ${task.sourceEmailAddress}` : 'Completion note'}
            </summary>
            <p>{task.completionResponse}</p>
          </details>
        )}
        {task.description && (
          <details className="task-details">
            <summary>Notes</summary>
            <p>{task.description}</p>
          </details>
        )}
      </div>
      <div className="task-actions">
        <button type="button" onClick={onEdit} aria-label={`Edit ${task.title}`} title="Edit">
          <Icon name="edit" />
        </button>
        <button type="button" className="danger" onClick={onDelete} aria-label={`Delete ${task.title}`} title="Delete">
          <Icon name="trash" />
        </button>
      </div>
    </li>
  )
}
