import { Fragment, useEffect, useRef, useState, type CSSProperties, type DragEvent } from 'react'
import type { TaskItem } from '../api/tasks'
import type { Reschedule } from '../api/team'
import { addDaysIso, daysFrom, formatDueDate, formatRange, formatWeekday, isWeekend } from '../lib/dates'
import { avatar } from '../lib/avatar'
import { compareWithinDay, isOverdue } from '../lib/taskViews'
import Icon from './Icon'
import RescheduleForm from './RescheduleForm'

interface Props {
  /** The open tasks of the person shown. */
  tasks: TaskItem[]
  today: string
  onOpen: (task: TaskItem) => void
  /** Moves a task. Resolves to an error message, or null on success. */
  onReschedule: (task: TaskItem, change: Reschedule) => Promise<string | null>
}

/** A column: a day, or the overdue tasks (which nothing is dropped onto), or the tasks without a due date. */
interface Column {
  key: string
  day: string | null
  title: string
  subtitle?: string
  tone?: 'danger' | 'weekend' | 'today'
  droppable: boolean
  tasks: TaskItem[]
}

/** Where a card would land: a column, the levelled part of it or the part without levels, and a position. */
interface Target {
  column: string
  zone: 'levels' | 'free'
  index: number
}

interface Pending {
  task: TaskItem
  dueDate: string | null
  priorityLevel: number | null
  dropped: boolean
}

const DAYS = 7
const MAX_LEVEL = 20

function columnsFor(tasks: TaskItem[], from: string, today: string): Column[] {
  const columns: Column[] = []
  const overdue = tasks.filter((t) => isOverdue(t, today))
  if (overdue.length) {
    columns.push({
      key: 'overdue',
      day: null,
      title: 'Întârziate',
      tone: 'danger',
      droppable: false,
      tasks: overdue.sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? '') || compareWithinDay(a, b)),
    })
  }
  for (const day of daysFrom(from, DAYS)) {
    columns.push({
      key: day,
      day,
      title: day === today ? 'Azi' : day === addDaysIso(today, 1) ? 'Mâine' : capitalise(formatWeekday(day)),
      subtitle: formatDueDate(day),
      tone: day === today ? 'today' : isWeekend(day) ? 'weekend' : undefined,
      droppable: true,
      tasks: tasks.filter((t) => t.dueDate === day).sort(compareWithinDay),
    })
  }
  columns.push({
    key: 'none',
    day: null,
    title: 'Fără termen',
    droppable: true,
    tasks: tasks.filter((t) => !t.dueDate).sort(compareWithinDay),
  })
  return columns
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

/**
 * One person's open tasks as a board, a column per day. Dragging a card up or down within a day changes its level;
 * onto another day, its due date. Each move asks for a reason, which the person is emailed and which stays in the
 * task's history. Every card also has a button that opens the same form, for the keyboard and for touch screens.
 */
