import { useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import { teamApi, type CalendarMember, type CalendarTask, type TeamCalendar as CalendarData } from '../api/team'
import {
  addDaysIso,
  dayOfMonth,
  daysFrom,
  formatLongDate,
  formatRange,
  formatWeekday,
  isWeekend,
  mondayOf,
} from '../lib/dates'
import { tasksLabel } from '../lib/text'
import Icon from './Icon'

const WEEKS = 4
const DAYS = WEEKS * 7
const MAX_PILLS = 3

interface Props {
  /** The department to show; null lets the server use the viewer's own. */
  department: string | null
  today: string
  onSessionExpired: () => void
}

/** Whether a task occupies a given day: its interval, or its due date when it has no start date. */
const occupies = (task: CalendarTask, day: string) => (task.startDate ?? task.dueDate) <= day && day <= task.dueDate

const tasksOn = (member: CalendarMember, day: string) => member.tasks.filter((task) => occupies(task, day))

/** Shade for a day's task count. Fixed steps, so the same number looks the same for everyone. */
function loadLevel(count: number): 0 | 1 | 2 | 3 | 4 {
  if (count === 0) return 0
  if (count === 1) return 1
  if (count === 2) return 2
  if (count <= 4) return 3
  return 4
}

const LEGEND: { level: 1 | 2 | 3 | 4; label: string }[] = [
  { level: 1, label: '1' },
  { level: 2, label: '2' },
  { level: 3, label: '3–4' },
  { level: 4, label: '5+' },
]

/**
 * The team's load as a calendar: how many open tasks each person has on each day of a four-week
 * window, or one person's tasks day by day.
 */
export default function TeamCalendar({ department, today, onSessionExpired }: Props) {
  const [start, setStart] = useState(() => mondayOf(today))
  const [calendar, setCalendar] = useState<CalendarData | null>(null)
  const [error, setError] = useState(false)
  const [personId, setPersonId] = useState('')
  const [selected, setSelected] = useState<{ userId: string; day: string } | null>(null)

  const end = addDaysIso(start, DAYS - 1)

  useEffect(() => {
    let cancelled = false
    teamApi
      .calendar(start, end, department)
      .then((loaded) => {
        if (cancelled) return
        setCalendar(loaded)
        setError(false)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError(true)
      })
    return () => {
      cancelled = true
    }
  }, [start, end, department, onSessionExpired])

  function move(weeks: number) {
    setSelected(null)
    setStart((current) => addDaysIso(current, weeks * 7))
  }

  if (error) {
    return (
      <p className="error" role="alert">
        Nu am putut încărca calendarul echipei.
      </p>
    )
  }
  if (!calendar) return <p className="muted">Se încarcă…</p>

  const days = daysFrom(start, DAYS)
  const person = calendar.members.find((member) => member.user.id === personId) ?? null
  const selectedMember = selected ? calendar.members.find((m) => m.user.id === selected.userId) : null
  const selectedTasks = selected && selectedMember ? tasksOn(selectedMember, selected.day) : []

  const dayClass = (day: string) => `${isWeekend(day) ? ' weekend' : ''}${day === today ? ' today' : ''}`

  return (
    <div className="calendar">
      <div className="calendar-bar">
        <div className="calendar-nav">
          <button type="button" className="button ghost" onClick={() => move(-WEEKS)} aria-label="Cele 4 săptămâni dinainte">
            <Icon name="arrowLeft" />
          </button>
          <button
            type="button"
            className="button ghost"
            onClick={() => {
              setSelected(null)
              setStart(mondayOf(today))
            }}
          >
            Azi
          </button>
          <button type="button" className="button ghost" onClick={() => move(WEEKS)} aria-label="Următoarele 4 săptămâni">
            <Icon name="arrowRight" />
          </button>
          <strong className="calendar-period">{formatRange(start, end)}</strong>
        </div>
        <label className="team-department">
          Arată
          <select
            value={person ? personId : ''}
            onChange={(e) => {
              setSelected(null)
              setPersonId(e.target.value)
            }}
            aria-label="Persoană"
          >
            <option value="">Toată echipa</option>
            {calendar.members.map((member) => (
              <option key={member.user.id} value={member.user.id}>
                {member.user.displayName}
              </option>
            ))}
          </select>
        </label>
      </div>

      {calendar.members.length === 0 ? (
        <p className="muted">Nu există persoane în acest departament.</p>
      ) : person ? (
        <PersonCalendar
          member={person}
          days={days}
          today={today}
          selectedDay={selected?.day ?? null}
          onSelect={(day) => setSelected({ userId: person.user.id, day })}
        />
      ) : (
        <div className="team-table-wrap">
          <table className="heatmap">
            <caption className="visually-hidden">
              Numărul de task-uri deschise pe fiecare persoană și zi, {formatRange(start, end)}
            </caption>
            <thead>
              <tr>
                <th scope="col" className="heat-person">
                  Persoană
                </th>
                {days.map((day) => (
                  <th key={day} scope="col" className={`heat-day${dayClass(day)}`} title={formatLongDate(day)}>
                    <span>{formatWeekday(day).charAt(0).toUpperCase()}</span>
                    {dayOfMonth(day)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {calendar.members.map((member) => (
                <tr key={member.user.id}>
                  <th scope="row" className="heat-person">
                    <button
                      type="button"
                      className="link-plain"
                      onClick={() => {
                        setSelected(null)
                        setPersonId(member.user.id)
                      }}
                      title={`Vezi calendarul: ${member.user.displayName}`}
                    >
                      {member.user.displayName}
                    </button>
                    {member.unscheduled > 0 && <small className="muted">{member.unscheduled} fără termen</small>}
                  </th>
                  {days.map((day) => {
                    const count = tasksOn(member, day).length
                    const isSelected = selected?.userId === member.user.id && selected.day === day
                    return (
                      <td key={day} className={`heat-cell${dayClass(day)}`}>
                        <button
                          type="button"
                          className={`heat level-${loadLevel(count)}${isSelected ? ' selected' : ''}`}
                          disabled={count === 0}
                          onClick={() => setSelected({ userId: member.user.id, day })}
                          title={`${member.user.displayName}, ${formatLongDate(day)}: ${tasksLabel(count)}`}
                        >
                          {count > 0 ? count : ''}
                          <span className="visually-hidden">
                            {count === 0 ? '0' : ''} task-uri, {formatLongDate(day)}
                          </span>
                        </button>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="calendar-legend">
        {!person && (
          <>
            <span className="muted">Task-uri pe zi:</span>
            {LEGEND.map(({ level, label }) => (
              <span key={level} className="legend-item">
                <span className={`heat level-${level} swatch`} aria-hidden="true" />
                {label}
              </span>
            ))}
          </>
        )}
        <span className="muted legend-note">
          Un task ocupă zilele din intervalul lui sau, fără dată de început, doar ziua termenului. Apar doar task-urile
          deschise.
        </span>
      </div>

      {selected && selectedMember && (
        <section className="calendar-detail" aria-live="polite">
          <h3>
            {selectedMember.user.displayName} · {formatLongDate(selected.day)} · {tasksLabel(selectedTasks.length)}
          </h3>
          {selectedTasks.length === 0 ? (
            <p className="muted">Niciun task în această zi.</p>
          ) : (
            <ul>
              {selectedTasks.map((task) => (
                <li key={task.id}>
                  {task.priority === 'high' && (
                    <span className="line-flag" title="Prioritate ridicată">
                      <Icon name="flag" size={13} />
                      <span className="visually-hidden">Prioritate ridicată: </span>
                    </span>
                  )}
                  <span className="line-title">{task.title}</span>
                  <span className="line-meta">{formatRange(task.startDate ?? task.dueDate, task.dueDate)}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  )
}

function PersonCalendar({
  member,
  days,
  today,
  selectedDay,
  onSelect,
}: {
  member: CalendarMember
  days: string[]
  today: string
  selectedDay: string | null
  onSelect: (day: string) => void
}) {
  return (
    <>
      <div className="month" role="grid" aria-label={`Calendarul task-urilor: ${member.user.displayName}`}>
        {days.slice(0, 7).map((day) => (
          <div key={day} className="month-head" role="columnheader">
            {formatWeekday(day)}
          </div>
        ))}
        {days.map((day) => {
          const tasks = tasksOn(member, day)
          const extra = tasks.length - MAX_PILLS
          return (
            <button
              key={day}
              type="button"
              role="gridcell"
              className={`month-day level-${loadLevel(tasks.length)}${isWeekend(day) ? ' weekend' : ''}${day === today ? ' today' : ''}${day === selectedDay ? ' selected' : ''}`}
              onClick={() => onSelect(day)}
              title={`${formatLongDate(day)}: ${tasksLabel(tasks.length)}`}
            >
              <span className="month-date">
                {dayOfMonth(day)}
                {tasks.length > 0 && <span className="month-count">{tasks.length}</span>}
              </span>
              {tasks.slice(0, MAX_PILLS).map((task) => (
                <span key={task.id} className={`pill${task.priority === 'high' ? ' high' : ''}`}>
                  {task.title}
                </span>
              ))}
              {extra > 0 && <span className="pill more">+{extra}</span>}
            </button>
          )
        })}
      </div>
      {member.unscheduled > 0 && (
        <p className="muted">
          {member.user.displayName} mai are {tasksLabel(member.unscheduled)} fără termen, care nu apar în calendar.
        </p>
      )}
    </>
  )
}
