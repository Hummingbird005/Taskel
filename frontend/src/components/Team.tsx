import { useEffect, useState } from 'react'
import type { User } from '../api/auth'
import { ApiError } from '../api/client'
import type { TaskItem } from '../api/tasks'
import { teamApi, type Team as TeamData, type TeamMember } from '../api/team'
import { countLabel, tasksLabel } from '../lib/text'
import Icon, { type IconName } from './Icon'
import ReadOnlyTask from './ReadOnlyTask'

interface Props {
  user: User
  today: string
  onSessionExpired: () => void
}

type MemberTasks = { status: 'loading' } | { status: 'error' } | { status: 'ready'; tasks: TaskItem[] }

const ROLE_LABEL = { user: '', manager: 'Manager', admin: 'Administrator' } as const

/** The manager's screen: how loaded each person in the department is, and what they are working on. */
export default function Team({ user, today, onSessionExpired }: Props) {
  const [team, setTeam] = useState<TeamData | null>(null)
  const [department, setDepartment] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [openMember, setOpenMember] = useState<string | null>(null)
  const [memberTasks, setMemberTasks] = useState<MemberTasks>({ status: 'loading' })

  useEffect(() => {
    let cancelled = false
    teamApi
      .get(today, department)
      .then((loaded) => {
        if (cancelled) return
        setTeam(loaded)
        setError(null)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else if (err instanceof ApiError && err.status === 403) setError('Nu ai acces la statisticile echipei.')
        else setError('Nu am putut încărca statisticile echipei.')
      })
    return () => {
      cancelled = true
    }
  }, [today, department, onSessionExpired])

  function toggleMember(member: TeamMember) {
    if (openMember === member.user.id) {
      setOpenMember(null)
      return
    }
    setOpenMember(member.user.id)
    setMemberTasks({ status: 'loading' })
    teamApi
      .memberTasks(member.user.id)
      .then((tasks) => setMemberTasks({ status: 'ready', tasks }))
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setMemberTasks({ status: 'error' })
      })
  }

  if (error) {
    return (
      <p className="empty error" role="alert">
        {error}
      </p>
    )
  }
  if (!team) return <p className="empty">Se încarcă…</p>

  const canChooseDepartment = user.role === 'admin' && team.departments.length > 0
  const departmentPicker = canChooseDepartment && (
    <label className="team-department">
      Departament
      <select
        value={team.department ?? ''}
        onChange={(e) => {
          setOpenMember(null)
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
  )

  if (!team.department) {
    return (
      <div className="team">
        {departmentPicker}
        <p className="empty">
          {user.role === 'admin'
            ? team.departments.length > 0
              ? 'Alege un departament ca să îi vezi încărcarea.'
              : 'Niciun utilizator nu are încă un departament. Setează departamentele din „Utilizatori”.'
            : 'Nu ai un departament setat, așa că nu există o echipă de afișat. Cere unui administrator să îl seteze.'}
        </p>
      </div>
    )
  }

  const { totals, members } = team
  const maxOpen = Math.max(1, ...members.map((m) => m.stats.open))

  const tile = (label: string, value: number, icon: IconName, tone?: 'danger') => (
    <div className={`stat static${tone ? ` ${tone}` : ''}`}>
      <span className="stat-label">
        <Icon name={icon} size={15} />
        {label}
      </span>
      <span className="stat-value">{value}</span>
    </div>
  )

  return (
    <div className="team">
      <div className="team-head">
        <div>
          <h2>{team.department}</h2>
          <p className="muted">
            {countLabel(members.length, 'persoană', 'persoane')} · {tasksLabel(totals.open)}{' '}
            {totals.open === 1 ? 'deschis' : 'deschise'}
          </p>
        </div>
        {departmentPicker}
      </div>

      <div className="stats">
        {tile('Deschise', totals.open, 'inbox')}
        {tile('Întârziate', totals.overdue, 'alert', totals.overdue > 0 ? 'danger' : undefined)}
        {tile('Cu termen azi', totals.dueToday, 'sun')}
        {tile('Rezolvate, 7 zile', totals.completedLast7Days, 'checkCircle')}
        {tile('Rezolvate, 30 de zile', totals.completedLast30Days, 'checkCircle')}
      </div>

      <section className="team-table-wrap" aria-label="Încărcarea pe persoane">
        <table className="team-table">
          <thead>
            <tr>
              <th scope="col">Persoană</th>
              <th scope="col" className="load-col">
                Deschise
              </th>
              <th scope="col" className="num">
                Întârziate
              </th>
              <th scope="col" className="num">
                Azi
              </th>
              <th scope="col" className="num">
                Următoarele 7 zile
              </th>
              <th scope="col" className="num">
                Prioritate ridicată
              </th>
              <th scope="col" className="num">
                Rezolvate, 7 zile
              </th>
              <th scope="col" className="num">
                Rezolvate, 30 de zile
              </th>
            </tr>
          </thead>
          <tbody>
            {members.map((member) => {
              const { stats } = member
              const isOpen = openMember === member.user.id
              return [
                <tr key={member.user.id} className={isOpen ? 'open' : ''}>
                  <th scope="row">
                    <button
                      type="button"
                      className="member-toggle"
                      aria-expanded={isOpen}
                      onClick={() => toggleMember(member)}
                      title={`Vezi task-urile: ${member.user.displayName}`}
                    >
                      <Icon name={isOpen ? 'chevronDown' : 'chevronRight'} />
                      <span className="member-name">
                        {member.user.displayName}
                        {ROLE_LABEL[member.role] && <small className="muted">{ROLE_LABEL[member.role]}</small>}
                      </span>
                    </button>
                  </th>
                  <td className="load-col">
                    <span className="load">
                      <span className="meter-track" aria-hidden="true">
                        <span className="meter-bar" style={{ width: `${(stats.open / maxOpen) * 100}%` }} />
                      </span>
                      <span className="meter-figure">{stats.open}</span>
                    </span>
                  </td>
                  <td className="num">
                    {stats.overdue > 0 ? (
                      <span className="chip overdue">
                        <Icon name="alert" size={13} />
                        {stats.overdue}
                        <span className="visually-hidden">{stats.overdue === 1 ? ' întârziat' : ' întârziate'}</span>
                      </span>
                    ) : (
                      0
                    )}
                  </td>
                  <td className="num">{stats.dueToday}</td>
                  <td className="num">{stats.dueNext7Days}</td>
                  <td className="num">{stats.highPriorityOpen}</td>
                  <td className="num">{stats.completedLast7Days}</td>
                  <td className="num">{stats.completedLast30Days}</td>
                </tr>,
                isOpen && (
                  <tr key={`${member.user.id}-tasks`} className="member-tasks">
                    <td colSpan={8}>
                      <MemberTaskList state={memberTasks} today={today} name={member.user.displayName} />
                    </td>
                  </tr>
                ),
              ]
            })}
          </tbody>
        </table>
      </section>
      <p className="muted team-note">
        Fiecărei persoane i se numără task-urile de care răspunde: cele alocate ei și cele proprii, nealocate altcuiva.
        Poți doar să le citești.
      </p>
    </div>
  )
}

function MemberTaskList({ state, today, name }: { state: MemberTasks; today: string; name: string }) {
  if (state.status === 'loading') return <p className="muted">Se încarcă…</p>
  if (state.status === 'error') return <p className="error">Nu am putut încărca task-urile.</p>

  const open = state.tasks.filter((t) => !t.isCompleted)
  const completed = state.tasks
    .filter((t) => t.isCompleted)
    .sort((a, b) => (b.completedAt ?? '').localeCompare(a.completedAt ?? ''))
    .slice(0, 10)
  if (state.tasks.length === 0) return <p className="muted">{name} nu are niciun task.</p>

  return (
    <div className="member-task-lists">
      <h3>Deschise ({open.length})</h3>
      {open.length === 0 ? (
        <p className="muted">Niciun task deschis.</p>
      ) : (
        <ul className="task-list">
          {open.map((task) => (
            <ReadOnlyTask key={task.id} task={task} today={today} />
          ))}
        </ul>
      )}
      {completed.length > 0 && (
        <>
          <h3>Ultimele finalizate</h3>
          <ul className="task-list">
            {completed.map((task) => (
              <ReadOnlyTask key={task.id} task={task} today={today} />
            ))}
          </ul>
        </>
      )}
    </div>
  )
}