export default function TeamBoard({ tasks, today, onOpen, onReschedule }: Props) {
  const [from, setFrom] = useState(today)
  const [dragged, setDragged] = useState<TaskItem | null>(null)
  const [target, setTarget] = useState<Target | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  // "Fără termen" stays a narrow strip on the right until opened, or while a card is dragged.
  const [undatedOpen, setUndatedOpen] = useState(false)
  const columns = columnsFor(tasks, from, today)
  const undatedShown = undatedOpen || dragged !== null
  // Overdue and the week share the width; no scrolling sideways.
  // Weekends are usually empty: they take less room, unless they have work.
  const template = (min: number) =>
    columns
      .map((column) =>
        column.key === 'none'
          ? undatedShown ? `minmax(${min}px, 1fr)` : '40px'
          : column.tone === 'weekend' && column.tasks.length === 0 ? `minmax(${Math.round(min * 0.6)}px, 0.6fr)` : `minmax(${min}px, 1fr)`,
      )
      .join(' ')
  // On a wide screen everything fits; on a narrow one (a tablet) columns keep a readable width and the board scrolls.
  const boardStyle = { gridTemplateColumns: template(0), '--board-narrow': template(150) } as CSSProperties

  function over(e: DragEvent, column: Column, zone: Target['zone'], index: number) {
    if (!dragged || !column.droppable) return
    e.preventDefault()
    e.stopPropagation()
    e.dataTransfer.dropEffect = 'move'
    if (target?.column !== column.key || target.zone !== zone || target.index !== index) setTarget({ column: column.key, zone, index })
  }

  /** Over a card: before it in its upper half, after it in its lower half. */
  function overCard(e: DragEvent<HTMLElement>, column: Column, zone: Target['zone'], index: number) {
    const box = e.currentTarget.getBoundingClientRect()
    over(e, column, zone, e.clientY < box.top + box.height / 2 ? index : index + 1)
  }

  function drop(e: DragEvent, column: Column) {
    e.preventDefault()
    const task = dragged
    const place = target
    setDragged(null)
    setTarget(null)
    if (!task || !place || place.column !== column.key || !column.droppable) return

    let level: number | null = null
    if (place.zone === 'levels') {
      // The card takes the level after the card above it (1 at the top); a taken level pushes the cards below down.
      // Positions count the dragged card where it still is, so it is left out of what lies above.
      const above = column.tasks
        .filter((t) => t.priorityLevel != null)
        .slice(0, place.index)
        .filter((t) => t.id !== task.id)
        .at(-1)
      level = above ? (above.priorityLevel ?? 0) + 1 : 1
      if (level > MAX_LEVEL) {
        setNotice(`În ziua aceea nu mai e loc: nivelul ${MAX_LEVEL} e ultimul.`)
        return
      }
    }
    if (column.day === task.dueDate && level === task.priorityLevel) return
    setNotice(null)
    setPending({ task, dueDate: column.day, priorityLevel: level, dropped: true })
  }

  return (
    <div className="board">
      <div className="board-nav">
        <button
          type="button"
          className="button ghost small"
          onClick={() => setFrom(addDaysIso(from, -DAYS) < today ? today : addDaysIso(from, -DAYS))}
          disabled={from <= today}
        >
          <Icon name="arrowLeft" />
          Înapoi
        </button>
        <span className="board-range">{formatRange(from, addDaysIso(from, DAYS - 1))}</span>
        <button type="button" className="button ghost small" onClick={() => setFrom(addDaysIso(from, DAYS))}>
          Înainte
          <Icon name="arrowRight" />
        </button>
        {from !== today && (
          <button type="button" className="button ghost small" onClick={() => setFrom(today)}>
            Azi
          </button>
        )}
      </div>
      {notice && (
        <p className="error" role="alert">
          {notice}
        </p>
      )}

      <div className="board-columns" style={boardStyle}>
        {columns.map((column) => {
          if (column.key === 'none' && !undatedShown) {
            return (
              <button
                key={column.key}
                type="button"
                className="board-column board-column-folded"
                onClick={() => setUndatedOpen(true)}
                title="Arată task-urile fără termen"
                aria-label={`Fără termen, ${column.tasks.length} task-uri: arată-le`}
              >
                <span className="board-folded-count">{column.tasks.length}</span>
                <span className="board-folded-title">Fără termen</span>
              </button>
            )
          }
          const levelled = column.tasks.filter((t) => t.priorityLevel != null)
          const free = column.tasks.filter((t) => t.priorityLevel == null)
          const marker = (zone: Target['zone'], index: number) =>
            target?.column === column.key && target.zone === zone && target.index === index ? (
              <li className="board-marker" aria-hidden="true" />
            ) : null
          const card = (task: TaskItem, zone: Target['zone'], index: number) => (
            <BoardCard
              key={task.id}
              task={task}
              today={today}
              dragging={dragged?.id === task.id}
              onDragStart={() => {
                setNotice(null)
                setDragged(task)
              }}
              onDragEnd={() => {
                setDragged(null)
                setTarget(null)
              }}
              onDragOver={(e) => overCard(e, column, zone, index)}
              onOpen={() => onOpen(task)}
              onMove={() => setPending({ task, dueDate: task.dueDate, priorityLevel: task.priorityLevel, dropped: false })}
            />
          )
          return (
            <section
              key={column.key}
              className={['board-column', column.tone, dragged && column.droppable ? 'can-drop' : '', target?.column === column.key ? 'is-over' : '']
                .filter(Boolean)
                .join(' ')}
              aria-label={`${column.title}${column.subtitle ? `, ${column.subtitle}` : ''}`}
              onDragOver={(e) => over(e, column, 'free', free.length)}
              onDrop={(e) => drop(e, column)}
            >
              <header className="board-column-head">
                <h3>
                  {column.title}
                  {column.subtitle && <small>{column.subtitle}</small>}
                </h3>
                <span className="board-count" aria-label={`${column.tasks.length} task-uri`}>
                  {column.tasks.length}
                </span>
                {column.key === 'none' && undatedOpen && (
                  <button type="button" className="board-fold" onClick={() => setUndatedOpen(false)} aria-label="Strânge coloana Fără termen" title="Strânge">
                    <Icon name="chevronRight" size={14} />
                  </button>
                )}
              </header>
              <div className="board-column-body">
              {column.key !== 'overdue' && (
                <ol
                  className={`board-zone levels${levelled.length === 0 && !dragged ? ' empty' : ''}`}
                  aria-label="Cu nivel"
                  onDragOver={(e) => over(e, column, 'levels', levelled.length)}
                >
                  {levelled.map((task, index) => (
                    <Fragment key={task.id}>
                      {marker('levels', index)}
                      {card(task, 'levels', index)}
                    </Fragment>
                  ))}
                  {marker('levels', levelled.length)}
                  {levelled.length === 0 && dragged && <li className="board-hint">Aici, cu nivel</li>}
                </ol>
              )}
              <ul
                className={`board-zone free${levelled.length === 0 && !dragged ? ' alone' : ''}`}
                aria-label={column.key === 'overdue' ? 'Task-uri întârziate' : 'Fără nivel'}
                onDragOver={(e) => over(e, column, 'free', free.length)}
              >
                {column.key !== 'overdue' && (levelled.length > 0 || dragged) && <li className="board-zone-title">Fără nivel</li>}
                {(column.key === 'overdue' ? column.tasks : free).map((task, index) => card(task, 'free', index))}
                {column.key !== 'overdue' && target?.column === column.key && target.zone === 'free' && (
                  <li className="board-marker" aria-hidden="true" />
                )}
              </ul>
              </div>
            </section>
          )
        })}
      </div>

      {pending && (
        <MoveDialog
          pending={pending}
          onSubmit={(change) => onReschedule(pending.task, change)}
          onClose={() => setPending(null)}
        />
      )}
    </div>
  )
}

