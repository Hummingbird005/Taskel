import { useEffect, useRef, useState } from 'react'
import type { Attachment, AttachmentLimits } from '../api/attachments'
import { attachmentsApi, checkFiles, DEFAULT_ATTACHMENT_LIMITS, uploadErrorText } from '../api/attachments'
import { ApiError } from '../api/client'
import type { GroupListItem } from '../api/groups'
import { tasksApi, type TaskDraft, type TaskItem, type TaskMessage, type TaskPriority, type TaskTarget } from '../api/tasks'
import type { Reschedule } from '../api/team'
import type { UserSummary } from '../api/users'
import { formatDueDate, formatRange } from '../lib/dates'
import { canDistribute, STATE_ICON, STATE_LABEL } from '../lib/taskStates'
import { buildThread } from '../lib/thread'
import { isOverdue } from '../lib/taskViews'
import { validateNotes } from '../lib/validation'
import DistributeTask from './DistributeTask'
import ForwardTask from './ForwardTask'
import Icon from './Icon'
import ReplyBox from './ReplyBox'
import RescheduleForm from './RescheduleForm'
import TaskAttachments from './TaskAttachments'
import TaskForm from './TaskForm'
import TaskThread from './TaskThread'

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
  /** For a manager reading a team member's task: moves it to another day or level. Resolves to an error message, or null. */
  onReschedule?: (change: Reschedule) => Promise<string | null>
  onSessionExpired?: () => void
  onClose: () => void
}

const PRIORITY_LABEL: Record<TaskPriority, string> = { high: 'Prioritate ridicată', medium: 'Prioritate medie', low: 'Prioritate scăzută' }

/** Who has the task now, as the edit form shows it: a group task someone took shows that person. */
const formTarget = (task: TaskItem) =>
  task.assignee ? { assigneeId: task.assignee.id, groupId: null } : { assigneeId: null, groupId: task.group?.id ?? null }

/** An answer sent without completing, refused: why, in words. */
function sendErrorText(err: unknown): string {
  if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel. Răspunsul nu a fost trimis.'
  switch (err.status) {
    case 503:
      return 'Trimiterea de emailuri nu este configurată pe serverul Taskel, deci răspunsul nu poate fi trimis.'
    case 502:
      return 'Emailul de răspuns nu a putut fi trimis. Încearcă din nou.'
    case 409:
      return 'Nu ai cui să trimiți: task-ul e al tău și nu a venit dintr-un email, sau e deja finalizat.'
    case 400:
      return 'Răspunsul este obligatoriu și poate avea cel mult 4000 de caractere.'
    default:
      return 'Răspunsul nu a putut fi trimis. Încearcă din nou.'
  }
}

/**
 * The task conversation, read again whenever the task changes: an answer, a send-back or a new distribution adds
 * to it. A reader without access to it (a manager reading a team member's task) gets none.
 */
function useConversation(task: TaskItem, onSessionExpired?: () => void) {
  const [messages, setMessages] = useState<TaskMessage[]>([])
  const version = `${task.id}|${task.state}|${task.completedAt}|${task.dueDate}|${task.priorityLevel}|${task.distributions?.map((d) => d.state).join()}`
  const expired = useRef(onSessionExpired)
  useEffect(() => {
    expired.current = onSessionExpired
  })
  useEffect(() => {
    let cancelled = false
    tasksApi
      .conversation(task.id)
      .then((list) => {
        if (!cancelled) setMessages(list)
      })
      .catch((err) => {
        if (!cancelled && err instanceof ApiError && err.status === 401) expired.current?.()
      })
    return () => {
      cancelled = true
    }
  }, [task.id, version])
  return [messages, setMessages] as const
}

