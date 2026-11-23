import { useEffect, useRef, useState } from 'react'
import type { Attachment, AttachmentLimits } from '../api/attachments'
import { attachmentsApi, checkFiles, DEFAULT_ATTACHMENT_LIMITS, uploadErrorText } from '../api/attachments'
import { ApiError } from '../api/client'
import type { GroupListItem } from '../api/groups'
import type { TaskDraft, TaskItem, TaskState, TaskTarget } from '../api/tasks'
import type { UserSummary } from '../api/users'
import { LIMITS, validateNotes } from '../lib/validation'
import ForwardTask from './ForwardTask'
import Icon from './Icon'
import TaskAttachments from './TaskAttachments'
import TaskForm from './TaskForm'
import { canDistribute, STATE_ICON, STATE_LABEL, stateChangeProblem } from '../lib/taskStates'
import Conversation from './Conversation'
import DistributeTask from './DistributeTask'
import { DistributedFrom, DistributionPanel } from './Distributions'
import { DetailFacts, Instructions, ReturnedResolution, StateCallout, WorkFooter } from './TaskDetailParts'
import TaskTrail from './TaskTrail'

/**
 * What can be done with a task, each resolving to an error message to show, or null when it
 * worked. The caller keeps its list of tasks up to date; this dialog only shows the result.
 */
export interface TaskActions {
  save: (draft: TaskDraft) => Promise<string | null>
  /** Completes the task; `attachmentIds` are files of the task sent with the answer. */
  complete: (response: string | null, attachmentIds?: string[]) => Promise<string | null>
  wait: (reason: string) => Promise<string | null>
  resume: () => Promise<string | null>
  reopen: () => Promise<string | null>
  forward: (target: TaskTarget, note: string | null, returnToMe: boolean) => Promise<string | null>
  distribute: (recipients: string[], message: string | null, dueDate: string | null) => Promise<string | null>
  /** Reopens one answered copy of the distributed task, with a message. */
  sendBack: (copyId: string, message: string) => Promise<string | null>
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
  /** For a manager reading a team member's task: moves its due date. Resolves to an error message, or null. */
  onChangeDueDate?: (dueDate: string | null) => Promise<string | null>
  onSessionExpired?: () => void
  onClose: () => void
}

/** The request that moves a task to `next`. */
function stateAction(
  actions: TaskActions,
  task: TaskItem,
  next: TaskState,
  text: string,
  answerFiles: string[],
): Promise<string | null> {
  if (next === 'waiting') return actions.wait(text)
  if (next === 'completed') return actions.complete(text || null, answerFiles)
  return task.state === 'waiting' ? actions.resume() : actions.reopen()
}

