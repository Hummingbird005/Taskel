import { Fragment, useState, type ReactNode } from 'react'
import { attachmentsApi, previewKind, type Attachment } from '../api/attachments'
import type { TaskItem } from '../api/tasks'
import { formatMailTime } from '../lib/dates'
import { formatBytes } from '../lib/text'
import { foldedKeys, type Party, type ThreadItem, type ThreadMessage } from '../lib/thread'
import { LIMITS, validateNotes } from '../lib/validation'
import AttachmentViewer from './AttachmentViewer'
import Icon from './Icon'

interface Props {
  task: TaskItem
  items: ThreadItem[]
  currentUserId?: string
  /** Where this reader downloads the task's files from, when not through the task itself. */
  attachmentUrl?: (attachment: Attachment) => string
  /** Sends one answered copy back with a message; left out when the reader cannot. */
  onSendBack?: (copyId: string, message: string) => Promise<string | null>
}

const AVATAR_COLOURS = ['#7c3aed', '#0e7490', '#c2410c', '#1d4ed8', '#047857', '#be185d', '#4d7c0f', '#9333ea']

/** Two letters and a colour that stays the same for the same person. */
function avatar(party: Party): { letters: string; colour: string } {
  const words = party.name.replace(/[<>@].*$/, '').trim().split(/\s+/).filter(Boolean)
  const letters = ((words[0]?.[0] ?? '?') + (words.length > 1 ? words[words.length - 1][0] : (words[0]?.[1] ?? ''))).toUpperCase()
  let hash = 0
  for (const ch of party.id ?? party.email ?? party.name) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0
  return { letters, colour: AVATAR_COLOURS[hash % AVATAR_COLOURS.length] }
}

function Avatar({ party, small = false }: { party: Party; small?: boolean }) {
  const { letters, colour } = avatar(party)
  return (
    <span className={`avatar${small ? ' small' : ''}`} style={{ background: colour }} aria-hidden="true">
      {letters}
    </span>
  )
}

/** "Tu" for the reader, the name otherwise. */
const nameOf = (party: Party, me?: string) => (me && party.id === me ? 'Tu' : party.name)
const toText = (to: Party[], me?: string) =>
  to.map((p) => (me && p.id === me ? 'tine' : p.name)).join(', ')

/** The kind of a file, as a small coloured label. */
function fileKind(name: string): { label: string; colour: string } {
  const ext = name.split('.').pop()?.toLowerCase() ?? ''
  if (ext === 'pdf') return { label: 'PDF', colour: '#b42318' }
  if (['xls', 'xlsx', 'csv'].includes(ext)) return { label: 'XLS', colour: '#067647' }
  if (['doc', 'docx', 'rtf', 'odt'].includes(ext)) return { label: 'DOC', colour: '#1d4ed8' }
  if (['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp'].includes(ext)) return { label: 'IMG', colour: '#7c3aed' }
  if (['zip', 'rar', '7z'].includes(ext)) return { label: 'ZIP', colour: '#55555c' }
  return { label: ext.slice(0, 4).toUpperCase() || 'FIȘ', colour: '#55555c' }
}

/** Files as tiles: pictures and PDFs open over the task, the rest download. */
export function FileTiles({
  files,
  urlOf,
  onView,
  onRemove,
}: {
  files: Attachment[]
  urlOf: (file: Attachment) => string
  onView: (file: Attachment) => void
  /** Offered in the reply being written, to leave a file out. */
  onRemove?: (file: Attachment) => void
}) {
  if (files.length === 0) return null
  return (
    <ul className="file-tiles">
      {files.map((file) => {
        const kind = fileKind(file.fileName)
        const viewable = previewKind(file.fileName) !== null
        const body = (
          <>
            <span className="file-kind" style={{ background: kind.colour }} aria-hidden="true">
              {kind.label}
            </span>
            <span className="file-text">
              <span className="file-name">{file.fileName}</span>
              <small>
                {formatBytes(file.size)} · {viewable ? 'Previzualizează' : 'Descarcă'}
              </small>
            </span>
          </>
        )
        return (
          <li key={file.id} className="file-tile">
            {viewable ? (
              <button type="button" onClick={() => onView(file)} title={`Deschide ${file.fileName}`}>
                {body}
              </button>
            ) : (
              <a href={urlOf(file)} download={file.fileName} title={`Descarcă ${file.fileName}`}>
                {body}
              </a>
            )}
            {onRemove && (
              <button type="button" className="file-remove" onClick={() => onRemove(file)} aria-label={`Nu trimite ${file.fileName}`} title="Nu-l trimite">
                <Icon name="close" size={13} />
              </button>
            )}
          </li>
        )
      })}
    </ul>
  )
}

/**
 * The task as a mail thread: the email or message it started from, then every message, answer and event, oldest
 * first. Older messages fold to one line, and long runs of them to "N mesaje mai vechi".
 */
