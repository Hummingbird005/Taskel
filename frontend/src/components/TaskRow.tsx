import type { TaskItem } from '../api/tasks'
import { formatDueDate, formatRange } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import Icon from './Icon'

interface Props {
  task: TaskItem
  today: string
  /** Opens the task, with its message, files and the buttons that change its state. */
  onOpen: () => void
  /** Takes a task that waits on one of the user's groups. Offered in the row, since it is the only thing to do. */
  onClaim?: () => void
  /** Also say who has the task now, for someone following a task they passed on. */
  showHolder?: boolean
}

/** The state of a task as a small symbol: an empty circle, a clock for on hold, a green tick for done. */
export function StateMark({ task }: { task: TaskItem }) {
  if (task.state === 'completed') {
    return (
      <span className="state-mark done" title="Finalizat">
        <Icon name="check" size={14} />
        <span className="visually-hidden">Finalizat: </span>
      </span>
    )
  }
  if (task.state === 'waiting') {
    return (
      <span className="state-mark waiting" title={`În așteptare: ${task.waitingReason ?? ''}`}>
        <Icon name="clock" size={16} />
        <span className="visually-hidden">În așteptare: </span>
      </span>
    )
  }
  if (task.access === 'group') {
    return (
      <span className="state-mark group" title="Așteaptă să fie preluat">
        <Icon name="users" size={15} />
        <span className="visually-hidden">De preluat: </span>
      </span>
    )
  }
  return (
    <span className="state-mark open" title="Deschis">
      <span className="visually-hidden">Deschis: </span>
    </span>
  )
}

/** When the task is due, in the row: an interval, "azi", a date, or nothing. */
function dueText(task: TaskItem, today: string): string | null {
  if (!task.dueDate) return null
  if (task.startDate && task.startDate !== task.dueDate) return formatRange(task.startDate, task.dueDate)
  return task.dueDate === today ? 'azi' : formatDueDate(task.dueDate)
}

/**
 * One task in a list: its title, and a line of what matters at a glance. The whole row opens the
 * task; its message and everything else are shown there.
 */
export default function TaskRow({ task, today, onOpen, onClaim, showHolder = false }: Props) {
  const overdue = isOverdue(task, today)
  const dueLabel = dueText(task, today)
  const isCreator = task.access === 'creator'
  // Who has it now, from the reader's point of view.
  const holder = task.assignee ?? (task.group ? null : task.createdBy)

  return (
    <li className={`task task-row state-${task.state}`}>
      <StateMark task={task} />
      <div className="task-body">
        <button type="button" className="task-open" onClick={onOpen}>
          {task.priorityLevel != null && !task.isCompleted && (
            <span className="level-badge" title={`Nivel de prioritate ${task.priorityLevel}: ordinea în zi`}>
              {task.priorityLevel}
            </span>
          )}
          {task.title}
        </button>
        <div className="task-meta">
          {task.state === 'waiting' && (
            <span className="chip waiting" title={task.waitingReason ?? undefined}>
              <Icon name="clock" size={13} />
              În așteptare
            </span>
          )}
          {task.priority === 'high' && (
            <span className="chip priority-high">
              <Icon name="flag" size={13} />
              Ridicată
            </span>
          )}
          {dueLabel && (
            <span className={`chip due${overdue ? ' overdue' : ''}`}>
              <Icon name="calendar" size={13} />
              {overdue ? 'Întârziat: ' : ''}
              {dueLabel}
            </span>
          )}
          {isCreator && task.assignee && (
            <span className="chip person" title={`Alocat către ${task.assignee.email}`}>
              <Icon name="send" size={13} />
              {task.assignee.displayName}
            </span>
          )}
          {task.group && !task.assignee && (
            <span className="chip person waiting" title={`Dat grupului ${task.group.name}`}>
              <Icon name="users" size={13} />
              {task.group.name}
            </span>
          )}
          {!isCreator && !showHolder && (
            <span className="chip person" title={`Primit de la ${task.createdBy.email}`}>
              <Icon name="reply" size={13} />
              {task.createdBy.displayName}
            </span>
          )}
          {showHolder && holder && (
            <span className="chip person" title={`Acum la ${holder.email}`}>
              <Icon name="user" size={13} />
              Acum la: {holder.displayName}
            </span>
          )}
          {task.attachments.length > 0 && (
            <span className="chip" title={task.attachments.map((file) => file.fileName).join(', ')}>
              <Icon name="paperclip" size={13} />
              {task.attachments.length}
              <span className="visually-hidden">{task.attachments.length === 1 ? ' fișier atașat' : ' fișiere atașate'}</span>
            </span>
          )}
          {task.sourceEmailAddress && task.state !== 'completed' && (
            <span className="chip reply" title={`La finalizare se trimite un răspuns către ${task.sourceEmailAddress}`}>
              <Icon name="mail" size={13} />
              Necesită răspuns
            </span>
          )}
        </div>
      </div>
      {onClaim && task.access === 'group' && (
        <button type="button" className="button send small task-claim" onClick={onClaim} aria-label={`Preia: ${task.title}`}>
          <Icon name="hand" />
          Preia
        </button>
      )}
      <Icon name="chevronRight" />
    </li>
  )
}
