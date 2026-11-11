import type { TaskItem, TaskPriority } from '../api/tasks'
import { addDaysIso } from './dates'

export type View =
  | { kind: 'all' }
  | { kind: 'today' }
  | { kind: 'upcoming' }
  | { kind: 'overdue' }
  | { kind: 'completed' }
  | { kind: 'tag'; tag: string }
  | { kind: 'integrations' }

export type SortBy = 'dueDate' | 'priority' | 'created'

export interface TaskGroup {
  key: string
  /** Null for a group that needs no divider. */
  label: string | null
  tone?: 'danger'
  tasks: TaskItem[]
}

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
    case 'integrations':
      return []
    default:
      return tasks
  }
}

export function matchesSearch(task: TaskItem, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  return [task.title, task.description ?? '', task.sourceEmailAddress ?? '', ...task.tags]
    .some((text) => text.toLowerCase().includes(needle))
}

const PRIORITY_RANK: Record<TaskPriority, number> = { high: 0, medium: 1, low: 2 }
const PRIORITY_LABEL: Record<TaskPriority, string> = {
  high: 'High priority',
  medium: 'Medium priority',
  low: 'Low priority',
}

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
      { key: 'overdue', label: 'Overdue', tone: 'danger', tasks: sorted.filter((t) => isOverdue(t, today)) },
      { key: 'today', label: 'Today', tasks: sorted.filter((t) => t.dueDate === today) },
      { key: 'tomorrow', label: 'Tomorrow', tasks: sorted.filter((t) => t.dueDate === tomorrow) },
      { key: 'later', label: 'Later', tasks: sorted.filter((t) => t.dueDate !== null && t.dueDate > tomorrow) },
      { key: 'none', label: 'No due date', tasks: sorted.filter((t) => t.dueDate === null) },
    )
  }

  groups.push({ key: 'completed', label: 'Completed', tasks: completed })

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

export function viewTitle(view: View): string {
  switch (view.kind) {
    case 'all':
      return 'All tasks'
    case 'today':
      return 'Today'
    case 'upcoming':
      return 'Upcoming'
    case 'overdue':
      return 'Overdue'
    case 'completed':
      return 'Completed'
    case 'tag':
      return view.tag
    case 'integrations':
      return 'Integrations'
  }
}

export function emptyMessage(view: View, searching: boolean): string {
  if (searching) return 'No tasks match your search.'
  switch (view.kind) {
    case 'today':
      return 'Nothing due today.'
    case 'upcoming':
      return 'Nothing coming up.'
    case 'overdue':
      return 'Nothing is overdue. Nice.'
    case 'completed':
      return 'No completed tasks yet.'
    case 'tag':
      return `No tasks tagged #${view.tag}.`
    default:
      return 'No tasks yet. Add your first one below.'
  }
}
