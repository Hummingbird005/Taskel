/**
 * What the workspace does with a task: each change goes to the server, the list follows its
 * answer, and a refusal becomes a sentence in Romanian (the server's own messages are in English).
 * Kept apart from the component so each step stays small and readable.
 */
import { attachmentsApi, uploadErrorText, type AttachmentLimits } from '../api/attachments'
import { ApiError } from '../api/client'
import { tasksApi, type TaskDraft, type TaskItem, type TaskTarget } from '../api/tasks'
import type { TaskActions } from '../components/TaskDetail'
import { openCopies } from './taskStates'

/** The workspace state the actions read and change. */
export interface TaskActionContext {
  setTasks: (update: (prev: TaskItem[]) => TaskItem[]) => void
  setNotice: (notice: string | null) => void
  setError: (error: string | null) => void
  closeTask: () => void
  /** Reloads tasks and groups, for changes the page cannot work out by itself. */
  refresh: () => Promise<void>
  onSessionExpired: () => void
}

type Explain = (err: ApiError) => string | null

const replaceIn = (context: TaskActionContext, updated: TaskItem) =>
  context.setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))

/** What to tell the user when completing an email task with a reply failed. */
export function replyErrorText(err: unknown): string {
  if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel. Task-ul nu a fost finalizat.'
  switch (err.status) {
    case 503:
      return 'Trimiterea de emailuri nu este configurată pe serverul Taskel, deci răspunsul nu poate fi trimis. Task-ul nu a fost finalizat.'
    case 502:
      return 'Emailul de răspuns nu a putut fi trimis. Task-ul nu a fost finalizat. Încearcă din nou.'
    case 409:
      return 'Task-ul este deja finalizat.'
    case 400:
      return 'Răspunsul este obligatoriu și poate avea cel mult 4000 de caractere.'
    default:
      return 'A apărut o eroare. Task-ul nu a fost finalizat.'
  }
}

/** A refused change in words: the action's own explanation first, then the common cases. */
function refusalText(err: ApiError, fallback: string, explain?: Explain): string {
  const explained = explain?.(err)
  if (explained) return explained
  if (err.status === 404) return 'Task-ul nu mai există sau nu mai ai acces la el.'
  if (err.status === 403) return 'Nu mai poți schimba acest task: nu mai este la tine.'
  return fallback
}

/**
 * Runs a change and keeps the list up to date with the server's answer. Resolves to an error
 * message for the dialog to show, or null when it worked (or the session ended).
 */
export async function attempt(
  context: TaskActionContext,
  change: () => Promise<TaskItem | void>,
  fallback: string,
  explain?: Explain,
): Promise<string | null> {
  try {
    const updated = await change()
    if (updated) replaceIn(context, updated)
    context.setError(null)
    return null
  } catch (err) {
    if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel. Încearcă din nou.'
    if (err.status !== 401) return refusalText(err, fallback, explain)
    context.onSessionExpired()
    return null
  }
}

/** Who has the task now, as the form shows it: a group task someone took shows that person. */
function currentTarget(task: TaskItem): TaskTarget {
  if (task.assignee) return { assigneeId: task.assignee.id, groupId: null }
  return { assigneeId: null, groupId: task.group?.id ?? null }
}

/**
 * Saves the task's fields, then who it goes to, only when that changed: reassigning is its own
 * request, which only the creator may make.
 */
async function saveTask(task: TaskItem, draft: TaskDraft): Promise<TaskItem> {
  const updated = await tasksApi.update({ ...task, ...draft })
  if (draft.assigneeId === undefined) return updated
  const next: TaskTarget = { assigneeId: draft.assigneeId, groupId: draft.groupId ?? null }
  const current = currentTarget(task)
  const changed = next.assigneeId !== current.assigneeId || next.groupId !== current.groupId
  return changed ? tasksApi.assign(task.id, next) : updated
}

