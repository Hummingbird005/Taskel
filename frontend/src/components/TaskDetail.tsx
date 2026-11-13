import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { Attachment, AttachmentLimits } from '../api/attachments'
import { DEFAULT_ATTACHMENT_LIMITS } from '../api/attachments'
import type { GroupListItem } from '../api/groups'
import type { TaskDraft, TaskItem, TaskState, TaskTarget } from '../api/tasks'
import type { UserSummary } from '../api/users'
import { formatDueDate, formatRange, formatTimestamp } from '../lib/dates'
import { isOverdue } from '../lib/taskViews'
import { LIMITS, validateNotes } from '../lib/validation'
import ForwardTask from './ForwardTask'
import Icon from './Icon'
import TaskAttachments from './TaskAttachments'
import TaskForm from './TaskForm'
import TaskTrail from './TaskTrail'

/**
 * What can be done with a task, each resolving to an error message to show, or null when it
 * worked. The caller keeps its list of tasks up to date; this dialog only shows the result.
 */
export interface TaskActions {
  save: (draft: TaskDraft) => Promise<string | null>
  complete: (response: string | null) => Promise<string | null>
  wait: (reason: string) => Promise<string | null>
  resume: () => Promise<string | null>
  reopen: () => Promise<string | null>
  forward: (target: TaskTarget, note: string | null) => Promise<string | null>
  claim: () => Promise<string | null>
  release: () => Promise<string | null>
  remove: () => Promise<string | null>
  attachmentsChanged: (attachments: Attachment[]) => void
}

interface Props {
  task: TaskItem
  today: string
  /** Leave out to show the task for reading only, as a manager reads the tasks of their people. */
  actions?: TaskActions
  people?: UserSummary[]
  groups?: GroupListItem[]
  currentUser?: { id: string }
  attachmentLimits?: AttachmentLimits
  /** Where this reader downloads the task's files from, when not through the task itself. */
  attachmentUrl?: (attachment: Attachment) => string
  /** Shows the tasks with a tag: closes the dialog and opens the tag's list. */
  onTagClick?: (tag: string) => void
  onSessionExpired?: () => void
  onClose: () => void
}

const STATE_LABEL: Record<TaskState, string> = { open: 'Deschis', waiting: 'În așteptare', completed: 'Finalizat' }

/**
 * A task opened from a list: its title and message, the details, the files and the path it took,
 * and, for the people working on it, a message and the buttons that change its state.
 */
