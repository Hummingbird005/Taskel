import { useEffect, useRef } from 'react'
import type { TaskItem } from '../api/tasks'
import { formatDueDate } from '../lib/dates'
import type { View } from '../lib/taskViews'
import Icon from './Icon'

interface Props {
  overdue: TaskItem[]
  dueToday: TaskItem[]
  onOpen: (view: View) => void
  onClose: () => void
}

const MAX_LISTED = 5

/** Shown when the app is opened and tasks are overdue or due today. */
export default function DueAlert({ overdue, dueToday, onOpen, onClose }: Props) {
  const dialog = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  function list(tasks: TaskItem[], meta: (task: TaskItem) => string) {
    const more = tasks.length - MAX_LISTED
    return (
      <ul className="alert-list">
        {tasks.slice(0, MAX_LISTED).map((task) => (
          <li key={task.id}>
            <span className="line-title">{task.title}</span>
            <span className="line-meta">{meta(task)}</span>
          </li>
        ))}
        {more > 0 && <li className="muted">and {more} more</li>}
      </ul>
    )
  }

  return (
    <dialog ref={dialog} className="due-alert" aria-labelledby="due-alert-title" onClose={onClose}>
      <div className="alert-head">
        <span className={`alert-icon${overdue.length > 0 ? ' danger' : ''}`}>
          <Icon name="bell" size={22} />
        </span>
        <div>
          <h2 id="due-alert-title">Tasks that need your attention</h2>
          <p className="muted">
            {overdue.length > 0 && `${overdue.length} overdue`}
            {overdue.length > 0 && dueToday.length > 0 && ' · '}
            {dueToday.length > 0 && `${dueToday.length} due today`}
          </p>
        </div>
      </div>

      {overdue.length > 0 && (
        <section className="alert-section danger">
          <h3>
            <Icon name="alert" /> Overdue
          </h3>
          {list(overdue, (task) => (task.dueDate ? `was due ${formatDueDate(task.dueDate)}` : ''))}
        </section>
      )}
      {dueToday.length > 0 && (
        <section className="alert-section">
          <h3>
            <Icon name="sun" /> Due today
          </h3>
          {list(dueToday, (task) => (task.priority === 'high' ? 'High priority' : ''))}
        </section>
      )}

      <div className="alert-actions">
        {overdue.length > 0 && (
          <button type="button" className="button primary" onClick={() => onOpen({ kind: 'overdue' })}>
            View overdue
          </button>
        )}
        {dueToday.length > 0 && (
          <button
            type="button"
            className={`button ${overdue.length > 0 ? 'ghost' : 'primary'}`}
            onClick={() => onOpen({ kind: 'today' })}
          >
            View today
          </button>
        )}
        <button type="button" className="button ghost" onClick={onClose}>
          Dismiss
        </button>
      </div>
      <p className="alert-note muted">You can turn this reminder off under Home → Customize.</p>
    </dialog>
  )
}