async function completeTask(context: TaskActionContext, task: TaskItem, response: string | null): Promise<string | null> {
  // Closing a distributed task before everyone answered closes their tasks too: asked first.
  const waitingFor = openCopies(task)
  if (
    waitingFor > 0 &&
    !window.confirm(
      waitingFor === 1
        ? 'O persoană nu a răspuns încă. Închizi task-ul și task-ul ei?'
        : `${waitingFor} persoane nu au răspuns încă. Închizi task-ul și task-urile lor?`,
    )
  ) {
    return null
  }
  try {
    replaceIn(context, await tasksApi.complete(task.id, response, waitingFor > 0))
    context.setError(null)
    if (waitingFor > 0) await context.refresh()
    if (task.answersTo) {
      context.setNotice(`Răspuns trimis lui ${task.answersTo.displayName}.`)
      return null
    }
    if (task.returnsTo) {
      // The task is no longer the caller's: it went back to whoever asked for it back.
      context.closeTask()
      context.setNotice(`Task trimis înapoi la ${task.returnsTo.displayName}. Îl poți urmări la „Direcționate”.`)
    }
    return null
  } catch (err) {
    if (!(err instanceof ApiError && err.status === 401)) return replyErrorText(err)
    context.onSessionExpired()
    return null
  }
}

function distributeRefusal(err: ApiError): string | null {
  if (err.status === 400) return err.message.includes('open copy')
    ? 'Cineva dintre cei aleși are deja task-ul, încă deschis. Scrie-i în conversație.'
    : 'Una dintre persoanele sau grupurile alese nu mai există.'
  if (err.status === 403) return 'Doar cine răspunde de task îl poate distribui.'
  return err.status === 409 ? 'Task-ul este finalizat. Redeschide-l mai întâi.' : null
}

async function distributeTask(
  context: TaskActionContext,
  task: TaskItem,
  recipients: string[],
  message: string | null,
  dueDate: string | null,
) {
  const failure = await attempt(
    context,
    () => tasksApi.distribute(task.id, recipients, message, dueDate),
    'Nu am putut distribui task-ul. Încearcă din nou.',
    distributeRefusal,
  )
  if (failure) return failure
  context.setNotice(
    recipients.length === 1
      ? 'Task distribuit. Răspunsul apare în conversația task-ului.'
      : `Task distribuit la ${recipients.length} destinatari. Răspunsurile apar în conversația task-ului.`,
  )
  // The copies are the user's own tasks, given to others: they appear under "Delegate".
  await context.refresh()
  return null
}

function waitRefusal(err: ApiError): string | null {
  if (err.status === 400) return 'Motivul este obligatoriu și poate avea cel mult 1000 de caractere.'
  return err.status === 409 ? 'Task-ul este finalizat. Redeschide-l mai întâi.' : null
}

function forwardRefusal(err: ApiError): string | null {
  if (err.status === 400) return 'Persoana sau grupul ales nu mai există. Alege altceva.'
  return err.status === 403 || err.status === 404 ? 'Task-ul nu mai este la tine, deci nu îl mai poți direcționa.' : null
}

async function forwardTask(
  context: TaskActionContext,
  task: TaskItem,
  target: TaskTarget,
  note: string | null,
  returnToMe: boolean,
) {
  const failure = await attempt(
    context,
    () => tasksApi.forward(task.id, target, note, returnToMe),
    'Nu am putut direcționa task-ul. Încearcă din nou.',
    forwardRefusal,
  )
  if (!failure) context.setNotice('Task direcționat. Îl poți urmări la „Direcționate”.')
  return failure
}

async function releaseTask(context: TaskActionContext, task: TaskItem) {
  const failure = await attempt(context, () => tasksApi.release(task.id), 'Nu am putut da task-ul înapoi grupului.')
  if (failure) return failure
  context.closeTask()
  context.setNotice(task.group ? `„${task.title}” s-a întors la grupul ${task.group.name}.` : `„${task.title}” s-a întors la grup.`)
  // Whether the task is still visible depends on the user still being in the group.
  await context.refresh()
  return null
}

async function removeTask(context: TaskActionContext, task: TaskItem) {
  const failure = await attempt(
    context,
    async () => {
      await tasksApi.remove(task.id)
      context.setTasks((prev) => prev.filter((t) => t.id !== task.id))
    },
    'Nu am putut șterge task-ul.',
    (err) => (err.status === 403 ? 'Doar persoana care a creat task-ul îl poate șterge.' : null),
  )
  if (!failure) context.setNotice(`Task șters: „${task.title}”.`)
  return failure
}

