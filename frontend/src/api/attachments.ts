import { API_BASE, ApiError, request } from './client'
import { formatBytes } from '../lib/text'
import type { UserSummary } from './users'

/** A file attached to a task. */
export interface Attachment {
  id: string
  fileName: string
  /** Size in bytes. */
  size: number
  contentType: string
  uploadedBy: UserSummary | null
  uploadedAt: string
  /** Sent with an answer: to whoever distributed the task, and with the email reply when it is closed. */
  withAnswer?: boolean
}

export interface AttachmentLimits {
  maxFileSizeMb: number
  maxFileSizeBytes: number
  maxFilesPerTask: number
}

/** Used until the server's limits are known; the server checks every upload anyway. */
export const DEFAULT_ATTACHMENT_LIMITS: AttachmentLimits = {
  maxFileSizeMb: 100,
  maxFileSizeBytes: 100 * 1024 * 1024,
  maxFilesPerTask: 20,
}

export const attachmentsApi = {
  limits: () => request<AttachmentLimits>('/attachments/limits'),
  /** Where a file is downloaded from. The browser saves it; to show a PDF or a picture, see {@link viewUrl}. */
  downloadUrl: (taskId: string, id: string) => `${API_BASE}/tasks/${taskId}/attachments/${id}`,
  /** The same file, as a manager reaches it through a team member's tasks. */
  memberDownloadUrl: (memberId: string, taskId: string, id: string) =>
    `${API_BASE}/team/members/${memberId}/tasks/${taskId}/attachments/${id}`,
  /** The same file shown rather than saved: PDF files and pictures only, recognised by the server from their content. */
  viewUrl: (downloadUrl: string) => `${downloadUrl}?view=true`,
  remove: (taskId: string, id: string) => request<void>(`/tasks/${taskId}/attachments/${id}`, { method: 'DELETE' }),
  /**
   * Uploads one file. Uses XMLHttpRequest rather than fetch because it reports progress, which
   * matters for files of tens of megabytes.
   */
  upload: (taskId: string, file: File, onProgress?: (fraction: number) => void) =>
    new Promise<Attachment>((resolve, reject) => {
      const xhr = new XMLHttpRequest()
      xhr.open('POST', `${API_BASE}/tasks/${taskId}/attachments`)
      xhr.withCredentials = true
      xhr.responseType = 'text'
      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable) onProgress?.(event.loaded / event.total)
      }
      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            resolve(JSON.parse(xhr.responseText) as Attachment)
          } catch {
            reject(new ApiError(xhr.status, 'Unreadable response'))
          }
          return
        }
        let message = `Request failed (${xhr.status})`
        try {
          const problem = JSON.parse(xhr.responseText)
          if (typeof problem.title === 'string') message = problem.title
        } catch {
          // Not JSON, e.g. a proxy's own "too large" page.
        }
        reject(new ApiError(xhr.status, message))
      }
      // A server that refuses a large body can close the connection before the browser reads the answer.
      xhr.onerror = () => reject(new ApiError(0, 'Network error'))
      xhr.onabort = () => reject(new ApiError(0, 'Aborted'))
      const form = new FormData()
      form.append('file', file, file.name)
      xhr.send(form)
    }),
}

/** What to tell the user when an upload failed. */
export function uploadErrorText(err: unknown, fileName: string, limits: AttachmentLimits): string {
  const status = err instanceof ApiError ? err.status : -1
  switch (status) {
    case 413:
      return `„${fileName}” depășește limita de ${limits.maxFileSizeMb} MB.`
    case 409:
      return `Un task poate avea cel mult ${limits.maxFilesPerTask} fișiere.`
    case 403:
      return 'Doar cine lucrează la task îi poate atașa fișiere.'
    case 404:
      return 'Task-ul nu mai există sau nu mai ai acces la el.'
    case 400:
      return `„${fileName}” este gol sau nu a putut fi citit.`
    case 0:
      return `Încărcarea fișierului „${fileName}” s-a întrerupt. Verifică mărimea lui și conexiunea, apoi încearcă din nou.`
    default:
      return `Nu am putut încărca „${fileName}”.`
  }
}

/** Checks files against the limits before anything is sent. Returns a message, or null when they are fine. */
export function checkFiles(files: File[], alreadyAttached: number, limits: AttachmentLimits): string | null {
  if (alreadyAttached + files.length > limits.maxFilesPerTask) {
    return `Un task poate avea cel mult ${limits.maxFilesPerTask} fișiere.`
  }
  const tooLarge = files.find((file) => file.size > limits.maxFileSizeBytes)
  if (tooLarge) {
    // The size is shown only when it reads differently from the limit: 100 MB plus one byte is still "100 MB".
    const size = formatBytes(tooLarge.size)
    const shown = size === `${limits.maxFileSizeMb} MB` ? '' : ` (${size})`
    return `„${tooLarge.name}”${shown} depășește limita de ${limits.maxFileSizeMb} MB pe fișier.`
  }
  const empty = files.find((file) => file.size === 0)
  if (empty) return `„${empty.name}” este gol.`
  return null
}


/** What Taskel can show of a file without downloading it, judged by its name; the server checks the content. */
export function previewKind(fileName: string): 'pdf' | 'image' | null {
  const extension = fileName.toLowerCase().split('.').pop() ?? ''
  if (extension === 'pdf') return 'pdf'
  return ['png', 'jpg', 'jpeg', 'jfif', 'gif', 'webp'].includes(extension) ? 'image' : null
}
