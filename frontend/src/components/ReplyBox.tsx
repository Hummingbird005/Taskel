import { forwardRef, useEffect, useRef, useState } from 'react'
import { attachmentsApi, type Attachment } from '../api/attachments'
import type { TaskItem } from '../api/tasks'
import { replyTargets, type ReplyTarget } from '../lib/replyTargets'
import { stateChangeProblem } from '../lib/taskStates'
import { LIMITS, validateNotes } from '../lib/validation'
import Icon from './Icon'
import AttachmentViewer from './AttachmentViewer'
import { FileTiles } from './TaskThread'

/** What completing does, said on its button: send the task back, email the answer, or just complete it. */
function completeLabel(task: TaskItem, target: ReplyTarget): string {
  if (target.kind === 'note') return 'Finalizează'
  if (task.returnsTo) return `Trimite rezolvarea, înapoi la ${task.returnsTo.displayName}`
  return 'Trimite și finalizează'
}

interface Props {
  task: TaskItem
  /** Files uploaded to go with the answer. */
  answerFiles: Attachment[]
  uploading: string | null
  /** The action under way: 'completed', 'send', 'note'. */
  busy: string | null
  error: string | null
  onError: (error: string | null) => void
  onAddFiles: (files: File[]) => void
  onDropFile: (id: string) => void
  /** Completes the task with this answer (or resolution, or none). */
  onComplete: (text: string) => Promise<boolean>
  /** Sends the answer and keeps the task open. */
  onSend: (text: string) => Promise<boolean>
  /** Writes to one of the people the task was distributed to (`copyId`), or a note on the task (null). */
  onMessage: (text: string, copyId: string | null) => Promise<boolean>
  onCancel?: () => void
}

/**
 * The reply, written at the end of the thread as in a mail app: who it goes to, the text, the files, and a button
 * that sends it and completes the task, or only sends it.
 */
