import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { ApiError } from '../api/client'
import { attachmentsApi, previewKind, type Attachment } from '../api/attachments'
import { tasksApi, type TaskItem, type TaskMessage } from '../api/tasks'
import AttachmentViewer from './AttachmentViewer'
import { LIMITS, validateNotes } from '../lib/validation'
import Icon, { type IconName } from './Icon'

interface Props {
  task: TaskItem
  currentUserId?: string
  /** Whether the person may write: those who work on the task. */
  canWrite: boolean
  onSessionExpired?: () => void
}

/** One line of the conversation: a message, or something that happened to the task. */
interface Entry {
  key: string
  at: string
  /** Who did it; null for what Taskel records by itself. */
  who: string | null
  /** What happened, after the name: "a distribuit task-ul către Andrei". Empty for a plain message. */
  what: string
  text: string | null
  /** Files that came with an answer. */
  files?: Attachment[]
  icon: IconName
  /** Written by the person looking: shown on the right. */
  mine: boolean
  /** Not a message but a fact: shown small, in the middle. */
  event: boolean
}

const TIME = new Intl.DateTimeFormat('ro-RO', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/** What a recorded message says, from where the person looking stands. */
function describe(message: TaskMessage, onCopy: boolean): Pick<Entry, 'what' | 'icon' | 'event'> {
  const to = message.related?.recipient ?? ''
  switch (message.kind) {
    case 'distributed':
      return { what: onCopy ? 'ți-a distribuit task-ul' : `a distribuit task-ul către ${to}`, icon: 'users', event: false }
    case 'answered':
      return { what: 'a răspuns', icon: 'checkCircle', event: false }
    case 'allAnswered':
      return { what: 'Au răspuns toți. Task-ul e din nou deschis.', icon: 'checkCircle', event: true }
    case 'sentBack':
      return { what: onCopy ? 'ți-a trimis task-ul înapoi' : `i-a trimis înapoi lui ${to}`, icon: 'reply', event: false }
    case 'closed':
      return { what: onCopy ? 'a închis task-ul' : `a închis task-ul lui ${to}`, icon: 'close', event: true }
    default:
      // A recipient writing on their own copy is not writing "to" themselves.
      return {
        what: !onCopy && message.related && message.author?.displayName !== to ? `către ${to}` : '',
        icon: 'mail',
        event: false,
      }
  }
}

/** "a distribuit" becomes "ai distribuit" after "Tu". */
function secondPerson(what: string): string {
  return what.replace(/^a /, 'ai ').replace(/^i-a /, 'i-ai ')
}

/** The task's own history, from its fields: created, forwarded, sent back, completed. */
function history(task: TaskItem, currentUserId?: string): Entry[] {
  // A copy starts with the distribution itself, already in the conversation.
  const entries: Entry[] = task.distributedFrom ? [] : [
    {
      key: 'created',
      at: task.createdAt,
      who: task.createdBy.displayName,
      what: task.sourceEmailAddress
          ? `a adus task-ul din emailul lui ${task.sourceEmailAddress}`
          : 'a creat task-ul',
      text: null,
      icon: 'plus',
      mine: false,
      event: true,
    },
  ]
  task.forwards.forEach((step, i) => {
    const to = step.toUser?.displayName ?? (step.toGroup ? `grupul ${step.toGroup.name}` : '—')
    entries.push({
      key: `forward-${i}`,
      at: step.at,
      who: step.from.displayName,
      what: step.isReturn ? `a rezolvat și l-a trimis înapoi la ${to}` : `a direcționat task-ul către ${to}`,
      text: step.note,
      icon: step.isReturn ? 'reply' : 'forward',
      mine: step.from.id === currentUserId,
      event: !step.note,
    })
  })
  // A copy's own completion is the answer, already in the conversation.
  if (task.completedAt && !task.distributedFrom) {
    entries.push({
      key: 'completed',
      at: task.completedAt,
      who: null,
      what: 'Task finalizat',
      text: task.completionResponse,
      icon: 'check',
      mine: false,
      event: !task.completionResponse,
    })
  }
  return entries
}

/**
 * Everything that was said and done on a task, in order: how it started, the forwards, the distribution and the
 * answers, the messages, and how it ended. Those who work on the task can write; on a distributed task, to one
 * person's exchange or as a note on the task.
 */
export default function Conversation({ task, currentUserId, canWrite, onSessionExpired }: Props) {
  const [messages, setMessages] = useState<TaskMessage[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [text, setText] = useState('')
  const [to, setTo] = useState(task.distributions?.length === 1 ? task.distributions[0].taskId : '')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [viewing, setViewing] = useState<Attachment | null>(null)
  const end = useRef<HTMLLIElement>(null)
  const urlOf = (file: Attachment) => attachmentsApi.downloadUrl(task.id, file.id)

  // Read again whenever the task changes: an answer, a send-back or a new distribution adds to the conversation.
  const version = `${task.state}|${task.completedAt}|${task.distributions?.map((d) => d.state).join()}`
  const expired = useRef(onSessionExpired)
  useEffect(() => {
    expired.current = onSessionExpired
  })
  useEffect(() => {
    let cancelled = false
    tasksApi
      .conversation(task.id)
      .then((list) => {
        if (cancelled) return
        setMessages(list)
        setFailed(false)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) expired.current?.()
        else setFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [task.id, version])

  useEffect(() => {
    end.current?.scrollIntoView({ block: 'nearest' })
  }, [messages])

  const onCopy = Boolean(task.distributedFrom)
  const entries = [
    ...history(task, currentUserId),
    ...(messages ?? []).map<Entry>((m) => ({
      key: m.id,
      at: m.at,
      who: m.author?.displayName ?? null,
      text: m.text,
      files: m.attachments ?? undefined,
      mine: Boolean(m.author && m.author.id === currentUserId),
      ...describe(m, onCopy),
    })),
  ].sort((a, b) => a.at.localeCompare(b.at))

  async function send(e?: FormEvent) {
    e?.preventDefault()
    const message = text.trim()
    if (!message || sending) return
    const invalid = validateNotes(message, LIMITS.response)
    if (invalid) {
      setError(invalid)
      return
    }
    setSending(true)
    try {
      const posted = await tasksApi.postMessage(task.id, message, to || null)
      setMessages((prev) => [...(prev ?? []), posted])
      setText('')
      setError(null)
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired?.()
      else setError('Mesajul nu a putut fi trimis. Încearcă din nou.')
    } finally {
      setSending(false)
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) send()
  }

  const copies = task.distributions ?? []
  const placeholder = onCopy
    ? `Scrie-i lui ${task.createdBy.displayName}…`
    : copies.length > 0 && to
      ? `Scrie-i lui ${copies.find((c) => c.taskId === to)?.assignee?.displayName ?? copies.find((c) => c.taskId === to)?.group?.name}…`
      : 'Scrie o notă pe task…'

  return (
    <section className="conversation" aria-label="Conversație">
      <h3>
        <Icon name="mail" size={15} />
        Conversație
      </h3>
      <ol className="chat">
        {entries.map((entry) => (
          <li key={entry.key} className={`chat-item${entry.mine ? ' mine' : ''}${entry.event ? ' event' : ''}`}>
            <div className="chat-meta">
              <Icon name={entry.icon} size={13} />
              <span>
                {entry.who && <strong>{entry.mine ? 'Tu' : entry.who}</strong>}
                {entry.who && entry.what ? ' ' : ''}
                {entry.mine ? secondPerson(entry.what) : entry.what}
              </span>
              <time dateTime={entry.at}>{TIME.format(new Date(entry.at))}</time>
            </div>
            {entry.text && <p className="chat-bubble">{entry.text}</p>}
            {entry.files && (
              <ul className="chat-files">
                {entry.files.map((file) => (
                  <li key={file.id}>
                    {previewKind(file.fileName) ? (
                      <button type="button" onClick={() => setViewing(file)} title={`Deschide ${file.fileName}`}>
                        <Icon name="eye" size={13} />
                        {file.fileName}
                      </button>
                    ) : (
                      <a href={urlOf(file)} download={file.fileName} title={`Descarcă ${file.fileName}`}>
                        <Icon name="paperclip" size={13} />
                        {file.fileName}
                      </a>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
        {messages === null && !failed && <li className="chat-item event muted">Se încarcă…</li>}
        {failed && <li className="chat-item event error">Conversația nu a putut fi încărcată.</li>}
        <li ref={end} aria-hidden="true" />
      </ol>

      {viewing && <AttachmentViewer files={[viewing]} start={0} urlOf={urlOf} onClose={() => setViewing(null)} />}
      {canWrite && (
        <form className="chat-compose" onSubmit={send} noValidate>
          {!onCopy && copies.length > 0 && (
            <select value={to} onChange={(e) => setTo(e.target.value)} aria-label="Către">
              <option value="">Notă pe task</option>
              {copies.map((c) => (
                <option key={c.taskId} value={c.taskId}>
                  Către {c.assignee?.displayName ?? c.group?.name}
                </option>
              ))}
            </select>
          )}
          <div className="chat-input">
            <textarea
              value={text}
              onChange={(e) => {
                setError(null)
                setText(e.target.value)
              }}
              onKeyDown={onKeyDown}
              rows={2}
              maxLength={LIMITS.response}
              placeholder={placeholder}
              aria-label="Mesaj"
            />
            <button type="submit" className="button primary" disabled={sending || !text.trim()} aria-label="Trimite mesajul" title="Trimite (Ctrl+Enter)">
              {sending ? <span className="spinner" aria-hidden="true" /> : <Icon name="send" />}
            </button>
          </div>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
        </form>
      )}
    </section>
  )
}
