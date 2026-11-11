import type { ReactNode } from 'react'
import type { TaskItem, TaskPriority } from '../api/tasks'
import { completedPerDay, recentlyCompleted, type WidgetId } from '../lib/dashboard'
import { addDaysIso, formatDueDate, formatLongDate, formatWeekday } from '../lib/dates'
import { isOverdue, tagCounts, type View } from '../lib/taskViews'
import Icon, { type IconName } from './Icon'

interface WidgetProps {
  tasks: TaskItem[]
  today: string
  onOpen: (view: View) => void
}

const MAX_ROWS = 5

/** A compact list of tasks. Each row opens the view the task lives in. */
function TaskLines({
  tasks,
  empty,
  view,
  meta,
  onOpen,
}: {
  tasks: TaskItem[]
  empty: string
  view: View
  meta: (task: TaskItem) => ReactNode
  onOpen: (view: View) => void
}) {
  if (tasks.length === 0) return <p className="widget-empty">{empty}</p>
  const more = tasks.length - MAX_ROWS
  return (
    <>
      <ul className="widget-lines">
        {tasks.slice(0, MAX_ROWS).map((task) => (
          <li key={task.id}>
            <button type="button" className="widget-line" onClick={() => onOpen(view)}>
              {task.priority === 'high' && (
                <span className="line-flag" title="High priority">
                  <Icon name="flag" size={13} />
                  <span className="visually-hidden">High priority: </span>
                </span>
              )}
              <span className="line-title">{task.title}</span>
              <span className="line-meta">{meta(task)}</span>
            </button>
          </li>
        ))}
      </ul>
      {more > 0 && (
        <button type="button" className="widget-more" onClick={() => onOpen(view)}>
          View all {tasks.length}
        </button>
      )}
    </>
  )
}

const dueMeta = (task: TaskItem) => (task.dueDate ? formatDueDate(task.dueDate) : '')

function Stats({ tasks, today, onOpen }: WidgetProps) {
  const open = tasks.filter((t) => !t.isCompleted)
  const weekAgo = addDaysIso(today, -6)
  const completedThisWeek = completedPerDay(tasks, today).reduce((sum, day) => sum + day.count, 0)
  const overdue = open.filter((t) => isOverdue(t, today)).length

  const tile = (label: string, value: number, icon: IconName, view: View, tone?: 'danger') => (
    <button type="button" className={`stat${tone ? ` ${tone}` : ''}`} onClick={() => onOpen(view)}>
      <span className="stat-label">
        <Icon name={icon} size={15} />
        {label}
      </span>
      <span className="stat-value">{value}</span>
    </button>
  )

  return (
    <div className="stats" title={`Completed counts tasks finished since ${formatDueDate(weekAgo)}`}>
      {tile('Open', open.length, 'inbox', { kind: 'all' })}
      {tile('Due today', open.filter((t) => t.dueDate === today).length, 'sun', { kind: 'today' })}
      {tile('Overdue', overdue, 'alert', { kind: 'overdue' }, overdue > 0 ? 'danger' : undefined)}
      {tile('Completed, 7 days', completedThisWeek, 'checkCircle', { kind: 'completed' })}
    </div>
  )
}

function Overdue({ tasks, today, onOpen }: WidgetProps) {
  const overdue = tasks
    .filter((t) => isOverdue(t, today))
    .sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? ''))
  return (
    <TaskLines tasks={overdue} empty="Nothing is overdue." view={{ kind: 'overdue' }} meta={dueMeta} onOpen={onOpen} />
  )
}

function Today({ tasks, today, onOpen }: WidgetProps) {
  const due = tasks.filter((t) => !t.isCompleted && t.dueDate === today)
  return (
    <TaskLines
      tasks={due}
      empty="Nothing due today."
      view={{ kind: 'today' }}
      meta={(task) => (task.tags.length > 0 ? `#${task.tags[0]}` : '')}
      onOpen={onOpen}
    />
  )
}

function Upcoming({ tasks, today, onOpen }: WidgetProps) {
  const until = addDaysIso(today, 7)
  const upcoming = tasks
    .filter((t) => !t.isCompleted && t.dueDate !== null && t.dueDate > today && t.dueDate <= until)
    .sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? ''))
  return (
    <TaskLines
      tasks={upcoming}
      empty="Nothing due in the next 7 days."
      view={{ kind: 'upcoming' }}
      meta={dueMeta}
      onOpen={onOpen}
    />
  )
}