/**
 * A task opened from a list, as a mail thread: the subject and its facts, the actions, the email or message it
 * started from with everything that happened since, and, for whoever works on it, the reply at the end.
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
  onReschedule,
  onSessionExpired,
  onClose,
}: Props) {
  const dialog = useRef<HTMLDialogElement>(null)
  const reply = useRef<HTMLTextAreaElement>(null)
  const [mode, setMode] = useState<'view' | 'edit' | 'forward' | 'distribute'>('view')
  const [waitOpen, setWaitOpen] = useState(false)
  // Files uploaded to go with the answer, sent with it.
  const [answerFiles, setAnswerFiles] = useState<Attachment[]>([])
  const [uploading, setUploading] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [messages, setMessages] = useConversation(task, onSessionExpired)

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  const close = () => dialog.current?.close()
  const canWork = Boolean(actions) && (task.access === 'creator' || task.access === 'assignee')
  const holds = canWork && task.access === 'assignee' && task.state !== 'completed'

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
        // Uploaded for the answer: it shows with the answer, not with the email.
        all = [...all, { ...added, withAnswer: true }]
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

  /** After an answer went with files: they belong to it now. */
  function filesSent(ids: string[]) {
    if (!actions || ids.length === 0) return
    actions.attachmentsChanged(task.attachments.map((a) => (ids.includes(a.id) ? { ...a, withAnswer: true } : a)))
    setAnswerFiles([])
  }

  async function sendOnly(text: string): Promise<boolean> {
    if (busy) return false
    setBusy('send')
    setError(null)
    const ids = answerFiles.map((f) => f.id)
    try {
      const sent = await tasksApi.reply(task.id, text, ids)
      // On a copy the answer lives on the distributor's task; reading the conversation again shows it.
      if (task.distributedFrom) setMessages(await tasksApi.conversation(task.id))
      else setMessages((prev) => [...prev, sent])
      filesSent(ids)
      return true
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired?.()
      else setError(sendErrorText(err))
      return false
    } finally {
      setBusy(null)
    }
  }

  async function writeMessage(text: string, copyId: string | null): Promise<boolean> {
    if (busy) return false
    setBusy('note')
    setError(null)
    try {
      const posted = await tasksApi.postMessage(task.id, text, copyId)
      setMessages((prev) => [...prev, posted])
      return true
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired?.()
      else setError('Mesajul nu a putut fi trimis. Încearcă din nou.')
      return false
    } finally {
      setBusy(null)
    }
  }

  async function complete(text: string): Promise<boolean> {
    if (!actions) return false
    const ids = answerFiles.map((f) => f.id)
    const done = await run('completed', () => actions.complete(text || null, ids))
    if (done) setAnswerFiles([])
    return done
  }

  async function remove() {
    if (!actions || !window.confirm(`Ștergi task-ul „${task.title}”? Nu poate fi recuperat.`)) return
    if (await run('remove', actions.remove)) close()
  }

  function focusReply() {
    setWaitOpen(false)
    reply.current?.focus()
    reply.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }

  const items = buildThread(task, messages, Boolean(actions) && canDistribute(task))
  const showReply = canWork && task.state !== 'completed'

  return (
    <dialog ref={dialog} className="task-dialog task-detail mail" aria-labelledby="task-detail-title" onClose={onClose}>
      <header className="mail-head">
        <div className="mail-head-text">
          <h2 id="task-detail-title">{task.title}</h2>
          <MailFacts task={task} today={today} onTagClick={onTagClick} />
        </div>
        <div className="detail-tools">
          {mode === 'view' && canWork && (
            <button type="button" className="icon-button" onClick={() => setMode('edit')} title="Editează" aria-label="Editează">
              <Icon name="edit" />
            </button>
          )}
          {mode === 'view' && holds && task.group && actions && (
            <button
              type="button"
              className="icon-button"
              onClick={() => run('release', actions.release)}
              title={`Dă înapoi grupului ${task.group.name}`}
              aria-label={`Dă înapoi grupului ${task.group.name}`}
            >
              <Icon name="users" />
            </button>
          )}
          {mode === 'view' && actions && task.access === 'creator' && (
            <button type="button" className="icon-button danger" onClick={remove} title="Șterge" aria-label="Șterge task-ul">
              <Icon name="trash" />
            </button>
          )}
          <button type="button" className="icon-button" onClick={close} aria-label="Închide">
            <Icon name="close" />
          </button>
        </div>
      </header>

      {mode === 'view' && actions && (canWork || task.access === 'group') && (
        <MailToolbar
          task={task}
          busy={busy}
          canWork={canWork}
          holds={holds}
          waitOpen={waitOpen}
          onReply={focusReply}
          onDistribute={() => setMode('distribute')}
          onForward={() => setMode('forward')}
          onWait={() => setWaitOpen((open) => !open)}
          onResume={() => run('open', actions.resume)}
          onReopen={() => run('open', actions.reopen)}
          onClaim={() => run('claim', actions.claim)}
        />
      )}

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

      {mode === 'view' && (
        <div className="mail-body">
          <div className="mail-column">
            {onReschedule && task.state !== 'completed' && (
              <section className="detail-callout due-editor" aria-label="Mută task-ul">
                <h3>
                  <Icon name="calendar" size={15} />
                  Termen și nivel, ca manager
                </h3>
                <RescheduleForm task={task} onSubmit={onReschedule} />
              </section>
            )}
            {waitOpen && actions && (
              <WaitForm
                task={task}
                busy={busy === 'waiting'}
                onCancel={() => setWaitOpen(false)}
                onSubmit={async (reason) => {
                  const done = await run('waiting', () => actions.wait(reason))
                  if (done) setWaitOpen(false)
                  return done
                }}
              />
            )}
            <TaskThread
              task={task}
              items={items}
              currentUserId={currentUser?.id}
              attachmentUrl={attachmentUrl}
              onSendBack={actions && canDistribute(task) ? actions.sendBack : undefined}
            />
            {actions && task.access === 'group' && (
              <p className="muted mail-group-hint">Task-ul așteaptă ca cineva din grupul {task.group?.name} să îl preia.</p>
            )}
            {showReply ? (
              <ReplyBox
                ref={reply}
                task={task}
                answerFiles={answerFiles}
                uploading={uploading}
                busy={busy}
                error={error}
                onError={setError}
                onAddFiles={(files) => {
                  addAnswerFiles(files)
                }}
                onDropFile={(id) => setAnswerFiles((prev) => prev.filter((f) => f.id !== id))}
                onComplete={complete}
                onSend={sendOnly}
                onMessage={writeMessage}
              />
            ) : (
              error && (
                <p className="error detail-error" role="alert">
                  {error}
                </p>
              )
            )}
            {(canWork || task.attachments.length > 0) && (
              <section className="mail-files-section">
                <TaskAttachments
                  task={task}
                  limits={attachmentLimits}
                  canEdit={canWork}
                  showWhenEmpty={false}
                  downloadUrl={attachmentUrl}
                  onChange={actions?.attachmentsChanged}
                  onSessionExpired={onSessionExpired}
                />
              </section>
            )}
          </div>
        </div>
      )}
    </dialog>
  )
}

