import type { TaskItem } from '../api/tasks'
import { formatDueDate, formatRange } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import Icon from './Icon'
import { DEFAULT_ATTACHMENT_LIMITS, type Attachment } from '../api/attachments'
import TaskAttachments from './TaskAttachments'
import TaskTrail from './TaskTrail'

interface Props {
  task: TaskItem
  today: string
  /** Also say who has the task now, for someone following a task they passed on. */
  showHolder?: boolean
  /** Where this viewer downloads the task's files from, when not through the task itself. */
  attachmentUrl?: (attachment: Attachment) => string
}

/**
 * A task shown for reading only: as a manager sees the tasks of someone in their department,
 * or as someone sees a task they forwarded.
 */
export default function ReadOnlyTask({ task, today, showHolder = false, attachmentUrl }: Props) {
  const overdue = isOverdue(task, today)
  const hasInterval = task.startDate !== null && task.dueDate !== null && task.startDate !== task.dueDate
  const dueLabel = !task.dueDate
    ? null
    : hasInterval
      ? formatRange(task.startDate!, task.dueDate)
      : task.dueDate === today
        ? 'azi'
        : formatDueDate(task.dueDate)
  const sharedBy = task.assignee || task.group ? task.createdBy : null
  const holder = task.assignee ?? (task.group ? null : task.createdBy)

  return (
    <li className={`task readonly${task.isCompleted ? ' done' : ''}`}>
      <span className={`task-state${task.isCompleted ? ' done' : ''}`} aria-hidden="true">
        <Icon name={task.isCompleted ? 'checkCircle' : 'inbox'} />
      </span>
      <div className="task-body">
        <span className="task-title">
          <span className="visually-hidden">{task.isCompleted ? 'Finalizat: ' : 'Deschis: '}</span>
          {task.title}
        </span>
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
          {sharedBy && (
            <span className="chip person" title={`Primit de la ${sharedBy.email}`}>
              <Icon name="reply" size={13} />
              De la: {sharedBy.displayName}
            </span>
          )}
          {showHolder && holder && (
            <span className="chip person" title={`Acum la ${holder.email}`}>
              <Icon name="user" size={13} />
              Acum la: {holder.displayName}
            </span>
          )}
          {task.group && (
            <span className={`chip person${task.assignee ? '' : ' waiting'}`} title={`Dat grupului ${task.group.name}`}>
              <Icon name="users" size={13} />
              Grup: {task.group.name}
              {!task.assignee && ' · nepreluat'}
            </span>
          )}
          {task.attachments.length > 0 && (
            <span className="chip" title={task.attachments.map((file) => file.fileName).join(', ')}>
              <Icon name="paperclip" size={13} />
              {task.attachments.length}
              <span className="visually-hidden">{task.attachments.length === 1 ? ' fișier atașat' : ' fișiere atașate'}</span>
            </span>
          )}
          {task.sourceEmailAddress && (
            <span className="chip" title={`Dintr-un email trimis de ${task.sourceEmailAddress}`}>
              <Icon name="mail" size={13} />
              {task.sourceEmailAddress}
            </span>
          )}
          {task.tags.map((tag) => (
            <span key={tag} className="chip tag-static">
              #{tag}
            </span>
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
        <TaskAttachments task={task} limits={DEFAULT_ATTACHMENT_LIMITS} downloadUrl={attachmentUrl} />
        <TaskTrail task={task} />
      </div>
    </li>
  )
}
