import { useEffect, useMemo, useState } from 'react'
import type { User } from '../api/auth'
import { ApiError } from '../api/client'
import { DEFAULT_DASHBOARD_SETTINGS, dashboardApi, type DashboardSettings } from '../api/dashboard'
import { attachmentsApi, DEFAULT_ATTACHMENT_LIMITS, uploadErrorText, type AttachmentLimits } from '../api/attachments'
import { groupsApi, type GroupListItem } from '../api/groups'
import { tasksApi, type TaskDraft, type TaskItem, type TaskTarget } from '../api/tasks'
import { usersApi, type UserSummary } from '../api/users'
import { todayIso } from '../lib/dates'
import { tasksLabel } from '../lib/text'
import {
  emptyMessage,
  emptyTitle,
  groupTasks,
  isDelegated,
  isMine,
  isOverdue,
  matchesSearch,
  PAGE_VIEWS,
  tagCounts,
  tasksInView,
  viewHint,
  viewTitle,
  type SortBy,
  type View,
} from '../lib/taskViews'
import Dashboard from './Dashboard'
import EmailNotifications from './EmailNotifications'
import NewTaskDialog from './NewTaskDialog'
import Groups from './Groups'
import DueAlert from './DueAlert'
import Icon from './Icon'
import Banner from './ui/Banner'
import Button from './ui/Button'
import EmptyState from './ui/EmptyState'
import { CardsSkeleton, TaskListSkeleton } from './ui/Skeleton'
import Integrations from './Integrations'
import Sidebar from './Sidebar'
import TaskDetail, { type TaskActions } from './TaskDetail'
import Team from './Team'
import AdminUsers from './AdminUsers'
import SystemSettings from './SystemSettings'
import TaskRow from './TaskRow'

/** What to tell the user when completing an email task with a reply failed. */
function replyErrorText(err: unknown): string {
  if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel. Task-ul nu a fost finalizat.'
  switch (err.status) {
    case 503:
      return 'Trimiterea de emailuri nu este configurată pe serverul Taskel, deci răspunsul nu poate fi trimis. Task-ul nu a fost finalizat.'
    case 502:
      return 'Emailul de răspuns nu a putut fi trimis. Task-ul nu a fost finalizat. Încearcă din nou.'
    case 409:
      return 'Task-ul este deja finalizat.'
    case 400:
      return 'Răspunsul este obligatoriu și poate avea cel mult 4000 de caractere.'
    default:
      return 'A apărut o eroare. Task-ul nu a fost finalizat.'
  }
}

interface Props {
  user: User
  /** Called when the signed-in account itself was changed (its role or department). */
  onUserChanged: () => void
  onLogout: () => void
  onSessionExpired: () => void
}

