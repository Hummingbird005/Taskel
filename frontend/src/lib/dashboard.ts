import type { TaskItem } from '../api/tasks'
import { addDaysIso } from './dates'

export type WidgetId =
  | 'stats'
  | 'overdue'
  | 'today'
  | 'upcoming'
  | 'reply'
  | 'priority'
  | 'completed-week'
  | 'tags'
  | 'recent'

export interface WidgetInfo {
  id: WidgetId
  title: string
  description: string
  /** Wide widgets span the full width of the dashboard. */
  wide?: boolean
}

/** Every widget a dashboard can show, in the order they are offered. */
export const WIDGETS: WidgetInfo[] = [
  { id: 'stats', title: 'At a glance', description: 'Open, due today, overdue and recently completed counts.', wide: true },
  { id: 'overdue', title: 'Overdue', description: 'Open tasks whose due date has passed.' },
  { id: 'today', title: 'Due today', description: 'Open tasks due today.' },
  { id: 'upcoming', title: 'Next 7 days', description: 'Open tasks due in the coming week.' },
  { id: 'reply', title: 'Waiting for your reply', description: 'Email tasks that need a response to be completed.' },
  { id: 'priority', title: 'Open tasks by priority', description: 'How your open tasks split across priorities.' },
  { id: 'completed-week', title: 'Completed, last 7 days', description: 'Tasks you completed on each of the last 7 days.' },
  { id: 'tags', title: 'Tags', description: 'Your most used tags, by open tasks.' },
  { id: 'recent', title: 'Recently completed', description: 'The last tasks you finished.' },
]

export const DEFAULT_WIDGETS: WidgetId[] = [
  'stats',
  'overdue',
  'today',
  'upcoming',
  'reply',
  'priority',
  'completed-week',
  'tags',
]

const KNOWN = new Map(WIDGETS.map((widget) => [widget.id as string, widget]))

/** The widgets to show for a saved layout, skipping ids this version does not know. */
export function visibleWidgets(saved: string[] | null): WidgetInfo[] {
  return (saved ?? DEFAULT_WIDGETS).flatMap((id) => KNOWN.get(id) ?? [])
}

export function hiddenWidgets(visible: WidgetInfo[]): WidgetInfo[] {
  const shown = new Set(visible.map((widget) => widget.id))
  return WIDGETS.filter((widget) => !shown.has(widget.id))
}

/** Local calendar date (YYYY-MM-DD) of a timestamp. */
function localDate(timestamp: string): string {
  const date = new Date(timestamp)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export interface DayCount {
  date: string
  count: number
}

/** Tasks completed on each of the last 7 days, oldest first, ending today. */
export function completedPerDay(tasks: TaskItem[], today: string): DayCount[] {
  const days = Array.from({ length: 7 }, (_, i) => ({ date: addDaysIso(today, i - 6), count: 0 }))
  const byDate = new Map(days.map((day) => [day.date, day]))
  for (const task of tasks) {
    if (!task.isCompleted || !task.completedAt) continue
    const day = byDate.get(localDate(task.completedAt))
    if (day) day.count += 1
  }
  return days
}

/** Completed tasks, most recently completed first. Tasks without a completion time go last. */
export function recentlyCompleted(tasks: TaskItem[]): TaskItem[] {
  return tasks
    .filter((task) => task.isCompleted)
    .sort((a, b) => (b.completedAt ?? '').localeCompare(a.completedAt ?? ''))
}
