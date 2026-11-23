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
          {task.sourceEmailAddress && <p className="muted">Mesaj trimis pe email la finalizare:</p>}
          <p>{task.completionResponse}</p>
        </>
      ) : (
        <p className="muted">Fără mesaj de rezolvare.</p>
      )}
    </section>
  )
}

/** What completing does, said on its button: send the task back, email the message, or just complete it. */
function completeLabel(task: TaskItem): string {
  if (task.answersTo) return 'Trimite răspunsul'
  if (task.returnsTo) return `Rezolvat, înapoi la ${task.returnsTo.displayName}`
  return task.replyTo ? 'Trimite și finalizează' : 'Finalizat'
}

/** The message field's label and hint, which say where the message goes. */
function messageField(task: TaskItem): { label: string; placeholder: string } {
  if (task.answersTo) {
    return {
      label: `Răspuns pentru ${task.answersTo.displayName}`,
      placeholder: 'Obligatoriu la finalizare: ajunge la cine ți-a distribuit task-ul, în conversație.',
    }
  }
  if (task.returnsTo) {
    return {
      label: `Rezolvare pentru ${task.returnsTo.displayName}`,
      placeholder: task.sourceEmailAddress
        ? 'Obligatorie: task-ul se întoarce cu ea la cine ți l-a trimis.'
        : 'Ce s-a rezolvat; task-ul se întoarce cu acest mesaj.',
    }
  }
  if (task.replyTo) {
    return {
      label: `Răspuns către ${task.replyTo}`,
      placeholder: 'Obligatoriu la finalizare: se trimite pe email ca răspuns.',
    }
  }
  return { label: 'Mesaj de rezolvare', placeholder: 'Ce s-a rezolvat, sau ce aștepți (obligatoriu pentru „În așteptare”).' }
}

/** Guidance from whoever sent the task, shown above its message. */
export function Instructions({ task }: { task: TaskItem }) {
  if (!task.instructions) return null
  return (
    <section className="detail-callout instructions" role="note">
      <h3>
        <Icon name="flag" size={15} />
        Indicații de la {task.createdBy.displayName}
      </h3>
      <p>{task.instructions}</p>
    </section>
  )
}

/** The resolution a forwarded task came back with, while it waits to be closed. */
export function ReturnedResolution({ task }: { task: TaskItem }) {
  const last = task.forwards.at(-1)
  if (task.state === 'completed' || !last?.isReturn || !last.note) return null
  return (
    <section className="detail-callout done" role="note">
      <h3>
        <Icon name="check" size={15} />
        Rezolvat de {last.from.displayName}, întors la tine
      </h3>
      <p>{last.note}</p>
    </section>
  )
}

/** The label of each state button, which depends on where the task stands now. */
function buttonLabels(task: TaskItem): Record<TaskState, string> {
  const labels: Record<TaskState, string> = {
    open: 'Deschis',
    waiting: 'În așteptare',
    completed: completeLabel(task),
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
  onDistribute,
}: {
  task: TaskItem
  message: string
  /** The action under way, if any: its button shows a spinner and the others wait. */
  busy: string | null
  onMessage: (message: string) => void
  onState: (state: TaskState) => void
  /** Opens the distribution form; left out when the person cannot distribute the task. */
  onDistribute?: () => void
}) {
  const field = messageField(task)
  const labels = buttonLabels(task)
  const states: TaskState[] = task.state === 'completed' ? ['open'] : ['open', 'waiting', 'completed']
  return (
    <form className="detail-footer" onSubmit={(e) => e.preventDefault()} noValidate>
      {task.state !== 'completed' && (
        <label className="field">
          <span>{field.label}</span>
          <textarea
            value={message}
            onChange={(e) => onMessage(e.target.value)}
            rows={3}
            maxLength={LIMITS.response}
            placeholder={field.placeholder}
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
        {onDistribute && (
          <button type="button" className="state-button distribute" onClick={onDistribute} disabled={busy !== null}>
            <Icon name="users" />
            Distribuie
          </button>
        )}
      </fieldset>
    </form>
  )
}
