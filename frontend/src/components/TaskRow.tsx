import type { TaskItem } from '../api/tasks'
import { formatDueDate, formatRange } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import Icon from './Icon'
import TaskTrail from './TaskTrail'

interface Props {
  task: TaskItem
  today: string
  /** Takes a task that waits on one of the user's groups. */
  onClaim: () => void
  /** Opens the form that passes a received task on. */
  onForward: () => void
  /** Hands a task taken from a group back to it. */
  onRelease: () => void
  onToggle: () => void
  onEdit: () => void
  onDelete: () => void
  onTagClick: (tag: string) => void
}

/**
 * A task the user can act on. What is offered follows their access: the creator edits and
 * deletes; the assignee edits, forwards and (for a group task) hands back; a member of the
 * group a task waits on can only take it.
 */
export default function TaskRow({
  task,
  today,
  onToggle,
  onEdit,
  onDelete,
  onTagClick,
  onClaim,
  onForward,
  onRelease,
}: Props) {
  const overdue = isOverdue(task, today)
  const isCreator = task.access === 'creator'
  const waitsOnGroup = task.access === 'group'
  const lastForward = task.forwards.at(-1)
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
      {waitsOnGroup ? (
        <span className="task-state" aria-hidden="true">
          <Icon name="users" />
        </span>
      ) : (
        <input
          type="checkbox"
          className="task-check"
          checked={task.isCompleted}
          onChange={onToggle}
          aria-label={`${task.isCompleted ? 'Redeschide' : 'Finalizează'}: ${task.title}`}
        />
      )}
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
          {!isCreator && lastForward && (
            <span className="chip person" title={`Direcționat de ${lastForward.from.email}`}>
              <Icon name="forward" size={13} />
              Direcționat de: {lastForward.from.displayName}
            </span>
          )}
          {task.group && (
            <span className={`chip person${task.assignee ? '' : ' waiting'}`} title={`Dat grupului ${task.group.name}`}>
              <Icon name="users" size={13} />
              Grup: {task.group.name}
              {!task.assignee && ' · nepreluat'}
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
        <TaskTrail task={task} />
      </div>
      {waitsOnGroup && (
        <button type="button" className="button send task-claim" onClick={onClaim} aria-label={`Preia: ${task.title}`}>
          <Icon name="hand" />
          Preia
        </button>
      )}
      <div className="task-actions">
        {!waitsOnGroup && (
          <button type="button" onClick={onEdit} aria-label={`Editează: ${task.title}`} title="Editează">
            <Icon name="edit" />
          </button>
        )}
        {task.access === 'assignee' && !task.isCompleted && (
          <button type="button" onClick={onForward} aria-label={`Direcționează: ${task.title}`} title="Direcționează către altcineva">
            <Icon name="forward" />
          </button>
        )}
        {task.access === 'assignee' && task.group && !task.isCompleted && (
          <button
            type="button"
            onClick={onRelease}
            aria-label={`Dă înapoi grupului: ${task.title}`}
            title={`Dă înapoi grupului ${task.group.name}`}
          >
            <Icon name="users" />
          </button>
        )}
        {isCreator && (
          <button type="button" className="danger" onClick={onDelete} aria-label={`Șterge: ${task.title}`} title="Șterge">
            <Icon name="trash" />
          </button>
        )}
      </div>
    </li>
  )
}
