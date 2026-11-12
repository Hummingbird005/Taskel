import type { TaskItem } from '../api/tasks'
import { addDaysIso } from './dates'

export type WidgetId =
  | 'stats'
  | 'overdue'
  | 'today'
  | 'upcoming'
  | 'reply'
  | 'delegated'
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
  { id: 'stats', title: 'Pe scurt', description: 'Câte task-uri sunt deschise, cu termen azi, întârziate și finalizate recent.', wide: true },
  { id: 'overdue', title: 'Întârziate', description: 'Task-uri deschise cu termenul depășit.' },
  { id: 'today', title: 'De făcut azi', description: 'Task-uri deschise cu termen azi.' },
  { id: 'upcoming', title: 'Următoarele 7 zile', description: 'Task-uri deschise cu termen în săptămâna următoare.' },
  { id: 'reply', title: 'Așteaptă răspunsul tău', description: 'Task-uri din email care se finalizează doar cu un răspuns.' },
  { id: 'delegated', title: 'Delegate de mine', description: 'Task-uri deschise pe care le-ai alocat altor persoane.' },
  { id: 'priority', title: 'Task-uri deschise pe priorități', description: 'Cum se împart task-urile deschise pe priorități.' },
  { id: 'completed-week', title: 'Finalizate în ultimele 7 zile', description: 'Task-urile finalizate în fiecare dintre ultimele 7 zile.' },
  { id: 'tags', title: 'Etichete', description: 'Cele mai folosite etichete, după task-urile deschise.' },
  { id: 'recent', title: 'Finalizate recent', description: 'Ultimele task-uri pe care le-ai finalizat.' },
]

export const DEFAULT_WIDGETS: WidgetId[] = [
  'stats',
  'overdue',
  'today',
  'upcoming',
  'reply',
  'delegated',
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