/** The signed-in app: sidebar with views, the dashboard or task list for the selected view, and the composer. */
export default function Workspace({ user, onUserChanged, onLogout, onSessionExpired }: Props) {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [view, setView] = useState<View>({ kind: 'dashboard' })
  const [sortBy, setSortBy] = useState<SortBy>('dueDate')
  const [search, setSearch] = useState('')
  // The task opened in its dialog. Looked up in `tasks` on each render, so the dialog shows every change.
  const [openId, setOpenId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [groups, setGroups] = useState<GroupListItem[]>([])
  const [attachmentLimits, setAttachmentLimits] = useState<AttachmentLimits>(DEFAULT_ATTACHMENT_LIMITS)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [settings, setSettings] = useState<DashboardSettings>(DEFAULT_DASHBOARD_SETTINGS)
  const [customizing, setCustomizing] = useState(false)
  const [alertOpen, setAlertOpen] = useState(false)
  const [people, setPeople] = useState<UserSummary[]>([])
  const [notice, setNotice] = useState<string | null>(null)

  const today = todayIso()

  /** Shows the failure, or returns to the sign-in screen when the session is gone. */
  function handleFailure(err: unknown, fallback: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else setError(fallback)
  }

  /** Reloads tasks and groups after something changed that this page cannot work out by itself. */
  async function refresh() {
    try {
      const [loaded, loadedGroups] = await Promise.all([tasksApi.list(), groupsApi.list().catch(() => groups)])
      setTasks(loaded)
      setGroups(loadedGroups)
    } catch (err) {
      handleFailure(err, 'Nu am putut reîncărca task-urile.')
    }
  }

  useEffect(() => {
    let cancelled = false

    function reload() {
      tasksApi
        .list()
        .then((loaded) => {
          if (cancelled) return
          setTasks(loaded)
          setError(null)
        })
        .catch((err) => {
          if (cancelled) return
          if (err instanceof ApiError && err.status === 401) onSessionExpired()
          else setError('Nu am putut încărca task-urile. Verifică dacă serverul este pornit.')
        })
    }

    // First load: tasks and dashboard preferences together, so the reminder can be decided once.
    // Preferences that fail to load fall back to the defaults rather than blocking the app.
    // The list of people is only needed for assigning, so the app still opens without it.
    Promise.all([
      tasksApi.list(),
      dashboardApi.get().catch(() => DEFAULT_DASHBOARD_SETTINGS),
      usersApi.list().catch(() => [] as UserSummary[]),
      groupsApi.list().catch(() => [] as GroupListItem[]),
      attachmentsApi.limits().catch(() => DEFAULT_ATTACHMENT_LIMITS),
    ])
      .then(([loaded, loadedSettings, loadedPeople, loadedGroups, loadedLimits]) => {
        if (cancelled) return
        setTasks(loaded)
        setSettings(loadedSettings)
        setPeople(loadedPeople.filter((person) => person.id !== user.id))
        setGroups(loadedGroups)
        setAttachmentLimits(loadedLimits)
        const now = todayIso()
        // Only the user's own work calls for the reminder, not what they handed to others.
        const needsAttention = loaded.some(
          (t) => isMine(t) && !t.isCompleted && t.dueDate !== null && t.dueDate <= now,
        )
        setAlertOpen(loadedSettings.showDueAlert && needsAttention)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca task-urile. Verifică dacă serverul este pornit.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    // Tasks can be added from outside this page (e.g. the Outlook macro),
    // so pick them up when the user comes back to it.
    window.addEventListener('focus', reload)
    return () => {
      cancelled = true
      window.removeEventListener('focus', reload)
    }
  }, [onSessionExpired, user.id])

  // The user's own work, and what they assigned to other people. Views, counts, the dashboard
  // and the reminder are about the first; the second has its own view.
  const mine = useMemo(() => tasks.filter(isMine), [tasks])
  const delegated = useMemo(() => tasks.filter(isDelegated), [tasks])
  // Tasks waiting on the user's groups, and tasks they passed on and can still read.
  const claimable = useMemo(() => tasks.filter((t) => t.access === 'group'), [tasks])
  const forwarded = useMemo(() => tasks.filter((t) => t.access === 'viewer'), [tasks])

  const counts = useMemo(() => {
    const open = mine.filter((t) => !t.isCompleted)
    return {
      all: open.length,
      today: open.filter((t) => t.dueDate === today).length,
      upcoming: open.filter((t) => t.dueDate !== null && t.dueDate > today).length,
      overdue: open.filter((t) => isOverdue(t, today)).length,
      delegated: delegated.filter((t) => !t.isCompleted).length,
      claimable: claimable.filter((t) => !t.isCompleted).length,
      forwarded: forwarded.filter((t) => !t.isCompleted).length,
    }
  }, [mine, delegated, claimable, forwarded, today])

  const tags = useMemo(() => tagCounts(mine), [mine])

  const sections = useMemo(() => {
    const source =
      view.kind === 'delegated' ? delegated : view.kind === 'claimable' ? claimable : view.kind === 'forwarded' ? forwarded : mine
    const visible = tasksInView(source, view, today).filter((t) => matchesSearch(t, search))
    return groupTasks(visible, sortBy, today)
  }, [mine, delegated, claimable, forwarded, view, today, search, sortBy])

  const visibleCount = sections.reduce((sum, group) => sum + group.tasks.length, 0)

  function selectView(next: View) {
    setView(next)
    setSearch('')
    setOpenId(null)
    setSidebarOpen(false)
    setCustomizing(false)
    setAlertOpen(false)
    setNotice(null)
  }

  /** Applies a dashboard change right away and saves it; a failed save is undone. */
  async function changeSettings(next: DashboardSettings) {
    const previous = settings
    setSettings(next)
    try {
      await dashboardApi.save(next)
      setError(null)
    } catch (err) {
      setSettings(previous)
      handleFailure(err, 'Nu am putut salva dashboard-ul.')
    }
  }

  async function add(draft: TaskDraft, files: File[] = []): Promise<boolean> {
    try {
      let created = await tasksApi.create(draft)
      setTasks((prev) => [...prev, created])
      setError(null)
      // The task exists from here on: a file that fails is reported, and can be added again on the task.
      for (const [index, file] of files.entries()) {
        const label = files.length > 1 ? ` (${index + 1} din ${files.length})` : ''
        setNotice(`Se încarcă „${file.name}”${label}…`)
        try {
          const attachment = await attachmentsApi.upload(created.id, file, (fraction) =>
            setNotice(`Se încarcă „${file.name}”${label}… ${Math.round(fraction * 100)}%`),
          )
          const withFile = { ...created, attachments: [...created.attachments, attachment] }
          created = withFile
          setTasks((prev) => prev.map((t) => (t.id === withFile.id ? withFile : t)))
        } catch (err) {
          if (err instanceof ApiError && err.status === 401) {
            onSessionExpired()
            return true
          }
          setError(`Task-ul a fost adăugat, dar fără toate fișierele. ${uploadErrorText(err, file.name, attachmentLimits)}`)
          break
        }
      }
      // A task given to someone else leaves this view, so say where it went.
      setNotice(
        created.assignee
          ? `Task alocat către ${created.assignee.displayName}. Îl găsești la „Delegate”.`
          : created.group
            ? `Task dat grupului ${created.group.name}. Îl găsești la „Delegate” până îl preia cineva.`
            : // The dialog closes on success, so say that it worked even when the view does not show the task.
              `Task adăugat: „${created.title}”.`,
      )
      return true
    } catch (err) {
      handleFailure(err, 'Nu am putut adăuga task-ul.')
      return false
    }
  }

  function replace(updated: TaskItem) {
    setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
  }

  /**
   * Runs a change to a task and keeps the list up to date with the server's answer. Resolves to an
   * error message for the dialog to show, or null when it worked. `explain` turns a refusal into
   * Romanian; the server's own messages are in English.
   */
  async function attempt(
    change: () => Promise<TaskItem | void>,
    fallback: string,
    explain?: (err: ApiError) => string | null,
  ): Promise<string | null> {
    try {
      const updated = await change()
      if (updated) replace(updated)
      setError(null)
      return null
    } catch (err) {
      if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel. Încearcă din nou.'
      if (err.status === 401) {
        onSessionExpired()
        return null
      }
      const explained = explain?.(err)
      if (explained) return explained
      if (err.status === 404) return 'Task-ul nu mai există sau nu mai ai acces la el.'
      if (err.status === 403) return 'Nu mai poți schimba acest task: nu mai este la tine.'
      return fallback
    }
  }

  /** Everything the task's dialog can do with it. */
  function actionsFor(task: TaskItem): TaskActions {
    return {
      // Who the task goes to is applied separately, and only when it differs: reassigning is its own
      // request, which only the creator may make.
      save: (draft) =>
        attempt(async () => {
          let updated = await tasksApi.update({ ...task, ...draft })
          if (draft.assigneeId !== undefined) {
            const next: TaskTarget = { assigneeId: draft.assigneeId, groupId: draft.groupId ?? null }
            // A group task someone took shows that person in the form; leaving them there changes nothing.
            const current: TaskTarget = { assigneeId: task.assignee?.id ?? null, groupId: task.assignee ? null : (task.group?.id ?? null) }
            if (next.assigneeId !== current.assigneeId || next.groupId !== current.groupId) {
              updated = await tasksApi.assign(task.id, next)
            }
          }
          return updated
        }, 'Nu am putut salva task-ul.'),
      complete: async (response) => {
        try {
          replace(await tasksApi.complete(task.id, response))
          setError(null)
          return null
        } catch (err) {
          if (err instanceof ApiError && err.status === 401) {
            onSessionExpired()
            return null
          }
          return replyErrorText(err)
        }
      },
      wait: (reason) =>
        attempt(() => tasksApi.wait(task.id, reason), 'Nu am putut pune task-ul în așteptare.', (err) =>
          err.status === 400
            ? 'Motivul este obligatoriu și poate avea cel mult 1000 de caractere.'
            : err.status === 409
              ? 'Task-ul este finalizat. Redeschide-l mai întâi.'
              : null,
        ),
      resume: () => attempt(() => tasksApi.resume(task.id), 'Nu am putut relua task-ul.'),
      reopen: () => attempt(() => tasksApi.update({ ...task, isCompleted: false }), 'Nu am putut redeschide task-ul.'),
      forward: async (target, note) => {
        const failure = await attempt(
          () => tasksApi.forward(task.id, target, note),
          'Nu am putut direcționa task-ul. Încearcă din nou.',
          (err) =>
            err.status === 400
              ? 'Persoana sau grupul ales nu mai există. Alege altceva.'
              : err.status === 403 || err.status === 404
                ? 'Task-ul nu mai este la tine, deci nu îl mai poți direcționa.'
                : null,
        )
        if (!failure) setNotice('Task direcționat. Îl poți urmări la „Direcționate”.')
        return failure
      },
      claim: () => claim(task),
      release: async () => {
        const failure = await attempt(() => tasksApi.release(task.id), 'Nu am putut da task-ul înapoi grupului.')
        if (failure) return failure
        setOpenId(null)
        setNotice(`„${task.title}” s-a întors la grupul ${task.group?.name ?? ''}.`.replace(' .', '.'))
        // Whether the task is still visible depends on the user still being in the group.
        await refresh()
        return null
      },
      remove: async () => {
        const failure = await attempt(
          async () => {
            await tasksApi.remove(task.id)
            setTasks((prev) => prev.filter((t) => t.id !== task.id))
          },
          'Nu am putut șterge task-ul.',
          (err) => (err.status === 403 ? 'Doar persoana care a creat task-ul îl poate șterge.' : null),
        )
        if (!failure) setNotice(`Task șters: „${task.title}”.`)
        return failure
      },
      attachmentsChanged: (attachments) =>
        setTasks((prev) => prev.map((t) => (t.id === task.id ? { ...t, attachments } : t))),
    }
  }

  /** Takes a task waiting on one of the user's groups, from its row or from its dialog. */
  async function claim(task: TaskItem): Promise<string | null> {
    const failure = await attempt(() => tasksApi.claim(task.id), 'Nu am putut prelua task-ul.', (err) =>
      err.status === 404 || err.status === 409 ? 'Task-ul a fost preluat între timp de altcineva.' : null,
    )
    if (failure) {
      await refresh()
      return failure
    }
    setNotice(`Ai preluat „${task.title}”. Îl găsești la „Toate task-urile”.`)
    return null
  }

  // New tasks start in the view they are added from: tagged in a tag view, due today in Today.
  const composerDefaults: TaskDraft = {
    title: '',
    startDate: null,
    priority: 'medium',
    dueDate: view.kind === 'today' ? today : null,
    tags: view.kind === 'tag' ? [view.tag] : [],
  }
  const isDashboard = view.kind === 'dashboard'
  const showsTasks =
    !isDashboard && !PAGE_VIEWS.has(view.kind)
  // New tasks are written in a dialog, opened from the header of the dashboard and of every task list.
  const canAdd = !loading && (isDashboard || showsTasks)
  const newTaskButton = canAdd && (
    <Button variant="primary" onClick={() => setAdding(true)}>
      <Icon name="plus" />
      Task nou
    </Button>
  )
  const hint = viewHint(view)

  function renderTask(task: TaskItem) {
    return (
      <TaskRow
        key={task.id}
        task={task}
        today={today}
        onOpen={() => setOpenId(task.id)}
        onClaim={() =>
          void claim(task).then((failure) => {
            if (failure) setError(failure)
          })
        }
        showHolder={task.access === 'viewer'}
      />
    )
  }

  const openTask = openId ? tasks.find((t) => t.id === openId) : undefined

  return (
    <div className={`workspace${sidebarOpen ? ' sidebar-open' : ''}`}>
      <Sidebar user={user} view={view} counts={counts} tags={tags} onSelect={selectView} onLogout={onLogout} />
      <button
        type="button"
        className="sidebar-backdrop"
        aria-label="Închide meniul"
        tabIndex={sidebarOpen ? 0 : -1}
        onClick={() => setSidebarOpen(false)}
      />

      <main className={`main${isDashboard || (PAGE_VIEWS.has(view.kind) && view.kind !== 'integrations') ? ' wide' : ''}`}>
        <header className="main-header">
          <button
            type="button"
            className="menu-button"
            aria-label="Deschide meniul"
            aria-expanded={sidebarOpen}
            onClick={() => setSidebarOpen(true)}
          >
            <Icon name="menu" size={20} />
          </button>
          <div className="main-title">
            <h1>
              {view.kind === 'tag' && <span className="title-hash">#</span>}
              {viewTitle(view)}
            </h1>
            {showsTasks && !loading && (
              <span className="main-subtitle">
                {tasksLabel(visibleCount)}
              </span>
            )}
          </div>
          {isDashboard && !loading && (
            <div className="main-tools">
              {newTaskButton}
              <button
                type="button"
                className="button ghost"
                aria-pressed={customizing}
                onClick={() => setCustomizing((on) => !on)}
              >
                <Icon name="settings" />
                {customizing ? 'Gata' : 'Personalizează'}
              </button>
            </div>
          )}
          {showsTasks && (
            <div className="main-tools">
              <label className="search">
                <Icon name="search" />
                <input
                  type="search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Caută task-uri"
                  aria-label="Caută task-uri"
                />
              </label>
              <label className="sort">
                <span className="visually-hidden">Sortează task-urile</span>
                <select value={sortBy} onChange={(e) => setSortBy(e.target.value as SortBy)} aria-label="Sortează task-urile">
                  <option value="dueDate">După termen</option>
                  <option value="priority">După prioritate</option>
                  <option value="created">După data creării</option>
                </select>
              </label>
              {newTaskButton}
            </div>
          )}
        </header>

        <div className="main-scroll">
          {notice && (
            <Banner tone="info" onClose={() => setNotice(null)}>
              {notice}
            </Banner>
          )}
          {error && <Banner onClose={() => setError(null)}>{error}</Banner>}
          {hint && !loading && <p className="view-hint muted">{hint}</p>}

          {view.kind === 'integrations' ? (
            <>
              <EmailNotifications onSessionExpired={onSessionExpired} />
              <Integrations onSessionExpired={onSessionExpired} />
            </>
          ) : view.kind === 'team' ? (
            <Team user={user} today={today} onSessionExpired={onSessionExpired} />
          ) : view.kind === 'groups' ? (
            <Groups user={user} onSessionExpired={onSessionExpired} onChanged={() => void refresh()} />
          ) : view.kind === 'admin' ? (
            <AdminUsers user={user} onSessionExpired={onSessionExpired} onOwnAccountChanged={onUserChanged} />
          ) : view.kind === 'settings' ? (
            <SystemSettings
              onSessionExpired={onSessionExpired}
              onAttachmentLimitChanged={() => void attachmentsApi.limits().then(setAttachmentLimits).catch(() => {})}
            />
          ) : loading ? (
            isDashboard ? (
              <CardsSkeleton />
            ) : (
              <TaskListSkeleton />
            )
          ) : isDashboard ? (
            <Dashboard
              tasks={mine}
              delegated={delegated}
              today={today}
              settings={settings}
              customizing={customizing}
              onChange={changeSettings}
              onDone={() => setCustomizing(false)}
              onOpen={selectView}
            />
          ) : visibleCount === 0 ? (
            <EmptyState
              {...emptyTitle(view, search.trim().length > 0)}
              action={
                search.trim().length > 0 ? (
                  <Button onClick={() => setSearch('')}>Șterge căutarea</Button>
                ) : canAdd ? (
                  <Button
                    variant="primary"
                    onClick={() => setAdding(true)}
                  >
                    <Icon name="plus" />
                    Adaugă un task
                  </Button>
                ) : undefined
              }
            >
              {emptyMessage(view, search.trim().length > 0)}
            </EmptyState>
          ) : (
            sections.map((group) => (
              <section key={group.key} className="task-group">
                {group.label && (
                  <h2 className={`divider${group.tone ? ` ${group.tone}` : ''}`}>
                    <span>
                      {group.label} <span className="divider-count">{group.tasks.length}</span>
                    </span>
                  </h2>
                )}
                <ul className="task-list">{group.tasks.map(renderTask)}</ul>
              </section>
            ))
          )}
        </div>

      </main>

      {openTask && (
        <TaskDetail
          // A new dialog for another task, so nothing typed for one is left in the other.
          key={openTask.id}
          task={openTask}
          today={today}
          actions={openTask.access === 'viewer' ? undefined : actionsFor(openTask)}
          people={people}
          groups={groups}
          currentUser={user}
          attachmentLimits={attachmentLimits}
          onTagClick={(tag) => selectView({ kind: 'tag', tag })}
          onSessionExpired={onSessionExpired}
          onClose={() => setOpenId(null)}
        />
      )}

      {adding && (
        <NewTaskDialog
          initial={composerDefaults}
          people={people}
          groups={groups}
          currentUser={user}
          attachmentLimits={attachmentLimits}
          onSubmit={add}
          onClose={() => setAdding(false)}
        />
      )}

      {alertOpen && (
        <DueAlert
          overdue={mine.filter((t) => isOverdue(t, today)).sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? ''))}
          dueToday={mine.filter((t) => !t.isCompleted && t.dueDate === today)}
          onOpen={selectView}
          onClose={() => setAlertOpen(false)}
        />
      )}
    </div>
  )
}