/** Takes a task waiting on one of the user's groups, from its row or from its dialog. */
export async function claimTask(context: TaskActionContext, task: TaskItem): Promise<string | null> {
  const failure = await attempt(context, () => tasksApi.claim(task.id), 'Nu am putut prelua task-ul.', (err) =>
    err.status === 404 || err.status === 409 ? 'Task-ul a fost preluat între timp de altcineva.' : null,
  )
  if (failure) {
    await context.refresh()
    return failure
  }
  context.setNotice(`Ai preluat „${task.title}”. Îl găsești la „Toate task-urile”.`)
  return null
}

/** Everything the task's dialog can do with it. */
export function taskActions(context: TaskActionContext, task: TaskItem): TaskActions {
  return {
    save: (draft) => attempt(context, () => saveTask(task, draft), 'Nu am putut salva task-ul.'),
    complete: (response) => completeTask(context, task, response),
    wait: (reason) => attempt(context, () => tasksApi.wait(task.id, reason), 'Nu am putut pune task-ul în așteptare.', waitRefusal),
    resume: () => attempt(context, () => tasksApi.resume(task.id), 'Nu am putut relua task-ul.'),
    reopen: () => attempt(context, () => tasksApi.update({ ...task, isCompleted: false }), 'Nu am putut redeschide task-ul.'),
    forward: (target, note, returnToMe) => forwardTask(context, task, target, note, returnToMe),
    distribute: (recipients, message, dueDate) => distributeTask(context, task, recipients, message, dueDate),
    sendBack: (copyId, message) =>
      attempt(context, () => tasksApi.sendBack(task.id, copyId, message), 'Nu am putut trimite înapoi.', (err) =>
        err.status === 409 ? 'Persoana nu a răspuns încă, sau task-ul tău e finalizat.' : null,
      ),
    claim: () => claimTask(context, task),
    release: () => releaseTask(context, task),
    remove: () => removeTask(context, task),
    attachmentsChanged: (attachments) =>
      context.setTasks((prev) => prev.map((t) => (t.id === task.id ? { ...t, attachments } : t))),
  }
}

/** Where a new task went, said after the dialog closes: a task given to someone else leaves this view. */
function addedNotice(created: TaskItem): string {
  if (created.assignee) return `Task alocat către ${created.assignee.displayName}. Îl găsești la „Delegate”.`
  if (created.group) return `Task dat grupului ${created.group.name}. Îl găsești la „Delegate” până îl preia cineva.`
  return `Task adăugat: „${created.title}”.`
}

/**
 * Uploads the files of a task just created, one after the other. The task exists already: a file
 * that fails is reported and the rest are skipped; it can be added again on the task.
 */
async function uploadAll(context: TaskActionContext, task: TaskItem, files: File[], limits: AttachmentLimits): Promise<TaskItem> {
  let current = task
  for (const [index, file] of files.entries()) {
    const label = files.length > 1 ? ` (${index + 1} din ${files.length})` : ''
    const progress = (percent: string) => context.setNotice(`Se încarcă „${file.name}”${label}…${percent}`)
    progress('')
    try {
      const attachment = await attachmentsApi.upload(current.id, file, (fraction) => progress(` ${Math.round(fraction * 100)}%`))
      const withFile: TaskItem = { ...current, attachments: [...current.attachments, attachment] }
      current = withFile
      replaceIn(context, withFile)
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) throw err
      context.setError(`Task-ul a fost adăugat, dar fără toate fișierele. ${uploadErrorText(err, file.name, limits)}`)
      break
    }
  }
  return current
}

/** Creates a task with its files. Resolves to true when the task was created (the dialog then closes). */
export async function addTask(
  context: TaskActionContext,
  draft: TaskDraft,
  files: File[],
  limits: AttachmentLimits,
): Promise<boolean> {
  let created: TaskItem
  try {
    created = await tasksApi.create(draft)
  } catch (err) {
    if (err instanceof ApiError && err.status === 401) context.onSessionExpired()
    else context.setError('Nu am putut adăuga task-ul.')
    return false
  }
  context.setTasks((prev) => [...prev, created])
  context.setError(null)
  try {
    created = await uploadAll(context, created, files, limits)
  } catch {
    // Only a lost session gets here.
    context.onSessionExpired()
    return true
  }
  context.setNotice(addedNotice(created))
  return true
}