function ReplyRequired({ tasks, onOpen }: WidgetProps) {
  const waiting = tasks.filter((t) => !t.isCompleted && t.sourceEmailAddress)
  return (
    <TaskLines
      tasks={waiting}
      empty="No email tasks are waiting for a reply."
      view={{ kind: 'tag', tag: 'email' }}
      meta={(task) => task.sourceEmailAddress}
      onOpen={onOpen}
    />
  )
}

const PRIORITIES: { priority: TaskPriority; label: string }[] = [
  { priority: 'high', label: 'High' },
  { priority: 'medium', label: 'Medium' },
  { priority: 'low', label: 'Low' },
]

/** One bar per priority on a shared scale: the bar carries magnitude, the label carries identity. */
function Priority({ tasks }: WidgetProps) {
  const open = tasks.filter((t) => !t.isCompleted)
  if (open.length === 0) return <p className="widget-empty">No open tasks.</p>
  const rows = PRIORITIES.map((row) => ({ ...row, count: open.filter((t) => t.priority === row.priority).length }))
  const max = Math.max(...rows.map((row) => row.count))
  return (
    <ul className="meter-list">
      {rows.map((row) => (
        <li key={row.priority} title={`${row.label}: ${row.count} of ${open.length} open tasks`}>
          <span className="meter-name">{row.label}</span>
          <span className="meter-track">
            <span className="meter-bar" style={{ width: `${(row.count / max) * 100}%` }} />
          </span>
          <span className="meter-figure">{row.count}</span>
        </li>
      ))}
    </ul>
  )
}

function CompletedWeek({ tasks, today }: WidgetProps) {
  const days = completedPerDay(tasks, today)
  const total = days.reduce((sum, day) => sum + day.count, 0)
  if (total === 0) return <p className="widget-empty">No tasks completed in the last 7 days.</p>
  const max = Math.max(...days.map((day) => day.count))
  return (
    <>
      <div className="columns" aria-hidden="true">
        {days.map((day) => {
          const isToday = day.date === today
          // Label only the tallest column and today, not every column.
          const labelled = day.count > 0 && (day.count === max || isToday)
          return (
            <div key={day.date} className="column" tabIndex={0}>
              <span className="column-tip">
                {formatLongDate(day.date)}: {day.count}
              </span>
              <span className="column-plot">
                {labelled && <span className="column-value">{day.count}</span>}
                <span className="column-bar" style={{ height: `${(day.count / max) * 100}%` }} />
              </span>
              <span className={`column-label${isToday ? ' today' : ''}`}>{isToday ? 'Today' : formatWeekday(day.date)}</span>
            </div>
          )
        })}
      </div>
      <table className="visually-hidden">
        <caption>Tasks completed per day, last 7 days ({total} in total)</caption>
        <thead>
          <tr>
            <th scope="col">Day</th>
            <th scope="col">Completed</th>
          </tr>
        </thead>
        <tbody>
          {days.map((day) => (
            <tr key={day.date}>
              <th scope="row">{formatLongDate(day.date)}</th>
              <td>{day.count}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

function Tags({ tasks, onOpen }: WidgetProps) {
  const tags = tagCounts(tasks)
    .sort((a, b) => b.open - a.open || a.tag.localeCompare(b.tag))
    .slice(0, 10)
  if (tags.length === 0) return <p className="widget-empty">Add tags to tasks to see them here.</p>
  return (
    <ul className="tag-cloud">
      {tags.map(({ tag, open }) => (
        <li key={tag}>
          <button type="button" className="chip tag" onClick={() => onOpen({ kind: 'tag', tag })}>
            #{tag} <span className="tag-count">{open}</span>
          </button>
        </li>
      ))}
    </ul>
  )
}

function Recent({ tasks, onOpen }: WidgetProps) {
  return (
    <TaskLines
      tasks={recentlyCompleted(tasks).slice(0, MAX_ROWS)}
      empty="No completed tasks yet."
      view={{ kind: 'completed' }}
      meta={(task) =>
        task.completedAt
          ? new Date(task.completedAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
          : ''
      }
      onOpen={onOpen}
    />
  )
}

/** Renders the body of one dashboard widget. */
export default function DashboardWidget({ id, ...props }: WidgetProps & { id: WidgetId }) {
  switch (id) {
    case 'stats':
      return <Stats {...props} />
    case 'overdue':
      return <Overdue {...props} />
    case 'today':
      return <Today {...props} />
    case 'upcoming':
      return <Upcoming {...props} />
    case 'reply':
      return <ReplyRequired {...props} />
    case 'priority':
      return <Priority {...props} />
    case 'completed-week':
      return <CompletedWeek {...props} />
    case 'tags':
      return <Tags {...props} />
    case 'recent':
      return <Recent {...props} />
  }
}
