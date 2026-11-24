import { useEffect, useMemo, useState } from 'react'
import type { User } from '../api/auth'
import { ApiError } from '../api/client'
import { DEFAULT_DASHBOARD_SETTINGS, dashboardApi, type DashboardSettings } from '../api/dashboard'
import { attachmentsApi, DEFAULT_ATTACHMENT_LIMITS, type AttachmentLimits } from '../api/attachments'
import { groupsApi, type GroupListItem } from '../api/groups'
import { tasksApi, type TaskDraft, type TaskItem } from '../api/tasks'
import { addTask, claimTask, taskActions, type TaskActionContext } from '../lib/taskActions'
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
  sourceOf,
  tagCounts,
  tasksInView,
  viewHint,
  viewTitle,
  type SortBy,
  type View,
} from '../lib/taskViews'
import Dashboard from './Dashboard'
import NewTaskDialog from './NewTaskDialog'
import DueAlert from './DueAlert'
import Icon from './Icon'
import Banner from './ui/Banner'
import Button from './ui/Button'
import EmptyState from './ui/EmptyState'
import { CardsSkeleton, TaskListSkeleton } from './ui/Skeleton'
import PageView from './PageView'
import Sidebar from './Sidebar'
import TaskDetail from './TaskDetail'
import TaskRow from './TaskRow'

interface Props {
  user: User
  /** Called when the signed-in account itself was changed (its role or department). */
  onUserChanged: () => void
  onLogout: () => void
  onSessionExpired: () => void
}

/**
 * The task named in the address (`?task=<id>`), as the links in Taskel's emails give it. Read once: the address is
 * cleaned at once, so reloading the page does not open it again.
 */
function taskFromLink(): string | null {
  const params = new URLSearchParams(window.location.search)
  const id = params.get('task')
  if (!id) return null
  params.delete('task')
  const rest = params.toString()
  window.history.replaceState(null, '', `${window.location.pathname}${rest ? `?${rest}` : ''}${window.location.hash}`)
  return /^[0-9a-f-]{36}$/i.test(id) ? id : null
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
        // A link from an email ("Deschide în Taskel") opens its task, instead of the reminder.
        const linked = taskFromLink()
        if (linked) {
          if (loaded.some((t) => t.id === linked)) setOpenId(linked)
          else setNotice('Task-ul din link nu mai este în listele tale: a fost șters sau a trecut la altcineva.')
        }
        setAlertOpen(!linked && loadedSettings.showDueAlert && needsAttention)
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
    const source = sourceOf(view, { mine, delegated, claimable, forwarded })
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

  const actionContext: TaskActionContext = {
    setTasks,
    setNotice,
    setError,
    closeTask: () => setOpenId(null),
    refresh,
    onSessionExpired,
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
          claimTask(actionContext, task).then((failure) => {
            if (failure) setError(failure)
          })
        }
        showHolder={task.access === 'viewer'}
      />
    )
  }

  const searching = search.trim().length > 0

  /** What an empty list offers: clearing the search, or adding a task. */
  function emptyAction() {
    if (searching) return <Button onClick={() => setSearch('')}>Șterge căutarea</Button>
    if (!canAdd) return undefined
    return (
      <Button variant="primary" onClick={() => setAdding(true)}>
        <Icon name="plus" />
        Adaugă un task
      </Button>
    )
  }

  /** The dashboard, or the selected list of tasks grouped in sections. */
  function renderTasks() {
    if (loading) return isDashboard ? <CardsSkeleton /> : <TaskListSkeleton />
    if (isDashboard) {
      return (
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
      )
    }
    if (visibleCount === 0) {
      return (
        <EmptyState {...emptyTitle(view, searching)} action={emptyAction()}>
          {emptyMessage(view, searching)}
        </EmptyState>
      )
    }
    return sections.map((group) => (
      <section key={group.key} className="task-group">
        {group.label && (
          <h2 className={['divider', group.tone].filter(Boolean).join(' ')}>
            <span>
              {group.label} <span className="divider-count">{group.tasks.length}</span>
            </span>
          </h2>
        )}
        <ul className="task-list">{group.tasks.map(renderTask)}</ul>
      </section>
    ))
  }

  const openTask = openId ? tasks.find((t) => t.id === openId) : undefined
  const wide = isDashboard || (PAGE_VIEWS.has(view.kind) && view.kind !== 'integrations')

  return (
    <div className={sidebarOpen ? 'workspace sidebar-open' : 'workspace'}>
      <Sidebar user={user} view={view} counts={counts} tags={tags} onSelect={selectView} onLogout={onLogout} />
      <button
        type="button"
        className="sidebar-backdrop"
        aria-label="Închide meniul"
        tabIndex={sidebarOpen ? 0 : -1}
        onClick={() => setSidebarOpen(false)}
      />

      <main className={wide ? 'main wide' : 'main'}>
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

          {PAGE_VIEWS.has(view.kind) ? (
            <PageView
              kind={view.kind}
              user={user}
              today={today}
              onSessionExpired={onSessionExpired}
              onUserChanged={onUserChanged}
              onGroupsChanged={() => {
                refresh()
              }}
              onAttachmentLimitChanged={() => {
                attachmentsApi.limits().then(setAttachmentLimits).catch(() => {})
              }}
            />
          ) : (
            renderTasks()
          )}
        </div>

      </main>

      {openTask && (
        <TaskDetail
          // A new dialog for another task, so nothing typed for one is left in the other.
          key={openTask.id}
          task={openTask}
          today={today}
          actions={openTask.access === 'viewer' ? undefined : taskActions(actionContext, openTask)}
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
          onSubmit={(draft, files) => addTask(actionContext, draft, files, attachmentLimits)}
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
