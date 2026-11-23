import { useRef, useState } from 'react'
import { ApiError } from '../api/client'
import {
  attachmentsApi,
  checkFiles,
  previewKind,
  uploadErrorText,
  type Attachment,
  type AttachmentLimits,
} from '../api/attachments'
import type { TaskItem } from '../api/tasks'
import { formatBytes } from '../lib/text'
import AttachmentViewer from './AttachmentViewer'
import Icon from './Icon'

interface Props {
  task: TaskItem
  limits: AttachmentLimits
  /** Whether the person looking may add and remove files. Without it the list is read-only. */
  canEdit?: boolean
  /**
   * Show the section even though the task has no files yet, opened and ready to add one. The
   * row asks for this when its paperclip button is pressed; otherwise a task without files
   * shows nothing here.
   */
  showWhenEmpty?: boolean
  /** Start with the list unfolded, as in the task's detail. */
  defaultOpen?: boolean
  /** Called with the task's new list of files after one was added or removed. */
  onChange?: (attachments: Attachment[]) => void
  onSessionExpired?: () => void
  /** Builds the download address; the default goes through the task itself. */
  downloadUrl?: (attachment: Attachment) => string
}

/**
 * The files of a task: a list to open or download from and, for those who work on it, a way to add and remove.
 * PDF files and pictures open in a viewer over the task; other files are downloaded.
 */
export default function TaskAttachments({
  task,
  limits,
  canEdit = false,
  showWhenEmpty = false,
  defaultOpen = false,
  onChange,
  onSessionExpired,
  downloadUrl,
}: Props) {
  const input = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState<{ name: string; fraction: number } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState(showWhenEmpty || defaultOpen)
  const [viewing, setViewing] = useState<number | null>(null)
  const files = task.attachments
  const viewable = files.filter((file) => previewKind(file.fileName) !== null)
  const urlOf = (file: Attachment) => (downloadUrl ? downloadUrl(file) : attachmentsApi.downloadUrl(task.id, file.id))

  if (files.length === 0 && !(canEdit && showWhenEmpty)) return null

  async function add(picked: File[]) {
    if (picked.length === 0) return
    const invalid = checkFiles(picked, files.length, limits)
    if (invalid) {
      setError(invalid)
      return
    }
    setError(null)
    let current = files
    for (const file of picked) {
      setUploading({ name: file.name, fraction: 0 })
      try {
        const added = await attachmentsApi.upload(task.id, file, (fraction) => setUploading({ name: file.name, fraction }))
        current = [...current, added]
        onChange?.(current)
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) onSessionExpired?.()
        else setError(uploadErrorText(err, file.name, limits))
        break
      }
    }
    setUploading(null)
  }

  async function remove(attachment: Attachment) {
    if (!window.confirm(`Ștergi fișierul „${attachment.fileName}”?`)) return
    try {
      await attachmentsApi.remove(task.id, attachment.id)
      setError(null)
      onChange?.(files.filter((file) => file.id !== attachment.id))
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired?.()
      else setError(`Nu am putut șterge „${attachment.fileName}”.`)
    }
  }

  return (
    // Open while something is happening, and otherwise as the user left it.
    <details
      className="task-details attachments"
      open={open || uploading !== null || error !== null}
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary>
        {files.length === 0 ? 'Atașează fișiere' : `Fișiere (${files.length})`}
      </summary>
      {files.length > 0 && (
        <ul className="attachment-list">
          {files.map((file) => (
            <li key={file.id}>
              <Icon name={previewKind(file.fileName) ? 'eye' : 'paperclip'} size={15} />
              {previewKind(file.fileName) ? (
                <button
                  type="button"
                  className="attachment-open"
                  onClick={() => setViewing(viewable.indexOf(file))}
                  title={`Deschide ${file.fileName}`}
                >
                  {file.fileName}
                </button>
              ) : (
                <a href={urlOf(file)} download={file.fileName} title={`Descarcă ${file.fileName}`}>
                  {file.fileName}
                </a>
              )}
              {file.withAnswer && (
                <span className="answer-badge" title="Trimis cu un răspuns">
                  la răspuns
                </span>
              )}
              <span className="attachment-meta">
                {formatBytes(file.size)}
                {file.uploadedBy && ` · ${file.uploadedBy.displayName}`}
              </span>
              {previewKind(file.fileName) && (
                <a
                  className="attachment-download"
                  href={urlOf(file)}
                  download={file.fileName}
                  aria-label={`Descarcă ${file.fileName}`}
                  title="Descarcă"
                >
                  <Icon name="download" size={15} />
                </a>
              )}
              {canEdit && (
                <button type="button" className="attachment-remove" onClick={() => { remove(file) }} aria-label={`Șterge fișierul ${file.fileName}`} title="Șterge fișierul">
                  <Icon name="close" size={15} />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {viewing !== null && viewable.length > 0 && (
        <AttachmentViewer files={viewable} start={viewing} urlOf={urlOf} onClose={() => setViewing(null)} />
      )}
      {uploading && (
        <output className="upload-progress">
          <span>
            Se încarcă „{uploading.name}”… {Math.round(uploading.fraction * 100)}%
          </span>
          <progress value={uploading.fraction} max={1} aria-label={`Încărcare ${uploading.name}`} />
        </output>
      )}
      {error && (
        <p className="error attachment-error" role="alert">
          {error}
        </p>
      )}
      {canEdit && (
        <>
          <input
            ref={input}
            type="file"
            multiple
            hidden
            onChange={(e) => {
              const picked = [...(e.target.files ?? [])]
              // Cleared so the same file can be picked again after a failure.
              e.target.value = ''
              add(picked)
            }}
          />
          <button
            type="button"
            className="button ghost small attachment-add"
            disabled={uploading !== null || files.length >= limits.maxFilesPerTask}
            onClick={() => input.current?.click()}
          >
            <Icon name="paperclip" />
            Adaugă fișier
          </button>
          <span className="attachment-hint muted">
            Cel mult {limits.maxFileSizeMb} MB pe fișier, {limits.maxFilesPerTask} fișiere pe task.
          </span>
        </>
      )}
    </details>
  )
}
