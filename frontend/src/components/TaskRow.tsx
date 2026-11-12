import type { TaskItem } from '../api/tasks'
import { formatDueDate, formatRange } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import Icon from './Icon'

interface Props {
  task: TaskItem
  /** Id of the signed-in user, to tell their own tasks from ones shared with them. */
  currentUserId: string
  today: string
  onToggle: () => void
  onEdit: () => void
  onDelete: () => void
  onTagClick: (tag: string) => void
}

export default function TaskRow({ task, currentUserId, today, onToggle, onEdit, onDelete, onTagClick }: Props) {
  const overdue = isOverdue(task, today)
  const isCreator = task.createdBy.id === currentUserId
  const hasInterval = task.startDate !== null && task.dueDate !== null && task.startDate !== task.dueDate
  const dueLabel = !task.dueDate
    ? null
    : hasInterval
      ? formatRange(task.startDate!, task.dueDate)
      : task.dueDate === today
        ? 'azi'
        : formatDueDate(task.dueDate)

  return (
    <li className={`task${task.isCompleted ? ' done' : ''}`}>
      <input
        type="checkbox"
        className="task-check"
        checked={task.isCompleted}
        onChange={onToggle}
        aria-label={`${task.isCompleted ? 'Redeschide' : 'Finalizează'}: ${task.title}`}
      />
      <div className="task-body">
        <span className="task-title">{task.title}</span>
        <div className="task-meta">
          {task.priority !== 'medium' && (
            <span className={`chip priority-${task.priority}`}>
              <Icon name="flag" size={13} />
              Prioritate {task.priority === 'high' ? 'ridicată' : 'scăzută'}
            </span>
          )}
          {dueLabel && (
            <span className={`chip due${overdue ? ' overdue' : ''}`}>
              <Icon name="calendar" size={13} />
              {overdue ? 'Întârziat: ' : hasInterval ? 'Interval: ' : 'Termen: '}
              {dueLabel}
            </span>
          )}
          {task.assignee && isCreator && (
            <span className="chip person" title={`Alocat către ${task.assignee.email}`}>
              <Icon name="send" size={13} />
              Alocat: {task.assignee.displayName}
            </span>
          )}
          {!isCreator && (
            <span className="chip person" title={`Primit de la ${task.createdBy.email}`}>
              <Icon name="reply" size={13} />
              De la: {task.createdBy.displayName}
            </span>
          )}
          {task.sourceEmailAddress && (
            <span className="chip" title={`Dintr-un email trimis de ${task.sourceEmailAddress}`}>
              <Icon name="mail" size={13} />
              {task.sourceEmailAddress}
            </span>
          )}
          {task.sourceEmailAddress && !task.isCompleted && (
            <span className="chip reply" title={`La finalizare se trimite un răspuns către ${task.sourceEmailAddress}`}>
              <Icon name="reply" size={13} />
              Necesită răspuns
            </span>
          )}
          {task.tags.map((tag) => (
            <button
              key={tag}
              type="button"
              className="chip tag"
              onClick={() => onTagClick(tag)}
              title={`Arată doar #${tag}`}
            >
              #{tag}
            </button>
          ))}
        </div>
        {task.isCompleted && task.completionResponse && (
          <details className="task-details">
            <summary>
              {task.sourceEmailAddress ? `Răspuns trimis către ${task.sourceEmailAddress}` : 'Notă de finalizare'}
            </summary>
            <p>{task.completionResponse}</p>
          </details>
        )}
        {task.description && (
          <details className="task-details">
            <summary>Note</summary>
            <p>{task.description}</p>
          </details>
        )}
      </div>
      <div className="task-actions">
        <button type="button" onClick={onEdit} aria-label={`Editează: ${task.title}`} title="Editează">
          <Icon name="edit" />
        </button>
        {isCreator && (
          <button type="button" className="danger" onClick={onDelete} aria-label={`Șterge: ${task.title}`} title="Șterge">
            <Icon name="trash" />
          </button>
        )}
      </div>
    </li>
  )
}