/** Who has the task now, as the edit form shows it: a group task someone took shows that person. */
const formTarget = (task: TaskItem) =>
  task.assignee ? { assigneeId: task.assignee.id, groupId: null } : { assigneeId: null, groupId: task.group?.id ?? null }

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
  onChangeDueDate,
  onSessionExpired,
  onClose,
}: Props) {
  const dialog = useRef<HTMLDialogElement>(null)
  const [mode, setMode] = useState<'view' | 'edit' | 'forward' | 'distribute'>('view')
  const [message, setMessage] = useState('')
  // Files uploaded to go with the answer, sent with it when the task is completed.
  const [answerFiles, setAnswerFiles] = useState<Attachment[]>([])
  const [uploading, setUploading] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  const close = () => dialog.current?.close()

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
    // Pressing "În așteptare" again on a task on hold updates the reason; other repeats do nothing.
    if (!actions || (next === task.state && next !== 'waiting')) return
    const text = message.trim()
    const problem = validateNotes(text, next === 'waiting' ? 1000 : LIMITS.response) ?? stateChangeProblem(task, next, text)
    if (problem) {
      setError(problem)
      return
    }
    const files = next === 'completed' ? answerFiles.map((f) => f.id) : []
    if (await run(next, () => stateAction(actions, task, next, text, files))) {
      setMessage('')
      if (next === 'completed') setAnswerFiles([])
    }
  }

  /** Uploads files to the task, to be sent with the answer. */
  async function addAnswerFiles(picked: File[]) {
    if (!actions || picked.length === 0) return
    const invalid = checkFiles(picked, task.attachments.length, attachmentLimits)
    if (invalid) {
      setError(invalid)
      return
    }
    setError(null)
    let all = task.attachments
    for (const file of picked) {
      setUploading(file.name)
      try {
        const added = await attachmentsApi.upload(task.id, file)
        all = [...all, added]
        actions.attachmentsChanged(all)
        setAnswerFiles((prev) => [...prev, added])
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) onSessionExpired?.()
        else setError(uploadErrorText(err, file.name, attachmentLimits))
        break
      }
    }
    setUploading(null)
  }

  async function remove() {
    if (!actions || !window.confirm(`Ștergi task-ul „${task.title}”? Nu poate fi recuperat.`)) return
    if (await run('remove', actions.remove)) close()
  }

  return (
    <dialog
      ref={dialog}
      // Wide, with the conversation beside the task, for those who can read the conversation.
      className={`task-dialog task-detail${actions && mode === 'view' ? ' with-conversation' : ''}`}
      aria-labelledby="task-detail-title"
      onClose={onClose}
    >
      <header className="task-dialog-head">
        <span className={`state-pill ${task.state}`}>
          <Icon name={STATE_ICON[task.state]} size={14} />
          {actions && task.access === 'group' ? 'De preluat' : STATE_LABEL[task.state]}
        </span>
        <DetailTools
          task={task}
          actions={mode === 'view' ? actions : undefined}
          onEdit={() => setMode('edit')}
          onForward={() => setMode('forward')}
          onRelease={(release) => {
            run('release', release)
          }}
          onRemove={() => {
            remove()
          }}
          onClose={close}
        />
      </header>

      {mode === 'edit' && actions && (
        <TaskForm
          initial={{ ...task, ...formTarget(task) }}
          // Only the creator can reassign, so only they are offered the choice.
          people={task.access === 'creator' ? people : []}
          groups={groups}
          currentUser={currentUser}
          taskId={task.id}
          submitLabel="Salvează"
          placeholder="Titlul task-ului"
          onCancel={() => setMode('view')}
          onSubmit={async (draft) => {
            const failure = await actions.save(draft)
            if (failure) setError(failure)
            else setMode('view')
            return !failure
          }}
        />
      )}
      {mode === 'forward' && actions && (
        <div className="detail-body">
          <ForwardTask
            task={task}
            people={people}
            groups={groups}
            onCancel={() => setMode('view')}
            onSubmit={async (target, note, returnToMe) => {
              const failure = await actions.forward(target, note, returnToMe)
              if (!failure) close()
              return failure
            }}
          />
        </div>
      )}
      {mode === 'distribute' && actions && (
        <div className="detail-body">
          <DistributeTask
            task={task}
            people={people}
            groups={groups}
            currentUserId={currentUser?.id}
            onCancel={() => setMode('view')}
            onSubmit={async (recipients, note, dueDate) => {
              const failure = await actions.distribute(recipients, note, dueDate)
              if (!failure) setMode('view')
              return failure
            }}
          />
        </div>
      )}
      {(mode === 'view' || !actions) && (
        <div className="detail-columns">
          <div className="detail-main">
            <DetailView
              task={task}
              today={today}
              actions={actions}
              message={message}
              error={error}
              busy={busy}
              attachmentLimits={attachmentLimits}
              attachmentUrl={attachmentUrl}
              onTagClick={onTagClick}
              onChangeDueDate={onChangeDueDate}
              onSessionExpired={onSessionExpired}
              onMessage={(text) => {
                setError(null)
                setMessage(text)
              }}
              onState={(state) => {
                changeState(state)
              }}
              onClaim={(claim) => {
                run('claim', claim)
              }}
              onDistribute={() => setMode('distribute')}
              answerFiles={answerFiles}
              uploading={uploading}
              onAddAnswerFiles={(files) => {
                addAnswerFiles(files)
              }}
              onDropAnswerFile={(id) => setAnswerFiles((prev) => prev.filter((f) => f.id !== id))}
            />
          </div>
          {actions && (
            <aside className="detail-side">
              <Conversation
                task={task}
                currentUserId={currentUser?.id}
                canWrite={task.access === 'creator' || task.access === 'assignee'}
                onSessionExpired={onSessionExpired}
              />
            </aside>
          )}
        </div>
      )}
    </dialog>
  )
}

/** The icon buttons in the dialog's header: edit, forward, hand back to the group, delete, close. */
function DetailTools({
  task,
  actions,
  onEdit,
  onForward,
  onRelease,
  onRemove,
  onClose,
}: {
  task: TaskItem
  /** Left out while editing or forwarding, and for readers: only the close button shows. */
  actions?: TaskActions
  onEdit: () => void
  onForward: () => void
  onRelease: (release: () => Promise<string | null>) => void
  onRemove: () => void
  onClose: () => void
}) {
  const canWork = task.access === 'creator' || task.access === 'assignee'
  const holds = task.access === 'assignee' && task.state !== 'completed'
  const releaseLabel = task.group ? `Dă înapoi grupului ${task.group.name}` : ''
  return (
    <div className="detail-tools">
      {actions && canWork && (
        <button type="button" className="icon-button" onClick={onEdit} title="Editează" aria-label="Editează">
          <Icon name="edit" />
        </button>
      )}
      {actions && holds && (
        <button type="button" className="icon-button" onClick={onForward} title="Direcționează" aria-label="Direcționează">
          <Icon name="forward" />
        </button>
      )}
      {actions && holds && task.group && (
        <button
          type="button"
          className="icon-button"
          onClick={() => onRelease(actions.release)}
          title={releaseLabel}
          aria-label={releaseLabel}
        >
          <Icon name="users" />
        </button>
      )}
      {actions && task.access === 'creator' && (
        <button type="button" className="icon-button danger" onClick={onRemove} title="Șterge" aria-label="Șterge task-ul">
          <Icon name="trash" />
        </button>
      )}
      <button type="button" className="icon-button" onClick={onClose} aria-label="Închide">
        <Icon name="close" />
      </button>
    </div>
  )
}

