import type { Attachment } from '../api/attachments'
import type { TaskItem, TaskMessage } from '../api/tasks'
import type { IconName } from '../components/Icon'

/** A person in the thread: named, and known by id when they have an account. */
export interface Party {
  id?: string
  name: string
  /** An address shown after the name, for someone outside the company. */
  email?: string
}

export type BadgeTone = 'ok' | 'back' | 'ext' | 'done' | 'info'

/** A message in the thread, drawn as a mail: who, to whom, when, the text and its files. */
export interface ThreadMessage {
  type: 'message'
  key: string
  at: string
  from: Party
  /** Who it went to; empty for a note on the task. */
  to: Party[]
  badge?: { text: string; tone: BadgeTone }
  text: string | null
  files: Attachment[]
  /** The email or message the task started from: always shown open, first. */
  original?: boolean
  /** The time to show, when it is not `at`: when the original email came. */
  shownAt?: string
  /** For an answer to a distributed task the reader can still send back: the copy and who has it. */
  sendBack?: { copyId: string; holder: string }
}

/** Something that happened to the task, drawn as a thin line between messages. */
export interface ThreadEvent {
  type: 'event'
  key: string
  at: string
  who: Party | null
  what: string
  text?: string | null
  icon: IconName
}

export type ThreadItem = ThreadMessage | ThreadEvent

const user = (u: { id: string; displayName: string }): Party => ({ id: u.id, name: u.displayName })

/** "De la: Ana Client <ana@client.example>" on the first line of a task made from an email: the sender, and the rest. */
export function emailSender(task: TaskItem): { from: Party; body: string | null; received: string | null } | null {
  let rest = task.description ?? ''
  const match = /^De la: (.*?)\s*<([^>]+)>\s*\r?\n/.exec(rest)
  // A copy of an email task carries the email too, without its sender's address on the task.
  if (!match && !task.sourceEmailAddress) return null
  if (match) rest = rest.slice(match[0].length)
  // "Primit: 2026-11-23 09:12", when Outlook gave the time the email came: the sender's local time.
  const received = /^Primit: (\d{4}-\d{2}-\d{2}) (\d{2}:\d{2})\s*(?:\r?\n|$)/.exec(rest)
  if (received) rest = rest.slice(received[0].length)
  const body = rest.replace(/^\s+/, '') || null
  const at = received ? `${received[1]}T${received[2]}:00` : null
  if (!match) return { from: { name: task.sourceEmailAddress!, email: task.sourceEmailAddress! }, body, received: at }
  const name = match[1].trim().replace(/^"|"$/g, '')
  return { from: { name: name || match[2], email: match[2] }, body, received: at }
}

/** Who answers for the task now: the person it is assigned to, or its group, or nobody else. */
function holderOf(task: TaskItem): Party[] {
  if (task.assignee) return [user(task.assignee)]
  if (task.group) return [{ name: `grupul ${task.group.name}` }]
  return []
}

/**
 * Everything that happened to a task, oldest first, as a mail thread: the email or message it started from, the
 * forwards, the distribution and the answers, the messages, the moves, and how it ended.
 */