const ReplyBox = forwardRef<HTMLTextAreaElement, Props>(function ReplyBox(
  { task, answerFiles, uploading, busy, error, onError, onAddFiles, onDropFile, onComplete, onSend, onMessage },
  ref,
) {
  const targets = replyTargets(task)
  const [targetKey, setTargetKey] = useState(targets[0]?.key ?? 'note')
  const target = targets.find((t) => t.key === targetKey) ?? targets[0]
  const [text, setText] = useState('')
  const [menu, setMenu] = useState(false)
  const [viewing, setViewing] = useState<Attachment | null>(null)
  const picker = useRef<HTMLInputElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!menu) return
    const close = (e: MouseEvent) => {
      if (!menuRef.current?.contains(e.target as Node)) setMenu(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [menu])

  if (!target) return null
  const withFiles = target.kind === 'reply'

  function check(message: string, required: boolean): boolean {
    const problem = validateNotes(message, LIMITS.response) ?? (required && !message ? 'Scrie mesajul.' : null)
    onError(problem)
    return !problem
  }

  async function complete() {
    setMenu(false)
    const message = text.trim()
    const problem = validateNotes(message, LIMITS.response) ?? stateChangeProblem(task, 'completed', message)
    if (problem) {
      onError(problem)
      return
    }
    if (await onComplete(message)) setText('')
  }

  async function sendOnly() {
    setMenu(false)
    const message = text.trim()
    if (!check(message, true)) return
    if (await onSend(message)) setText('')
  }

  async function writeMessage() {
    setMenu(false)
    const message = text.trim()
    if (!check(message, true)) return
    if (await onMessage(message, target.kind === 'copy' ? target.key.slice('copy:'.length) : null)) setText('')
  }

  const placeholder =
    target.kind === 'note'
      ? 'Scrie o notă, sau cum s-a rezolvat task-ul, apoi Finalizează.'
      : target.kind === 'copy'
        ? `Scrie-i lui ${target.label}…`
        : `Scrie răspunsul pentru ${target.label}…`

  return (
    <section className="reply-box" aria-label="Răspuns">
      <div className="reply-to">
        <span className="muted">Către</span>
        {targets.length > 1 ? (
          <select
            value={target.key}
            onChange={(e) => {
              onError(null)
              setTargetKey(e.target.value)
            }}
            aria-label="Către"
          >
            {targets.map((t) => (
              <option key={t.key} value={t.key}>
                {t.label}
              </option>
            ))}
          </select>
        ) : (
          <span className="reply-chip">{target.label}</span>
        )}
        <span className="reply-hint">{target.hint}</span>
      </div>
      <textarea
        ref={ref}
        value={text}
        onChange={(e) => {
          onError(null)
          setText(e.target.value)
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            if (target.kind === 'reply') complete()
            else if (target.kind === 'copy') writeMessage()
          }
        }}
        rows={4}
        maxLength={LIMITS.response}
        placeholder={placeholder}
        aria-label="Mesajul"
      />
      {withFiles && (answerFiles.length > 0 || uploading) && (
        <div className="reply-files">
          <FileTiles
            files={answerFiles}
            urlOf={(file) => attachmentsApi.downloadUrl(task.id, file.id)}
            onView={setViewing}
            onRemove={(file) => onDropFile(file.id)}
          />
          {uploading && (
            <span className="muted reply-uploading">
              <span className="spinner" aria-hidden="true" />
              {uploading}
            </span>
          )}
        </div>
      )}
      {error && (
        <p className="error reply-error" role="alert">
          {error}
        </p>
      )}
      <div className="reply-foot">
        {target.kind === 'copy' ? (
          <button type="button" className="button primary" onClick={writeMessage} disabled={busy !== null}>
            {busy === 'note' ? <span className="spinner" aria-hidden="true" /> : <Icon name="send" />}
            Trimite mesajul
          </button>
        ) : (
          <div className="split-button" ref={menuRef}>
            <button type="button" className="button go" onClick={complete} disabled={busy !== null}>
              {busy === 'completed' ? <span className="spinner" aria-hidden="true" /> : <Icon name="check" />}
              {completeLabel(task, target)}
            </button>
            <button
              type="button"
              className="button go split-toggle"
              onClick={() => setMenu((m) => !m)}
              disabled={busy !== null}
              aria-label="Alte variante de trimitere"
              aria-expanded={menu}
            >
              {busy === 'send' || busy === 'note' ? <span className="spinner" aria-hidden="true" /> : <Icon name="chevronDown" />}
            </button>
            {menu && (
              <div className="split-menu" role="menu">
                {target.kind === 'reply' ? (
                  <button type="button" role="menuitem" onClick={sendOnly}>
                    <Icon name="send" />
                    <span>
                      Doar trimite
                      <small>Task-ul rămâne deschis</small>
                    </span>
                  </button>
                ) : (
                  <button type="button" role="menuitem" onClick={writeMessage}>
                    <Icon name="edit" />
                    <span>
                      Adaugă doar nota
                      <small>Task-ul rămâne deschis</small>
                    </span>
                  </button>
                )}
              </div>
            )}
          </div>
        )}
        {target.kind === 'reply' && <span className="reply-note muted">sau, din săgeată, doar Trimite</span>}
        {withFiles && (
          <span className="reply-tools">
            <input
              ref={picker}
              type="file"
              multiple
              hidden
              onChange={(e) => {
                const picked = [...(e.target.files ?? [])]
                e.target.value = ''
                onAddFiles(picked)
              }}
            />
            <button
              type="button"
              className="icon-button"
              onClick={() => picker.current?.click()}
              disabled={busy !== null || uploading !== null}
              title="Atașează la răspuns"
              aria-label="Atașează la răspuns"
            >
              <Icon name="paperclip" />
            </button>
          </span>
        )}
      </div>
      {viewing && (
        <AttachmentViewer files={[viewing]} start={0} urlOf={(file) => attachmentsApi.downloadUrl(task.id, file.id)} onClose={() => setViewing(null)} />
      )}
    </section>
  )
})

export default ReplyBox