/** One line under the subject: state, due date, level, priority, tags, and who the task is from and with. */
function MailFacts({ task, today, onTagClick }: { task: TaskItem; today: string; onTagClick?: (tag: string) => void }) {
  const overdue = isOverdue(task, today)
  const due =
    task.startDate && task.dueDate && task.startDate !== task.dueDate
      ? `Interval ${formatRange(task.startDate, task.dueDate)}`
      : task.dueDate
        ? task.dueDate === today
          ? 'Termen azi'
          : `Termen ${formatDueDate(task.dueDate)}`
        : null
  const people = task.distributedFrom
    ? `Distribuit de ${task.createdBy.displayName}, din „${task.distributedFrom.title}”`
    : task.assignee
      ? `De la ${task.createdBy.displayName}, pentru ${task.assignee.displayName}`
      : task.group
        ? `De la ${task.createdBy.displayName}, pentru grupul ${task.group.name}`
        : `Creat de ${task.createdBy.displayName}`
  return (
    <div className="mail-facts">
      <span className={`state-pill ${task.state}`}>
        <Icon name={STATE_ICON[task.state]} size={13} />
        {task.access === 'group' && task.state === 'open' ? 'De preluat' : STATE_LABEL[task.state]}
      </span>
      {due && (
        <span className={overdue ? 'overdue' : undefined}>
          <Icon name="calendar" size={14} />
          {due}
          {overdue && ' · întârziat'}
        </span>
      )}
      {task.priorityLevel != null && (
        <span>
          <span className="level-badge">{task.priorityLevel}</span>
          Nivel {task.priorityLevel} din 20
        </span>
      )}
      <span className={task.priority === 'high' ? 'overdue' : undefined}>
        <Icon name="flag" size={14} />
        {PRIORITY_LABEL[task.priority]}
      </span>
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
      <span className="mail-facts-people">{people}</span>
    </div>
  )
}

