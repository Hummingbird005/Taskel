import type { TaskItem, TaskPriority, TaskState } from '../api/tasks'
import { formatDueDate, formatRange, formatTimestamp } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import { LIMITS } from '../lib/validation'
import { STATE_ICON } from '../lib/taskStates'
import Icon from './Icon'

/** The pieces of the task dialog, each one small enough to read on its own. */

const PRIORITY_LABEL: Record<TaskPriority, string> = { high: 'Ridicată', medium: 'Medie', low: 'Scăzută' }

/** When the task is due: an interval, "azi", or a date. */
function dueText(task: TaskItem, today: string): string {
  if (task.startDate && task.dueDate && task.startDate !== task.dueDate) return formatRange(task.startDate, task.dueDate)
  if (task.dueDate === today) return 'azi'
  return task.dueDate ? formatDueDate(task.dueDate) : ''
}

function TagList({ tags, onTagClick }: { tags: string[]; onTagClick?: (tag: string) => void }) {
  return (
    <dd className="detail-tags">
      {tags.map((tag) =>
        onTagClick ? (
          <button key={tag} type="button" className="chip tag" onClick={() => onTagClick(tag)} title={`Arată doar #${tag}`}>
            #{tag}
          </button>
        ) : (
          <span key={tag} className="chip tag-static">
            #{tag}
          </span>
        ),
      )}
    </dd>
  )
}

/** Due date, priority, who made it and who has it, where it came from, its tags. */
export function DetailFacts({ task, today, onTagClick }: { task: TaskItem; today: string; onTagClick?: (tag: string) => void }) {
  const overdue = isOverdue(task, today)
  const hasInterval = task.startDate !== null && task.dueDate !== null && task.startDate !== task.dueDate
  return (
    <dl className="detail-facts">
      {task.dueDate && (
        <div>
          <dt>{hasInterval ? 'Interval' : 'Termen'}</dt>
          <dd className={overdue ? 'overdue' : undefined}>
            {dueText(task, today)}
            {overdue && ' · întârziat'}
          </dd>
        </div>
      )}
      <div>
        <dt>Prioritate</dt>
        <dd className={task.priority === 'high' ? 'overdue' : undefined}>{PRIORITY_LABEL[task.priority]}</dd>
      </div>
      <div>
        <dt>Creat de</dt>
        <dd>{task.createdBy.displayName}</dd>
      </div>
      {(task.assignee || task.group) && (
        <div>
          <dt>{task.assignee ? 'Alocat către' : 'Grup'}</dt>
          <dd>
            {task.assignee?.displayName ?? task.group?.name}
            {task.assignee && task.group && <span className="muted"> · din grupul {task.group.name}</span>}
          </dd>
        </div>
      )}
      {task.sourceEmailAddress && (
        <div>
          <dt>Din emailul lui</dt>
          <dd>{task.sourceEmailAddress}</dd>
        </div>
      )}
      {task.tags.length > 0 && (
        <div>
          <dt>Etichete</dt>
          <TagList tags={task.tags} onTagClick={onTagClick} />
        </div>
      )}
    </dl>
  )
}

/** Why the task is on hold, or how it was resolved. Nothing for an open task. */
export function StateCallout({ task }: { task: TaskItem }) {
  if (task.state === 'waiting') {
    return (
      <section className="detail-callout waiting" role="note">
        <h3>
          <Icon name="clock" size={15} />
          În așteptare{task.waitingSince && ` din ${formatTimestamp(task.waitingSince)}`}
        </h3>
        <p>{task.waitingReason}</p>
      </section>
    )
  }
  if (task.state !== 'completed') return null
  return (
    <section className="detail-callout done" role="note">
      <h3>
        <Icon name="check" size={15} />
        Finalizat{task.completedAt && ` pe ${formatTimestamp(task.completedAt)}`}
      </h3>
      {task.completionResponse ? (
        <>
          {task.sourceEmailAddress && <p className="muted">Răspuns trimis către {task.sourceEmailAddress}:</p>}
          <p>{task.completionResponse}</p>
        </>
      ) : (
        <p className="muted">Fără mesaj de rezolvare.</p>
      )}
    </section>
  )
}

/** The label of each state button, which depends on where the task stands now. */
function buttonLabels(task: TaskItem): Record<TaskState, string> {
  const labels: Record<TaskState, string> = {
    open: 'Deschis',
    waiting: 'În așteptare',
    completed: task.sourceEmailAddress ? 'Trimite și finalizează' : 'Finalizat',
  }
  if (task.state === 'completed') labels.open = 'Redeschide'
  if (task.state === 'waiting') {
    labels.open = 'Reia lucrul'
    labels.waiting = 'Schimbă motivul'
  }
  return labels
}

/**
 * Where the person working on the task writes how it was resolved (or why it waits, or the reply
 * to the email it came from) and changes its state with the coloured buttons.
 */
export function WorkFooter({
  task,
  message,
  busy,
  onMessage,
  onState,
}: {
  task: TaskItem
  message: string
  /** The action under way, if any: its button shows a spinner and the others wait. */
  busy: string | null
  onMessage: (message: string) => void
  onState: (state: TaskState) => void
}) {
  const reply = task.sourceEmailAddress
  const labels = buttonLabels(task)
  const states: TaskState[] = task.state === 'completed' ? ['open'] : ['open', 'waiting', 'completed']
  return (
    <form className="detail-footer" onSubmit={(e) => e.preventDefault()} noValidate>
      {task.state !== 'completed' && (
        <label className="field">
          <span>{reply ? `Răspuns către ${reply}` : 'Mesaj de rezolvare'}</span>
          <textarea
            value={message}
            onChange={(e) => onMessage(e.target.value)}
            rows={3}
            maxLength={LIMITS.response}
            placeholder={
              reply
                ? 'Obligatoriu la finalizare: se trimite pe email ca răspuns.'
                : 'Ce s-a rezolvat, sau ce aștepți (obligatoriu pentru „În așteptare”).'
            }
          />
        </label>
      )}
      <fieldset className="state-bar">
        <legend className="visually-hidden">Starea task-ului</legend>
        {states.map((state) => (
          <button
            key={state}
            type="button"
            className={`state-button ${state}`}
            aria-pressed={task.state === state}
            disabled={busy !== null || (state === 'open' && task.state === 'open')}
            onClick={() => onState(state)}
          >
            {busy === state ? <span className="spinner" aria-hidden="true" /> : <Icon name={STATE_ICON[state]} />}
            {labels[state]}
          </button>
        ))}
      </fieldset>
    </form>
  )
}