export function buildThread(task: TaskItem, messages: TaskMessage[], canSendBack: boolean): ThreadItem[] {
  const items: ThreadItem[] = []
  const onCopy = Boolean(task.distributedFrom)
  const email = emailSender(task)
  // Whoever answers for the task: the one the answers of a distribution come to.
  const responsible = user(task.assignee ?? task.createdBy)
  // An address of someone with an account shows as their name.
  const named = (address: string): Party =>
    address === task.createdBy.email ? user(task.createdBy) : { name: address, email: address }

  // The start: the email, or the task as it was written.
  items.push({
    type: 'message',
    key: 'original',
    // When the email came, if known; ordering still follows when the task was made.
    at: task.createdAt,
    shownAt: email?.received ?? undefined,
    from: email?.from ?? user(task.createdBy),
    to: email && !onCopy ? [user(task.createdBy)] : onCopy ? [] : holderOf(task),
    badge: email ? { text: 'Email extern', tone: 'ext' } : undefined,
    text: email ? email.body : (task.description ?? null),
    files: task.attachments.filter((a) => !a.withAnswer),
    original: true,
  })
  if (email && !onCopy) {
    const given = task.forwards.length === 0 && task.assignee ? ` și i l-a dat lui ${task.assignee.displayName}` : ''
    items.push({ type: 'event', key: 'brought', at: task.createdAt, who: user(task.createdBy), what: `a adus emailul în Taskel${given}`, icon: 'mail' })
  }
  // On a copy the instructions are the distribution's message, already in the thread.
  if (task.instructions && !onCopy) {
    items.push({
      type: 'message',
      key: 'instructions',
      at: task.createdAt,
      from: user(task.createdBy),
      to: holderOf(task),
      badge: { text: 'Indicații', tone: 'info' },
      text: task.instructions,
      files: [],
    })
  }

  task.forwards.forEach((step, i) => {
    const to: Party = step.toUser ? user(step.toUser) : { name: step.toGroup ? `grupul ${step.toGroup.name}` : '—' }
    if (step.note) {
      items.push({
        type: 'message',
        key: `forward-${i}`,
        at: step.at,
        from: user(step.from),
        to: [to],
        badge: step.isReturn ? { text: 'Rezolvat, trimis înapoi', tone: 'ok' } : { text: 'Direcționat', tone: 'info' },
        text: step.note,
        files: [],
      })
    } else {
      items.push({
        type: 'event',
        key: `forward-${i}`,
        at: step.at,
        who: user(step.from),
        what: step.isReturn ? `a rezolvat și i-a trimis task-ul înapoi lui ${to.name}` : `a direcționat task-ul către ${to.name}`,
        icon: step.isReturn ? 'reply' : 'forward',
      })
    }
  })

  // The last answer of each copy is the one that can be sent back.
  const lastAnswer = new Map<string, string>()
  for (const m of messages) if (m.kind === 'answered' && m.related) lastAnswer.set(m.related.taskId, m.id)
  const copies = new Map((task.distributions ?? []).map((c) => [c.taskId, c]))

  for (let i = 0; i < messages.length; i++) {
    const m = messages[i]
    const author: Party | null = m.author ? user(m.author) : null
    const recipient: Party | null = m.related && !onCopy ? { name: m.related.recipient } : null
    const files = m.attachments ?? []
    switch (m.kind) {
      case 'distributed': {
        // One distribution to several people is one message to all of them.
        const to: Party[] = recipient ? [recipient] : []
        while (
          i + 1 < messages.length &&
          messages[i + 1].kind === 'distributed' &&
          messages[i + 1].author?.id === m.author?.id &&
          messages[i + 1].text === m.text &&
          !onCopy
        ) {
          i++
          const next = messages[i].related
          if (next) to.push({ name: next.recipient })
        }
        items.push({
          type: 'message',
          key: m.id,
          at: m.at,
          from: author ?? { name: 'Taskel' },
          to: onCopy ? [] : to,
          badge: { text: 'Distribuit', tone: 'info' },
          text: m.text,
          files,
        })
        break
      }
      case 'answered':
      case 'replied':
      case 'sentBack':
      case 'note': {
        const copy = m.related ? copies.get(m.related.taskId) : undefined
        const sendBack =
          m.kind === 'answered' && canSendBack && !onCopy && copy?.state === 'completed' && lastAnswer.get(copy.taskId) === m.id
            ? { copyId: copy.taskId, holder: m.related!.recipient }
            : undefined
        // On a copy, what its holder writes goes to whoever distributed it.
        const toDistributor = onCopy && (m.kind === 'answered' || m.kind === 'replied' || m.kind === 'note') && m.author?.id !== task.createdBy.id
        const fromCopy = !onCopy && m.related && (m.kind === 'answered' || m.kind === 'replied')
        const to: Party[] =
          toDistributor
            ? [user(task.createdBy)]
            : fromCopy
              ? [responsible]
              : m.kind === 'replied' && m.sentTo
            ? [named(m.sentTo)]
            : m.kind === 'sentBack' && recipient
              ? [recipient]
              : m.kind === 'note' && recipient && recipient.name !== author?.name
                ? [recipient]
                : []
        items.push({
          type: 'message',
          key: m.id,
          at: m.at,
          from: author ?? { name: 'Taskel' },
          to,
          badge:
            m.kind === 'answered'
              ? { text: 'A răspuns', tone: 'ok' }
              : m.kind === 'replied'
                ? { text: m.sentTo ? 'Trimis pe email' : 'Răspuns', tone: 'info' }
                : m.kind === 'sentBack'
                  ? { text: 'Trimis înapoi', tone: 'back' }
                  : undefined,
          text: m.text,
          files,
          sendBack,
        })
        break
      }
      case 'allAnswered':
        items.push({ type: 'event', key: m.id, at: m.at, who: null, what: 'Au răspuns toți. Task-ul e din nou deschis.', icon: 'checkCircle' })
        break
      case 'closed':
        items.push({
          type: 'event',
          key: m.id,
          at: m.at,
          who: author,
          what: onCopy ? 'a închis task-ul, fără să mai aștepte răspunsul' : `a închis task-ul lui ${m.related?.recipient ?? '—'}`,
          icon: 'close',
        })
        break
      case 'dueDateChanged':
      case 'rescheduled':
        items.push({
          type: 'event',
          key: m.id,
          at: m.at,
          who: author,
          what: m.kind === 'rescheduled' ? 'a replanificat task-ul' : 'a schimbat termenul',
          text: m.text,
          icon: 'calendar',
        })
        break
    }
  }

  // How it ended; on a copy the answer is already in the thread.
  if (task.completedAt && !onCopy) {
    const by = task.assignee ?? task.createdBy
    if (task.completionResponse) {
      items.push({
        type: 'message',
        key: 'completed',
        at: task.completedAt,
        from: user(by),
        to: task.sourceEmailAddress ? [named(task.assignee ? task.createdBy.email : task.sourceEmailAddress)] : [],
        badge: { text: 'Finalizat', tone: 'done' },
        text: task.completionResponse,
        files: task.attachments.filter((a) => a.withAnswer && !messages.some((m) => m.attachments?.some((f) => f.id === a.id))),
      })
    } else {
      items.push({ type: 'event', key: 'completed', at: task.completedAt, who: user(by), what: 'a finalizat task-ul', icon: 'check' })
    }
  }

  const ordered = items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => a.item.at.localeCompare(b.item.at) || a.index - b.index)
    .map(({ item }) => item)

  // Where it stands now, at the end.
  const open = (task.distributions ?? []).filter((c) => c.state !== 'completed')
  if (open.length > 0 && task.state !== 'completed') {
    const names = open.map((c) => c.assignee?.displayName ?? c.group?.name ?? '—').join(', ')
    ordered.push({ type: 'event', key: 'awaiting', at: '', who: null, what: `Așteaptă răspunsul de la ${names}.`, icon: 'clock' })
  } else if (task.state === 'waiting' && task.waitingReason) {
    ordered.push({ type: 'event', key: 'waiting', at: task.waitingSince ?? '', who: null, what: 'În așteptare', text: task.waitingReason, icon: 'clock' })
  }
  return ordered
}

/**
 * Which messages are drawn open: the original, and the last two; the others fold to one line. Runs of three or
 * more folded messages fold further, into "N mesaje mai vechi".
 */
export function foldedKeys(items: ThreadItem[]): Set<string> {
  const messages = items.filter((i): i is ThreadMessage => i.type === 'message' && !i.original)
  return new Set(messages.slice(0, -2).filter((m) => !m.sendBack).map((m) => m.key))
}
