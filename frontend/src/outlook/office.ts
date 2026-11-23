// What the pane uses from Office.js, which Outlook gives it (loaded from Microsoft's address by outlook.html).
// Typed here by hand: only these few calls are needed, and the official typings are a large dependency.

interface AsyncResult<T> {
  status: 'succeeded' | 'failed'
  value: T
  error?: { message: string }
}

export interface OfficeAttachment {
  id: string
  name: string
  size: number
  isInline: boolean
  /** "file", "item" (an attached email) or "cloud" (a link to a shared file). */
  attachmentType: string
}

interface MailItem {
  /** Mailbox 1.8: the Outlook categories of the item; adding one needs the ReadWriteItem permission. */
  categories?: {
    getAsync(callback: (result: AsyncResult<{ displayName: string }[] | null>) => void): void
    addAsync(categories: string[], callback: (result: AsyncResult<void>) => void): void
  }
  subject?: string
  from?: { displayName?: string; emailAddress?: string }
  internetMessageId?: string
  dateTimeCreated?: Date
  attachments?: OfficeAttachment[]
  body: { getAsync(coercion: string, callback: (result: AsyncResult<string>) => void): void }
  /** Mailbox 1.8: the content of an attachment. */
  getAttachmentContentAsync?(id: string, callback: (result: AsyncResult<{ format: string; content: string }>) => void): void
  /** Mailbox 1.8: the message's headers, as text. */
  getAllInternetHeadersAsync?(callback: (result: AsyncResult<string>) => void): void
}

interface OfficeGlobal {
  onReady(): Promise<{ host: string | null }>
  context: {
    mailbox?: {
      item: MailItem | null
      addHandlerAsync?(eventType: string, handler: () => void): void
    }
    ui?: { openBrowserWindow?(url: string): void }
  }
  EventType?: { ItemChanged: string }
}

declare global {
  interface Window {
    Office?: OfficeGlobal
  }
}

/** The email open in Outlook, as Taskel's from-email request takes it. */
export interface OutlookEmail {
  subject: string
  sender: string
  senderEmail: string
  body: string
  importance: 'low' | 'normal' | 'high'
  receivedAt: string | null
  messageId: string | null
  /** The files the email carries; pictures shown inside its text are left out. */
  attachments: OfficeAttachment[]
  /** False in Outlook versions that cannot hand an attachment's content to an add-in. */
  canReadAttachments: boolean
}

// The same limits as the macro: what Taskel keeps of each field.
const MAX_SUBJECT = 1000
const MAX_SENDER = 320
const MAX_BODY = 4000
const MAX_MESSAGE_ID = 998

/** Waits for Office.js; true when running inside Outlook with a mailbox. */
export async function officeReady(): Promise<boolean> {
  const office = window.Office
  if (!office) return false
  try {
    await office.onReady()
  } catch {
    return false
  }
  return Boolean(office.context?.mailbox)
}

function item(): MailItem | null {
  return window.Office?.context.mailbox?.item ?? null
}

/** Calls fn again whenever the user selects another email while the pane is pinned. */
export function onItemChanged(fn: () => void) {
  const office = window.Office
  const mailbox = office?.context.mailbox
  if (mailbox?.addHandlerAsync && office?.EventType) mailbox.addHandlerAsync(office.EventType.ItemChanged, fn)
}

/** Opens Taskel in the user's browser, outside Outlook. */
export function openInBrowser(url: string) {
  const ui = window.Office?.context.ui
  if (ui?.openBrowserWindow) ui.openBrowserWindow(url)
  else window.open(url, '_blank', 'noopener')
}

function promised<T>(call: (callback: (result: AsyncResult<T>) => void) => void): Promise<T> {
  return new Promise((resolve, reject) =>
    call((result) => (result.status === 'succeeded' ? resolve(result.value) : reject(new Error(result.error?.message ?? 'Office error')))),
  )
}

/** Cuts text to at most max UTF-16 units without splitting a surrogate pair (half an emoji is not valid JSON). */
export function safeLeft(text: string, max: number): string {
  if (text.length <= max) return text
  const cut = text.slice(0, max)
  const last = cut.charCodeAt(cut.length - 1)
  return last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut
}

