import { useRef, useState } from 'react'
import { ApiError } from '../api/client'
import { attachmentsApi, checkFiles, uploadErrorText, type Attachment, type AttachmentLimits } from '../api/attachments'
import type { TaskItem } from '../api/tasks'
import { formatBytes } from '../lib/text'
import Icon from './Icon'

interface Props {
  task: TaskItem
  limits: AttachmentLimits
  /** Whether the person looking may add and remove files. Without it the list is read-only. */
  canEdit?: boolean
  /** Called with the task's new list of files after one was added or removed. */
  onChange?: (attachments: Attachment[]) => void
  onSessionExpired?: () => void
  /** Builds the download address; the default goes through the task itself. */
  downloadUrl?: (attachment: Attachment) => string
}

/** The files of a task: a list to download from and, for those who work on it, a way to add and remove. */
export default function TaskAttachments({ task, limits, canEdit = false, onChange, onSessionExpired, downloadUrl }: Props) {
  const input = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState<{ name: string; fraction: number } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const files = task.attachments

  if (files.length === 0 && !canEdit) return null

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
              <Icon name="paperclip" size={15} />
              <a
                href={downloadUrl ? downloadUrl(file) : attachmentsApi.downloadUrl(task.id, file.id)}
                download={file.fileName}
                title={`Descarcă ${file.fileName}`}
              >
                {file.fileName}
              </a>
              <span className="attachment-meta">
                {formatBytes(file.size)}
                {file.uploadedBy && ` · ${file.uploadedBy.displayName}`}
              </span>
              {canEdit && (
                <button type="button" className="attachment-remove" onClick={() => void remove(file)} aria-label={`Șterge fișierul ${file.fileName}`} title="Șterge fișierul">
                  <Icon name="close" size={15} />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {uploading && (
        <div className="upload-progress" role="status">
          <span>
            Se încarcă „{uploading.name}”… {Math.round(uploading.fraction * 100)}%
          </span>
          <progress value={uploading.fraction} max={1} aria-label={`Încărcare ${uploading.name}`} />
        </div>
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
              void add(picked)
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