export default function TaskDetail({
  task,
  today,
  actions,
  people = [],
  groups = [],
  currentUser,
  attachmentLimits = DEFAULT_ATTACHMENT_LIMITS,
  attachmentUrl,
  onTagClick,
  onSessionExpired,
  onClose,
}: Props) {
  const dialog = useRef<HTMLDialogElement>(null)
  const [mode, setMode] = useState<'view' | 'edit' | 'forward'>('view')
  const [message, setMessage] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  const canWork = Boolean(actions) && (task.access === 'creator' || task.access === 'assignee')
  const isCreator = Boolean(actions) && task.access === 'creator'
  const waitsOnGroup = Boolean(actions) && task.access === 'group'
  const needsReply = Boolean(task.sourceEmailAddress)
  const overdue = isOverdue(task, today)
  const hasInterval = task.startDate !== null && task.dueDate !== null && task.startDate !== task.dueDate

  /** Runs an action, shows its error if it had one, and returns whether it worked. */
  async function run(name: string, action: () => Promise<string | null>): Promise<boolean> {
    if (busy) return false
    setBusy(name)
    setError(null)
    const failure = await action()
    setBusy(null)
    if (failure) setError(failure)
    return !failure
  }

  async function changeState(next: TaskState) {
    if (!actions || next === task.state) {
      // Pressing "În așteptare" again on a task on hold updates the reason.
      if (!(actions && next === 'waiting' && task.state === 'waiting')) return
    }
    const text = message.trim()
    const invalid = validateNotes(text, next === 'waiting' ? 1000 : LIMITS.response)
    if (invalid) {
      setError(invalid)
      return
    }
    let worked = false
    if (next === 'waiting') {
      if (!text) {
        setError('Scrie în câmpul de mai jos motivul pentru care task-ul este în așteptare.')
        return
      }
      worked = await run('waiting', () => actions!.wait(text))
    } else if (next === 'completed') {
      if (needsReply && !text) {
        setError(`Scrie răspunsul: se trimite pe email către ${task.sourceEmailAddress}, apoi task-ul se finalizează.`)
        return
      }
      worked = await run('completed', () => actions!.complete(text || null))
    } else {
      worked = await run('open', () => (task.state === 'waiting' ? actions!.resume() : actions!.reopen()))
    }
    if (worked) setMessage('')
  }

  async function remove() {
    if (!actions || !window.confirm(`Ștergi task-ul „${task.title}”? Nu poate fi recuperat.`)) return
    if (await run('remove', actions.remove)) dialog.current?.close()
  }

  const stateButton = (state: TaskState, icon: 'clock' | 'check' | 'inbox', label: string) => (
    <button
      type="button"
      className={`state-button ${state}`}
      aria-pressed={task.state === state}
      disabled={busy !== null || (state === 'open' && task.state === 'open')}
      onClick={() => void changeState(state)}
    >
      {busy === state ? <span className="spinner" aria-hidden="true" /> : <Icon name={icon} />}
      {label}
    </button>
  )

  return (
    <dialog ref={dialog} className="task-dialog task-detail" aria-labelledby="task-detail-title" onClose={onClose}>
      <header className="task-dialog-head">
        <span className={`state-pill ${task.state}`}>
          <Icon name={task.state === 'completed' ? 'check' : task.state === 'waiting' ? 'clock' : 'inbox'} size={14} />
          {waitsOnGroup ? 'De preluat' : STATE_LABEL[task.state]}
        </span>
        <div className="detail-tools">
          {canWork && mode === 'view' && (
            <button type="button" className="icon-button" onClick={() => setMode('edit')} title="Editează" aria-label="Editează">
              <Icon name="edit" />
            </button>
          )}
          {actions && task.access === 'assignee' && task.state !== 'completed' && mode === 'view' && (
            <button type="button" className="icon-button" onClick={() => setMode('forward')} title="Direcționează" aria-label="Direcționează">
              <Icon name="forward" />
            </button>
          )}
          {actions && task.access === 'assignee' && task.group && task.state !== 'completed' && mode === 'view' && (
            <button
              type="button"
              className="icon-button"
              onClick={() => void run('release', actions.release)}
              title={`Dă înapoi grupului ${task.group.name}`}
              aria-label={`Dă înapoi grupului ${task.group.name}`}
            >
              <Icon name="users" />
            </button>
          )}
          {isCreator && mode === 'view' && (
            <button type="button" className="icon-button danger" onClick={() => void remove()} title="Șterge" aria-label="Șterge task-ul">
              <Icon name="trash" />
            </button>
          )}
          <button type="button" className="icon-button" onClick={() => dialog.current?.close()} aria-label="Închide">
            <Icon name="close" />
          </button>
        </div>
      </header>

      {mode === 'edit' && actions ? (
        <TaskForm
          variant="dialog"
          initial={{ ...task, assigneeId: task.assignee?.id ?? null, groupId: task.assignee ? null : (task.group?.id ?? null) }}
          // Only the creator can reassign, so only they are offered the choice.
          people={isCreator ? people : []}
          groups={groups}
          currentUser={currentUser}
          taskId={task.id}
          submitLabel="Salvează"
          placeholder="Titlul task-ului"
          onCancel={() => setMode('view')}
          onSubmit={async (draft) => {
            const failure = await actions.save(draft)
            if (failure) {
              setError(failure)
              return false
            }
            setMode('view')
            return true
          }}
        />
      ) : mode === 'forward' && actions ? (
        <div className="detail-body">
          <ForwardTask
            task={task}
            people={people}
            groups={groups}
            onCancel={() => setMode('view')}
            onSubmit={async (target, note) => {
              const failure = await actions.forward(target, note)
              if (!failure) dialog.current?.close()
              return failure
            }}
          />
        </div>
      ) : (
        <>
          <div className="detail-body">
            <h2 id="task-detail-title" className="detail-title">
              {task.title}
            </h2>

            <dl className="detail-facts">
              {task.dueDate && (
                <div>
                  <dt>{hasInterval ? 'Interval' : 'Termen'}</dt>
                  <dd className={overdue ? 'overdue' : undefined}>
                    {hasInterval ? formatRange(task.startDate!, task.dueDate) : task.dueDate === today ? 'azi' : formatDueDate(task.dueDate)}
                    {overdue && ' · întârziat'}
                  </dd>
                </div>
              )}
              <div>
                <dt>Prioritate</dt>
                <dd className={task.priority === 'high' ? 'overdue' : undefined}>
                  {task.priority === 'high' ? 'Ridicată' : task.priority === 'low' ? 'Scăzută' : 'Medie'}
                </dd>
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
                  <dd className="detail-tags">
                    {task.tags.map((tag) =>
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
                </div>
              )}
            </dl>

            <section className="detail-section">
              <h3>Mesaj</h3>
              {task.description ? <p className="detail-message">{task.description}</p> : <p className="muted">Fără mesaj.</p>}
            </section>

            {task.state === 'waiting' && (
              <section className="detail-callout waiting" role="note">
                <h3>
                  <Icon name="clock" size={15} />
                  În așteptare{task.waitingSince && ` din ${formatTimestamp(task.waitingSince)}`}
                </h3>
                <p>{task.waitingReason}</p>
              </section>
            )}
            {task.state === 'completed' && (
              <section className="detail-callout done" role="note">
                <h3>
                  <Icon name="check" size={15} />
                  Finalizat{task.completedAt && ` pe ${formatTimestamp(task.completedAt)}`}
                </h3>
                {task.completionResponse ? (
                  <>
                    {needsReply && <p className="muted">Răspuns trimis către {task.sourceEmailAddress}:</p>}
                    <p>{task.completionResponse}</p>
                  </>
                ) : (
                  <p className="muted">Fără mesaj de rezolvare.</p>
                )}
              </section>
            )}

            {(canWork || task.attachments.length > 0) && (
              <section className="detail-section">
                <TaskAttachments
                  task={task}
                  limits={attachmentLimits}
                  canEdit={canWork}
                  showWhenEmpty={canWork}
                  defaultOpen
                  downloadUrl={attachmentUrl}
                  onChange={actions?.attachmentsChanged}
                  onSessionExpired={onSessionExpired}
                />
              </section>
            )}
            <TaskTrail task={task} />
            {error && (
              <p className="error detail-error" role="alert">
                {error}
              </p>
            )}
          </div>

          {canWork && (
            <form className="detail-footer" onSubmit={(e: FormEvent) => e.preventDefault()} noValidate>
              {task.state !== 'completed' && (
                <label className="field">
                  <span>{needsReply ? `Răspuns către ${task.sourceEmailAddress}` : 'Mesaj de rezolvare'}</span>
                  <textarea
                    value={message}
                    onChange={(e) => {
                      setError(null)
                      setMessage(e.target.value)
                    }}
                    rows={3}
                    maxLength={LIMITS.response}
                    placeholder={
                      needsReply
                        ? 'Obligatoriu la finalizare: se trimite pe email ca răspuns.'
                        : 'Ce s-a rezolvat, sau ce aștepți (obligatoriu pentru „În așteptare”).'
                    }
                  />
                </label>
              )}
              <div className="state-bar" role="group" aria-label="Starea task-ului">
                {stateButton('open', 'inbox', task.state === 'completed' ? 'Redeschide' : task.state === 'waiting' ? 'Reia lucrul' : 'Deschis')}
                {task.state !== 'completed' &&
                  stateButton('waiting', 'clock', task.state === 'waiting' ? 'Schimbă motivul' : 'În așteptare')}
                {task.state !== 'completed' && stateButton('completed', 'check', needsReply ? 'Trimite și finalizează' : 'Finalizat')}
              </div>
            </form>
          )}
          {waitsOnGroup && actions && (
            <div className="detail-footer">
              <p className="muted">Task-ul așteaptă ca cineva din grupul {task.group?.name} să îl preia.</p>
              <div className="state-bar">
                <button type="button" className="state-button completed" onClick={() => void run('claim', actions.claim)} disabled={busy !== null}>
                  <Icon name="hand" />
                  Preia task-ul
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </dialog>
  )
}