export default function TaskThread({ task, items, currentUserId, attachmentUrl, onSendBack }: Props) {
  const [opened, setOpened] = useState<Set<string>>(new Set())
  const [showAll, setShowAll] = useState(false)
  const [viewing, setViewing] = useState<Attachment | null>(null)
  const urlOf = (file: Attachment) => (attachmentUrl ? attachmentUrl(file) : attachmentsApi.downloadUrl(task.id, file.id))
  const folded = foldedKeys(items)
  const isFolded = (item: ThreadItem) => item.type === 'message' && folded.has(item.key) && !opened.has(item.key)

  // Runs of three or more folded messages (events between them included) collapse into one line.
  const rendered: ReactNode[] = []
  for (let i = 0; i < items.length; i++) {
    const item = items[i]
    if (!showAll && isFolded(item)) {
      let end = i
      let count = 0
      while (end < items.length && (isFolded(items[end]) || items[end].type === 'event')) {
        if (items[end].type === 'message') count++
        end++
      }
      // Events at the end of a run stay visible after it.
      while (end > i && items[end - 1].type === 'event') end--
      if (count >= 3) {
        rendered.push(
          <li key={`stack-${item.key}`}>
            <button type="button" className="thread-stack" onClick={() => setShowAll(true)}>
              {count} mesaje mai vechi · Arată-le
            </button>
          </li>,
        )
        i = end - 1
        continue
      }
    }
    rendered.push(
      <Fragment key={item.key}>
        {item.type === 'event' ? (
          <li className="thread-event">
            <span>
              <Icon name={item.icon} size={15} />
              <span>
                {item.who && <b>{nameOf(item.who, currentUserId)}</b>} {item.what}
                {item.at && <span className="thread-event-time"> · {formatMailTime(item.at)}</span>}
              </span>
            </span>
            {item.text && <p>{item.text}</p>}
          </li>
        ) : isFolded(item) ? (
          <li>
            <button
              type="button"
              className="mail-card folded"
              onClick={() => setOpened((prev) => new Set(prev).add(item.key))}
              aria-label={`Deschide mesajul de la ${nameOf(item.from, currentUserId)}`}
            >
              <Avatar party={item.from} small />
              <b>
                {nameOf(item.from, currentUserId)}
                {item.to.length > 0 && <> → {toText(item.to, currentUserId)}</>}
              </b>
              <span className="mail-snippet">{item.text ?? ''}</span>
              {item.files.length > 0 && <Icon name="paperclip" size={14} />}
              <time dateTime={item.at}>{formatMailTime(item.at)}</time>
            </button>
          </li>
        ) : (
          <li>
            <MailCard
              message={item}
              currentUserId={currentUserId}
              urlOf={urlOf}
              onView={setViewing}
              onSendBack={onSendBack}
            />
          </li>
        )}
      </Fragment>,
    )
  }

  return (
    <>
      <ol className="mail-thread" aria-label="Firul task-ului">
        {rendered}
      </ol>
      {viewing && <AttachmentViewer files={[viewing]} start={0} urlOf={urlOf} onClose={() => setViewing(null)} />}
    </>
  )
}

function MailCard({
  message,
  currentUserId,
  urlOf,
  onView,
  onSendBack,
}: {
  message: ThreadMessage
  currentUserId?: string
  urlOf: (file: Attachment) => string
  onView: (file: Attachment) => void
  onSendBack?: (copyId: string, message: string) => Promise<string | null>
}) {
  const [sendingBack, setSendingBack] = useState(false)
  const to = toText(message.to, currentUserId)
  return (
    <article className={`mail-card${message.original ? ' original' : ''}`}>
      <header className="mail-card-head">
        <Avatar party={message.from} />
        <div className="mail-who">
          <div>
            <b>{nameOf(message.from, currentUserId)}</b>
            {message.from.email && message.from.email !== message.from.name && (
              <span className="muted"> &lt;{message.from.email}&gt;</span>
            )}
            {message.badge && <span className={`mail-badge ${message.badge.tone}`}>{message.badge.text}</span>}
          </div>
          {to && <div className="mail-to">către {to}</div>}
        </div>
        <time dateTime={message.shownAt ?? message.at}>{formatMailTime(message.shownAt ?? message.at)}</time>
      </header>
      {message.text ? (
        <div className="mail-text">{message.text}</div>
      ) : (
        message.original && <p className="mail-text muted">Fără mesaj.</p>
      )}
      <div className="mail-files">
        <FileTiles files={message.files} urlOf={urlOf} onView={onView} />
      </div>
      {message.sendBack && onSendBack && !sendingBack && (
        <div className="mail-actions">
          <button type="button" className="button ghost small" onClick={() => setSendingBack(true)}>
            <Icon name="reply" />
            Revino
          </button>
        </div>
      )}
      {message.sendBack && onSendBack && sendingBack && (
        <SendBackForm holder={message.sendBack.holder} onCancel={() => setSendingBack(false)}
          onSubmit={async (text) => {
            const failure = await onSendBack(message.sendBack!.copyId, text)
            if (!failure) setSendingBack(false)
            return failure
          }}
        />
      )}
    </article>
  )
}

/** What is missing from an answer: the task opens again for whoever answered. */
function SendBackForm({ holder, onSubmit, onCancel }: { holder: string; onSubmit: (text: string) => Promise<string | null>; onCancel: () => void }) {
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  return (
    <form
      className="send-back mail-actions"
      onSubmit={async (e) => {
        e.preventDefault()
        const message = text.trim()
        const invalid = message ? validateNotes(message, LIMITS.response) : 'Scrie ce lipsește din răspuns.'
        if (invalid) {
          setError(invalid)
          return
        }
        setBusy(true)
        const failure = await onSubmit(message)
        setBusy(false)
        setError(failure)
      }}
      noValidate
    >
      <textarea
        value={text}
        onChange={(e) => {
          setError(null)
          setText(e.target.value)
        }}
        rows={2}
        maxLength={LIMITS.response}
        placeholder={`Ce mai trebuie? Task-ul se redeschide la ${holder}.`}
        aria-label={`Mesaj pentru ${holder}`}
        autoFocus
      />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="send-back-actions">
        <button type="submit" className="button primary small" disabled={busy}>
          {busy ? <span className="spinner" aria-hidden="true" /> : <Icon name="reply" />}
          Trimite înapoi
        </button>
        <button type="button" className="button ghost small" onClick={onCancel} disabled={busy}>
          Anulează
        </button>
      </div>
    </form>
  )
}