interface CardProps {
  task: TaskItem
  today: string
  dragging: boolean
  onDragStart: () => void
  onDragEnd: () => void
  onDragOver: (e: DragEvent<HTMLLIElement>) => void
  onOpen: () => void
  onMove: () => void
}

const PRIORITY: Record<TaskItem['priority'], { label: string; icon: 'priorityHigh' | 'priorityMedium' | 'priorityLow' }> = {
  high: { label: 'Prioritate ridicată', icon: 'priorityHigh' },
  medium: { label: 'Prioritate medie', icon: 'priorityMedium' },
  low: { label: 'Prioritate scăzută', icon: 'priorityLow' },
}

/**
 * A task as a card: its title, then a line of small labels (level, late, interval, on hold) and, on the right, its
 * priority and who gave it.
 */
function BoardCard({ task, today, dragging, onDragStart, onDragEnd, onDragOver, onOpen, onMove }: CardProps) {
  const overdue = isOverdue(task, today)
  const priority = PRIORITY[task.priority]
  const from = avatar({ id: task.createdBy.id, name: task.createdBy.displayName })
  // The board is one person's: an avatar only says who gave them the task, when it was someone else.
  const givenBy = task.assignee !== null && task.assignee.id !== task.createdBy.id
  return (
    <li
      className={['board-card', dragging ? 'is-dragging' : ''].filter(Boolean).join(' ')}
      draggable
      onDragStart={(e) => {
        e.dataTransfer.effectAllowed = 'move'
        e.dataTransfer.setData('text/plain', task.id)
        onDragStart()
      }}
      onDragEnd={onDragEnd}
      onDragOver={onDragOver}
    >
      <button type="button" className="board-card-open" onClick={onOpen}>
        <span className="board-card-title">{task.title}</span>
      </button>
      <div className="board-card-foot">
        <span className="board-card-labels">
          {task.priorityLevel != null && (
            <span className="board-lozenge level" title={`Nivelul ${task.priorityLevel}: ordinea în zi`}>
              <span className="board-lozenge-word">Nivel </span>
              {task.priorityLevel}
            </span>
          )}
          {overdue && task.dueDate && <span className="board-lozenge danger">{formatDueDate(task.dueDate)}</span>}
          {task.startDate && task.dueDate && task.startDate !== task.dueDate && !overdue && (
            <span className="board-lozenge">{formatRange(task.startDate, task.dueDate)}</span>
          )}
          {task.state === 'waiting' && (
            <span className="board-lozenge warn" title={`În așteptare: ${task.waitingReason ?? ''}`}>
              În așteptare
            </span>
          )}
        </span>
        {/* Medium is most tasks: only what stands out is marked. */}
        {task.priority !== 'medium' && (
          <span className={`board-priority ${task.priority}`} title={priority.label}>
            <Icon name={priority.icon} size={14} />
            <span className="visually-hidden">{priority.label}</span>
          </span>
        )}
        {givenBy && (
          <span className="board-avatar" style={{ background: from.colour }} title={`De la ${task.createdBy.displayName}`}>
            {from.letters}
          </span>
        )}
      </div>
      <button type="button" className="board-card-move" onClick={onMove} aria-label={`Mută „${task.title}”`} title="Mută: alt termen sau alt nivel">
        <Icon name="forward" size={14} />
      </button>
    </li>
  )
}

/** Asks for the reason of a move; the day and level can still be changed in it. */
function MoveDialog({ pending, onSubmit, onClose }: { pending: Pending; onSubmit: (change: Reschedule) => Promise<string | null>; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  return (
    <dialog ref={dialog} className="task-dialog move-dialog" aria-labelledby="move-dialog-title" onClose={onClose}>
      <header className="task-dialog-head">
        <h2 id="move-dialog-title">Mută „{pending.task.title}”</h2>
        <button type="button" className="dialog-close" onClick={() => dialog.current?.close()} aria-label="Închide">
          <Icon name="close" />
        </button>
      </header>
      <div className="move-dialog-body">
        <RescheduleForm
          task={pending.task}
          dueDate={pending.dueDate}
          priorityLevel={pending.priorityLevel}
          focusReason={pending.dropped}
          onCancel={() => dialog.current?.close()}
          onSubmit={async (change) => {
            const failure = await onSubmit(change)
            if (!failure) dialog.current?.close()
            return failure
          }}
        />
      </div>
    </dialog>
  )
}