/** The mail actions: reply, distribute, forward, put on hold; or reopen, or take a group's task. */
function MailToolbar({
  task,
  busy,
  canWork,
  holds,
  waitOpen,
  onReply,
  onDistribute,
  onForward,
  onWait,
  onResume,
  onReopen,
  onClaim,
}: {
  task: TaskItem
  busy: string | null
  canWork: boolean
  holds: boolean
  waitOpen: boolean
  onReply: () => void
  onDistribute: () => void
  onForward: () => void
  onWait: () => void
  onResume: () => void
  onReopen: () => void
  onClaim: () => void
}) {
  const spinner = (name: string, icon: Parameters<typeof Icon>[0]['name']) =>
    busy === name ? <span className="spinner" aria-hidden="true" /> : <Icon name={icon} />
  const copies = task.distributions ?? []
  const answered = copies.filter((c) => c.state === 'completed').length
  const status =
    task.state === 'completed'
      ? null
      : copies.length > 0
        ? `${answered} din ${copies.length} au răspuns${answered === copies.length ? ' · task-ul e din nou la tine' : ''}`
        : task.state === 'waiting'
          ? 'În așteptare'
          : null

  if (task.access === 'group') {
    return (
      <nav className="mail-toolbar" aria-label="Acțiuni">
        <button type="button" className="button primary" onClick={onClaim} disabled={busy !== null}>
          {spinner('claim', 'hand')}
          Preia task-ul
        </button>
      </nav>
    )
  }
  if (!canWork) return null
  if (task.state === 'completed') {
    return (
      <nav className="mail-toolbar" aria-label="Acțiuni">
        <button type="button" className="button ghost" onClick={onReopen} disabled={busy !== null}>
          {spinner('open', 'inbox')}
          Redeschide
        </button>
      </nav>
    )
  }
  return (
    <nav className="mail-toolbar" aria-label="Acțiuni">
      <button type="button" className="button primary" onClick={onReply} disabled={busy !== null}>
        <Icon name="reply" />
        Răspunde
      </button>
      {canDistribute(task) && (
        <button type="button" className="button ghost" onClick={onDistribute} disabled={busy !== null}>
          <Icon name="users" />
          Distribuie
        </button>
      )}
      {holds && (
        <button type="button" className="button ghost" onClick={onForward} disabled={busy !== null}>
          <Icon name="forward" />
          Direcționează
        </button>
      )}
      <span className="mail-toolbar-sep" aria-hidden="true" />
      {task.state === 'waiting' ? (
        <>
          <button type="button" className="button ghost" onClick={onResume} disabled={busy !== null}>
            {spinner('open', 'inbox')}
            Redeschide
          </button>
          <button type="button" className="button ghost" onClick={onWait} disabled={busy !== null} aria-expanded={waitOpen}>
            <Icon name="edit" />
            Schimbă motivul
          </button>
        </>
      ) : (
        <button type="button" className="button ghost" onClick={onWait} disabled={busy !== null} aria-expanded={waitOpen}>
          <Icon name="clock" />
          Pune în așteptare
        </button>
      )}
      {status && <span className="mail-toolbar-status">{status}</span>}
    </nav>
  )
}

/** Why the task waits: required, shown to everyone who follows the task. */
function WaitForm({
  task,
  busy,
  onSubmit,
  onCancel,
}: {
  task: TaskItem
  busy: boolean
  onSubmit: (reason: string) => Promise<boolean>
  onCancel: () => void
}) {
  const [reason, setReason] = useState(task.state === 'waiting' ? (task.waitingReason ?? '') : '')
  const [error, setError] = useState<string | null>(null)
  return (
    <form
      className="wait-form"
      onSubmit={async (e) => {
        e.preventDefault()
        const text = reason.trim()
        const invalid = text ? validateNotes(text, 1000) : 'Scrie ce aștepți: rămâne vizibil pe task.'
        if (invalid) {
          setError(invalid)
          return
        }
        await onSubmit(text)
      }}
      noValidate
    >
      <label className="field">
        <span>
          <Icon name="clock" size={14} /> Ce aștepți?
        </span>
        <textarea
          value={reason}
          onChange={(e) => {
            setError(null)
            setReason(e.target.value)
          }}
          rows={2}
          maxLength={1000}
          placeholder="De exemplu: răspunsul clientului la ofertă."
          autoFocus
        />
      </label>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="wait-actions">
        <button type="submit" className="button primary small" disabled={busy}>
          {busy ? <span className="spinner" aria-hidden="true" /> : <Icon name="clock" />}
          {task.state === 'waiting' ? 'Salvează motivul' : 'Pune în așteptare'}
        </button>
        <button type="button" className="button ghost small" onClick={onCancel} disabled={busy}>
          Anulează
        </button>
      </div>
    </form>
  )
}

