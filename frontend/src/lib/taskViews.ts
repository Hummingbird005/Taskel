import type { TaskItem, TaskPriority } from '../api/tasks'
import { addDaysIso } from './dates'

export type View =
  | { kind: 'dashboard' }
  | { kind: 'all' }
  | { kind: 'today' }
  | { kind: 'upcoming' }
  | { kind: 'overdue' }
  | { kind: 'completed' }
  | { kind: 'delegated' }
  | { kind: 'claimable' }
  | { kind: 'forwarded' }
  | { kind: 'tag'; tag: string }
  | { kind: 'integrations' }
  | { kind: 'team' }
  | { kind: 'teamTasks' }
  | { kind: 'groups' }
  | { kind: 'admin' }
  | { kind: 'settings' }

export type SortBy = 'dueDate' | 'priority' | 'created'

export interface TaskGroup {
  key: string
  /** Null for a group that needs no divider. */
  label: string | null
  tone?: 'danger'
  tasks: TaskItem[]
}

/**
 * Whether a task is the user's own work: assigned to them, or created by them and not handed
 * to someone else or to a group. The rest of what they see is listed separately: tasks they
 * created for others ("delegated"), tasks waiting on their groups, and tasks they forwarded.
 */
export const isMine = (task: TaskItem) =>
  task.access === 'assignee' || (task.access === 'creator' && !task.assignee && !task.group)

/** Created by the user and given to someone else or to a group. */
export const isDelegated = (task: TaskItem) => task.access === 'creator' && !isMine(task)

/** The selection value for a task's target in a picker: a user id, or a group id with a prefix. */
export const GROUP_PREFIX = 'group:'

export const isOverdue = (task: TaskItem, today: string) =>
  !task.isCompleted && task.dueDate !== null && task.dueDate < today

/** The tasks a view shows, before searching and grouping. */
export function tasksInView(tasks: TaskItem[], view: View, today: string): TaskItem[] {
  switch (view.kind) {
    case 'today':
      return tasks.filter((t) => !t.isCompleted && t.dueDate === today)
    case 'upcoming':
      return tasks.filter((t) => !t.isCompleted && t.dueDate !== null && t.dueDate > today)
    case 'overdue':
      return tasks.filter((t) => isOverdue(t, today))
    case 'completed':
      return tasks.filter((t) => t.isCompleted)
    case 'tag':
      return tasks.filter((t) => t.tags.includes(view.tag))
    case 'dashboard':
    case 'integrations':
    case 'team':
    case 'teamTasks':
    case 'groups':
    case 'admin':
    case 'settings':
      return []
    default:
      // 'all', 'delegated', 'claimable' and 'forwarded' show everything they are given.
      return tasks
  }
}

export function matchesSearch(task: TaskItem, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  const people = [task.createdBy.displayName, task.assignee?.displayName ?? '', task.group?.name ?? '']
  return [task.title, task.description ?? '', task.sourceEmailAddress ?? '', ...people, ...task.tags]
    .some((text) => text.toLowerCase().includes(needle))
}

const PRIORITY_RANK: Record<TaskPriority, number> = { high: 0, medium: 1, low: 2 }
const PRIORITY_LABEL: Record<TaskPriority, string> = {
  high: 'Prioritate ridicată',
  medium: 'Prioritate medie',
  low: 'Prioritate scăzută',
}

/** The order of a day's tasks: by priority. */
export const compareWithinDay = (a: TaskItem, b: TaskItem) => PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority]

const byDueDate = (a: TaskItem, b: TaskItem) => (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999')
const byPriority = (a: TaskItem, b: TaskItem) => PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority]

/**
 * Splits tasks into the sections shown between dividers. Open tasks are grouped by the sort
 * order; completed ones always come last. Tasks keep their creation order within ties.
 */
export function groupTasks(tasks: TaskItem[], sortBy: SortBy, today: string): TaskGroup[] {
  const open = tasks.filter((t) => !t.isCompleted)
  const completed = tasks.filter((t) => t.isCompleted)
  const groups: TaskGroup[] = []

  if (sortBy === 'created') {
    groups.push({ key: 'open', label: null, tasks: open })
  } else if (sortBy === 'priority') {
    for (const priority of ['high', 'medium', 'low'] as const) {
      groups.push({
        key: priority,
        label: PRIORITY_LABEL[priority],
        tasks: open.filter((t) => t.priority === priority).sort(byDueDate),
      })
    }
  } else {
    const tomorrow = addDaysIso(today, 1)
    const sorted = [...open].sort((a, b) => byDueDate(a, b) || byPriority(a, b))
    groups.push(
      { key: 'overdue', label: 'Întârziate', tone: 'danger', tasks: sorted.filter((t) => isOverdue(t, today)) },
      { key: 'today', label: 'Azi', tasks: sorted.filter((t) => t.dueDate === today) },
      { key: 'tomorrow', label: 'Mâine', tasks: sorted.filter((t) => t.dueDate === tomorrow) },
      { key: 'later', label: 'Mai târziu', tasks: sorted.filter((t) => t.dueDate !== null && t.dueDate > tomorrow) },
      { key: 'none', label: 'Fără termen', tasks: sorted.filter((t) => t.dueDate === null) },
    )
  }

  groups.push({ key: 'completed', label: 'Finalizate', tasks: completed })

  const nonEmpty = groups.filter((group) => group.tasks.length > 0)
  // A single section needs no divider above it.
  return nonEmpty.length === 1 ? [{ ...nonEmpty[0], label: null }] : nonEmpty
}