/** The local date and time, as the macro sends it: what the reader saw in Outlook. */
function localTimestamp(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

/** The importance from the headers ("Importance", or "X-Priority" from other mail programs). */
export function importanceFromHeaders(headers: string): OutlookEmail['importance'] {
  const importance = /^importance:\s*(\w+)/im.exec(headers)?.[1]?.toLowerCase()
  if (importance === 'high' || importance === 'low') return importance
  const priority = /^x-priority:\s*(\d)/im.exec(headers)?.[1]
  if (priority === '1' || priority === '2') return 'high'
  if (priority === '4' || priority === '5') return 'low'
  return 'normal'
}

/** Reads the email open in Outlook, or null when none is. */
export async function readEmail(): Promise<OutlookEmail | null> {
  const mail = item()
  if (!mail) return null

  const body = await promised<string>((cb) => mail.body.getAsync('text', cb)).catch(() => '')
  let importance: OutlookEmail['importance'] = 'normal'
  if (mail.getAllInternetHeadersAsync) {
    const headers = await promised<string>((cb) => mail.getAllInternetHeadersAsync!(cb)).catch(() => '')
    importance = importanceFromHeaders(headers)
  }

  const name = mail.from?.displayName?.trim() ?? ''
  const address = mail.from?.emailAddress?.trim() ?? ''
  const sender = !name || name === address ? address : address ? `${name} <${address}>` : name
  const messageId = mail.internetMessageId?.trim() ?? ''
  const created = mail.dateTimeCreated ? new Date(mail.dateTimeCreated) : null

  return {
    subject: safeLeft(mail.subject ?? '', MAX_SUBJECT),
    sender: safeLeft(sender, MAX_SENDER),
    senderEmail: safeLeft(address, MAX_SENDER),
    body: safeLeft(body.replace(/\r\n?/g, '\n'), MAX_BODY),
    importance,
    receivedAt: created && !Number.isNaN(created.getTime()) ? localTimestamp(created) : null,
    messageId: messageId && messageId.length <= MAX_MESSAGE_ID ? messageId : null,
    attachments: (mail.attachments ?? []).filter((a) => !a.isInline),
    canReadAttachments: typeof mail.getAttachmentContentAsync === 'function',
  }
}

/** The Outlook category put on emails sent to Taskel. */
export const TASKEL_CATEGORY = 'Taskel'

/**
 * Marks the open email with the category "Taskel", so the list shows it is in Taskel.
 * "missing" when Outlook has no such category: an add-in cannot create one without access to the whole mailbox,
 * so the user adds it once. "unsupported" in Outlook versions without categories for add-ins.
 */
export async function markAsSent(): Promise<'marked' | 'missing' | 'unsupported'> {
  const categories = item()?.categories
  if (!categories) return 'unsupported'
  const current = await promised<{ displayName: string }[] | null>((cb) => categories.getAsync(cb)).catch(() => null)
  if (current?.some((c) => c.displayName.toLowerCase() === TASKEL_CATEGORY.toLowerCase())) return 'marked'
  try {
    await promised<void>((cb) => categories.addAsync([TASKEL_CATEGORY], cb))
    return 'marked'
  } catch {
    return 'missing'
  }
}

/**
 * The content of an attachment as a file, or null for one Outlook only links to (a cloud attachment).
 * An attached email comes as .eml, which Taskel stores as any other file.
 */
export async function attachmentFile(attachment: OfficeAttachment): Promise<File | null> {
  const mail = item()
  if (!mail?.getAttachmentContentAsync) return null
  const content = await promised<{ format: string; content: string }>((cb) => mail.getAttachmentContentAsync!(attachment.id, cb))
  if (content.format === 'base64') {
    const bytes = Uint8Array.from(atob(content.content), (c) => c.charCodeAt(0))
    return new File([bytes], attachment.name || 'atasament', { type: 'application/octet-stream' })
  }
  if (content.format === 'eml' || content.format === 'iCalendar') {
    const extension = content.format === 'eml' ? '.eml' : '.ics'
    const name = /\.[a-z0-9]{2,5}$/i.test(attachment.name) ? attachment.name : `${attachment.name || 'mesaj'}${extension}`
    return new File([content.content], name, { type: 'application/octet-stream' })
  }
  return null
}