/** The task as read: title, facts, message, state, files, path, and the footer for whoever works on it. */
function DetailView({
  task,
  today,
  actions,
  message,
  error,
  busy,
  attachmentLimits,
  attachmentUrl,
  onTagClick,
  onChangeDueDate,
  onSessionExpired,
  onMessage,
  onState,
  onClaim,
  onDistribute,
  answerFiles,
  uploading,
  onAddAnswerFiles,
  onDropAnswerFile,
}: {
  task: TaskItem
  today: string
  actions?: TaskActions
  message: string
  error: string | null
  busy: string | null
  attachmentLimits: AttachmentLimits
  attachmentUrl?: (attachment: Attachment) => string
  onTagClick?: (tag: string) => void
  onChangeDueDate?: (dueDate: string | null) => Promise<string | null>
  onSessionExpired?: () => void
  onMessage: (message: string) => void
  onState: (state: TaskState) => void
  onClaim: (claim: () => Promise<string | null>) => void
  onDistribute: () => void
  answerFiles: Attachment[]
  uploading: string | null
  onAddAnswerFiles: (files: File[]) => void
  onDropAnswerFile: (id: string) => void
}) {
  const canWork = Boolean(actions) && (task.access === 'creator' || task.access === 'assignee')
  return (
    <>
      <div className="detail-body">
        <h2 id="task-detail-title" className="detail-title">
          {task.title}
        </h2>
        <DetailFacts task={task} today={today} onTagClick={onTagClick} />
        {onChangeDueDate && task.state !== 'completed' && <DueDateEditor task={task} onChange={onChangeDueDate} />}
        <DistributedFrom task={task} />
        <Instructions task={task} />
        <ReturnedResolution task={task} />
        <section className="detail-section">
          <h3>Mesaj</h3>
          {task.description ? <p className="detail-message">{task.description}</p> : <p className="muted">Fără mesaj.</p>}
        </section>
        <StateCallout task={task} />
        <DistributionPanel task={task} onSendBack={actions && canDistribute(task) ? actions.sendBack : undefined} />
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
        <WorkFooter
          task={task}
          message={message}
          busy={busy}
          onMessage={onMessage}
          onState={onState}
          onDistribute={canDistribute(task) ? onDistribute : undefined}
          answerFiles={answerFiles}
          uploading={uploading}
          onAddAnswerFiles={onAddAnswerFiles}
          onDropAnswerFile={onDropAnswerFile}
        />
      )}
      {actions && task.access === 'group' && (
        <div className="detail-footer">
          <p className="muted">Task-ul așteaptă ca cineva din grupul {task.group?.name} să îl preia.</p>
          <div className="state-bar">
            <button type="button" className="state-button completed" onClick={() => onClaim(actions.claim)} disabled={busy !== null}>
              <Icon name="hand" />
              Preia task-ul
            </button>
          </div>
        </div>
      )}
    </>
  )
}

/** For a manager: moves the due date of a task one of their people answers for. */
function DueDateEditor({ task, onChange }: { task: TaskItem; onChange: (dueDate: string | null) => Promise<string | null> }) {
  const [value, setValue] = useState(task.dueDate ?? '')
  const [state, setState] = useState<{ busy: boolean; error: string | null; saved: boolean }>({ busy: false, error: null, saved: false })
  const changed = value !== (task.dueDate ?? '')

  async function save() {
    setState({ busy: true, error: null, saved: false })
    const failure = await onChange(value || null)
    setState({ busy: false, error: failure, saved: !failure })
  }

  return (
    <section className="detail-callout due-editor" aria-label="Schimbă termenul">
      <h3>
        <Icon name="calendar" size={15} />
        Termenul, ca manager
      </h3>
      <div className="due-editor-row">
        <input
          type="date"
          value={value}
          min={task.startDate ?? '2000-01-01'}
          max="2100-12-31"
          onChange={(e) => {
            setValue(e.target.value)
            setState({ busy: false, error: null, saved: false })
          }}
          aria-label="Termen nou"
        />
        <button type="button" className="button primary small" onClick={save} disabled={!changed || state.busy}>
          {state.busy ? <span className="spinner" aria-hidden="true" /> : <Icon name="check" />}
          Mută termenul
        </button>
        {value && (
          <button type="button" className="button ghost small" onClick={() => setValue('')} disabled={state.busy}>
            Fără termen
          </button>
        )}
      </div>
      {state.error && (
        <p className="error" role="alert">
          {state.error}
        </p>
      )}
      {state.saved && <p className="muted">Termen mutat. Persoana responsabilă a primit un email.</p>}
      {!state.error && !state.saved && <p className="muted">Doar termenul se poate schimba; restul task-ului rămâne al persoanei.</p>}
    </section>
  )
}