export interface TagCount {
  tag: string
  open: number
}

/** Every tag in use, alphabetically, with its number of open tasks. */
export function tagCounts(tasks: TaskItem[]): TagCount[] {
  const counts = new Map<string, number>()
  for (const task of tasks) {
    for (const tag of task.tags) {
      counts.set(tag, (counts.get(tag) ?? 0) + (task.isCompleted ? 0 : 1))
    }
  }
  return [...counts.entries()]
    .map(([tag, open]) => ({ tag, open }))
    .sort((a, b) => a.tag.localeCompare(b.tag))
}

/** The tasks a view draws from: the user's own, or one of the lists of other people's tasks. */
export function sourceOf(
  view: View,
  lists: { mine: TaskItem[]; delegated: TaskItem[]; claimable: TaskItem[]; forwarded: TaskItem[] },
): TaskItem[] {
  switch (view.kind) {
    case 'delegated':
      return lists.delegated
    case 'claimable':
      return lists.claimable
    case 'forwarded':
      return lists.forwarded
    default:
      return lists.mine
  }
}

/** Views that show a page of their own rather than a list of tasks. */
export const PAGE_VIEWS: ReadonlySet<View['kind']> = new Set(['integrations', 'team', 'teamTasks', 'groups', 'admin', 'settings'])

export function viewTitle(view: View): string {
  switch (view.kind) {
    case 'dashboard':
      return 'Acasă'
    case 'all':
      return 'Toate task-urile'
    case 'today':
      return 'Azi'
    case 'upcoming':
      return 'Urmează'
    case 'overdue':
      return 'Întârziate'
    case 'completed':
      return 'Finalizate'
    case 'delegated':
      return 'Delegate'
    case 'claimable':
      return 'De preluat'
    case 'forwarded':
      return 'Direcționate'
    case 'groups':
      return 'Grupuri'
    case 'tag':
      return view.tag
    case 'integrations':
      return 'Integrări'
    case 'team':
      return 'Echipă'
    case 'teamTasks':
      return 'Task-urile echipei'
    case 'admin':
      return 'Utilizatori'
    case 'settings':
      return 'Setări sistem'
  }
}

/** The headline and pictogram of a view with nothing in it; `emptyMessage` gives the sentence under it. */
export function emptyTitle(view: View, searching: boolean): { title: string; icon: 'search' | 'inbox' | 'sun' | 'calendar' | 'checkCircle' | 'send' | 'hand' | 'forward' | 'hash' } {
  if (searching) return { title: 'Niciun rezultat', icon: 'search' }
  switch (view.kind) {
    case 'today':
      return { title: 'Nimic pentru azi', icon: 'sun' }
    case 'upcoming':
      return { title: 'Nimic programat', icon: 'calendar' }
    case 'overdue':
      return { title: 'Ești la zi', icon: 'checkCircle' }
    case 'completed':
      return { title: 'Nimic finalizat', icon: 'checkCircle' }
    case 'delegated':
      return { title: 'Nimic delegat', icon: 'send' }
    case 'claimable':
      return { title: 'Nimic de preluat', icon: 'hand' }
    case 'forwarded':
      return { title: 'Nimic direcționat', icon: 'forward' }
    case 'tag':
      return { title: 'Etichetă fără task-uri', icon: 'hash' }
    default:
      return { title: 'Niciun task', icon: 'inbox' }
  }
}

/**
 * One sentence under the title of the views whose names do not explain themselves: which tasks
 * are listed, and what the user can still do with them.
 */
export function viewHint(view: View): string | null {
  switch (view.kind) {
    case 'delegated':
      return 'Task-uri create de tine și date altcuiva sau unui grup. Rămân ale tale: le poți modifica, realoca și șterge.'
    case 'forwarded':
      return 'Task-uri primite de tine de la altcineva și date mai departe. Nu mai sunt la tine: le poți doar citi, ca să vezi ce s-a întâmplat cu ele.'
    case 'claimable':
      return 'Task-uri date unui grup din care faci parte. Le vede tot grupul, până când unul dintre voi apasă „Preia”.'
    default:
      return null
  }
}

export function emptyMessage(view: View, searching: boolean): string {
  if (searching) return 'Niciun task nu se potrivește căutării.'
  switch (view.kind) {
    case 'today':
      return 'Nimic cu termen azi.'
    case 'upcoming':
      return 'Nimic în perioada următoare.'
    case 'overdue':
      return 'Nimic întârziat. Bravo!'
    case 'completed':
      return 'Niciun task finalizat încă.'
    case 'delegated':
      return 'Nu ai dat niciun task creat de tine altcuiva.'
    case 'claimable':
      return 'Niciun task nu așteaptă să fie preluat în grupurile tale.'
    case 'forwarded':
      return 'Nu ai dat mai departe niciun task primit de la altcineva.'
    case 'tag':
      return `Niciun task cu eticheta #${view.tag}.`
    default:
      return 'Niciun task încă. Adaugă primul mai jos.'
  }
}
