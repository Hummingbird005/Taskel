import { useEffect, useMemo, useState } from 'react'
import type { User } from '../api/auth'
import { attachmentsApi } from '../api/attachments'
import { ApiError } from '../api/client'
import type { TaskItem } from '../api/tasks'
import { teamApi, type Team as TeamData } from '../api/team'
import { addDaysIso, formatLongDate } from '../lib/dates'
import { compareWithinDay, isOverdue } from '../lib/taskViews'
import EmptyState from './ui/EmptyState'
import { TaskListSkeleton } from './ui/Skeleton'
import TaskDetail from './TaskDetail'
import TaskRow from './TaskRow'

interface Props {
  user: User
  today: string
  onSessionExpired: () => void
}

interface DayGroup {
  key: string
  label: string
  tone?: 'danger'
  tasks: TaskItem[]
}

/** Who answers for a task in the team's list: the person it is assigned to, or its creator. */
const responsibleOf = (task: TaskItem) => task.assignee ?? task.createdBy

/**
 * The open tasks by day: overdue first, then each day with tasks on it, then those without a due date, then the
 * recently completed. Within a day, in the order the day's work is done.
 */
function groupByDay(tasks: TaskItem[], today: string): DayGroup[] {
  const open = tasks.filter((t) => !t.isCompleted)
  const tomorrow = addDaysIso(today, 1)
  const groups: DayGroup[] = []
  const overdue = open.filter((t) => isOverdue(t, today)).sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? '') || compareWithinDay(a, b))
  if (overdue.length) groups.push({ key: 'overdue', label: 'Întârziate', tone: 'danger', tasks: overdue })

  const days = [...new Set(open.filter((t) => t.dueDate && t.dueDate >= today).map((t) => t.dueDate as string))].sort()
  for (const day of days) {
    const label = day === today ? 'Azi' : day === tomorrow ? 'Mâine' : formatLongDate(day)
    groups.push({ key: day, label, tasks: open.filter((t) => t.dueDate === day).sort(compareWithinDay) })
  }
  const undated = open.filter((t) => !t.dueDate).sort(compareWithinDay)
  if (undated.length) groups.push({ key: 'none', label: 'Fără termen', tasks: undated })
  const done = tasks
    .filter((t) => t.isCompleted)
    .sort((a, b) => (b.completedAt ?? '').localeCompare(a.completedAt ?? ''))
  if (done.length) groups.push({ key: 'done', label: 'Finalizate în ultimele 2 săptămâni', tasks: done })
  return groups
}

/**
 * For managers: every task the people of their department answer for, by day, with a filter by person. A task
 * opens for reading, and its due date can be moved.
 */
export default function TeamTasks({ user, today, onSessionExpired }: Props) {
  const [department, setDepartment] = useState<string | null>(null)
  const [team, setTeam] = useState<TeamData | null>(null)
  const [tasks, setTasks] = useState<TaskItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [person, setPerson] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([teamApi.get(today, department), teamApi.tasks(department)])
      .then(([teamData, list]) => {
        if (cancelled) return
        setTeam(teamData)
        setTasks(list)
        setError(null)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca task-urile echipei.')
      })
    return () => {
      cancelled = true
    }
  }, [today, department, onSessionExpired])

  const shown = useMemo(
    () => (tasks ?? []).filter((t) => !person || responsibleOf(t).id === person),
    [tasks, person],
  )
  const groups = useMemo(() => groupByDay(shown, today), [shown, today])
  const openTask = tasks?.find((t) => t.id === openId) ?? null

  async function changeDueDate(task: TaskItem, dueDate: string | null): Promise<string | null> {
    try {
      const updated = await teamApi.changeDueDate(task.id, dueDate)
      setTasks((prev) => prev?.map((t) => (t.id === updated.id ? updated : t)) ?? null)
      return null
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onSessionExpired()
        return null
      }
      if (err instanceof ApiError && err.status === 400) return 'Termenul nu poate fi înaintea datei de început a task-ului.'
      if (err instanceof ApiError && err.status === 409) return 'Task-ul este finalizat; termenul nu se mai poate schimba.'
      return 'Nu am putut schimba termenul.'
    }
  }

  if (error) {
    return (
      <p className="error" role="alert">
        {error}
      </p>
    )
  }
  if (!team || !tasks) return <TaskListSkeleton rows={6} />

  const canChooseDepartment = user.role === 'admin' && team.departments.length > 0
  return (
    <div className="team-tasks">
      <div className="team-tasks-toolbar">
        {canChooseDepartment && (
          <label className="team-department">
            <span>Departament</span>
            <select
              value={team.department ?? ''}
              onChange={(e) => {
                setPerson('')
                setDepartment(e.target.value || null)
              }}
              aria-label="Departament"
            >
              {!team.department && <option value="">Alege un departament</option>}
              {team.departments.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </label>
        )}
        {team.members.length > 0 && (
          <label className="team-department">
            <span>Persoana</span>
            <select value={person} onChange={(e) => setPerson(e.target.value)} aria-label="Persoana">
              <option value="">Toată echipa</option>
              {team.members.map((m) => (
                <option key={m.user.id} value={m.user.id}>
                  {m.user.displayName} ({m.stats.open})
                </option>
              ))}
            </select>
          </label>
        )}
        <p className="muted team-tasks-hint">
          Deschide un task ca să-l citești sau să-i muți termenul. Persoana responsabilă primește un email.
        </p>
      </div>

      {!team.department ? (
        <EmptyState icon="users" title="Niciun departament">
          {user.role === 'admin' ? 'Alege un departament.' : 'Contul tău nu are un departament setat.'}
        </EmptyState>
      ) : groups.length === 0 ? (
        <EmptyState icon="inbox" title="Niciun task">
          {person ? 'Persoana aleasă nu are task-uri.' : 'Nimeni din echipă nu are task-uri.'}
        </EmptyState>
      ) : (
        groups.map((group) => (
          <section key={group.key} className="task-group">
            <h2 className={['divider', group.tone].filter(Boolean).join(' ')}>
              <span>
                {group.label} <span className="divider-count">{group.tasks.length}</span>
              </span>
            </h2>
            <ul className="task-list">
              {group.tasks.map((task) => (
                <TaskRow key={task.id} task={task} today={today} onOpen={() => setOpenId(task.id)} showHolder />
              ))}
            </ul>
          </section>
        ))
      )}

      {openTask && (
        <TaskDetail
          task={openTask}
          today={today}
          attachmentUrl={(file) => attachmentsApi.memberDownloadUrl(responsibleOf(openTask).id, openTask.id, file.id)}
          onChangeDueDate={(dueDate) => changeDueDate(openTask, dueDate)}
          onClose={() => setOpenId(null)}
        />
      )}
    </div>
  )
}
