import { useEffect, useMemo, useState } from 'react'
import type { User } from '../api/auth'
import { ApiError } from '../api/client'
import { DEFAULT_DASHBOARD_SETTINGS, dashboardApi, type DashboardSettings } from '../api/dashboard'
import { tasksApi, type TaskDraft, type TaskItem } from '../api/tasks'
import { todayIso } from '../lib/dates'
import {
  emptyMessage,
  groupTasks,
  isOverdue,
  matchesSearch,
  tagCounts,
  tasksInView,
  viewTitle,
  type SortBy,
  type View,
} from '../lib/taskViews'
import CompleteWithReply from './CompleteWithReply'
import Dashboard from './Dashboard'
import DueAlert from './DueAlert'
import Icon from './Icon'
import Integrations from './Integrations'
import Sidebar from './Sidebar'
import TaskForm from './TaskForm'
import TaskRow from './TaskRow'

interface Props {
  user: User
  onLogout: () => void
  onSessionExpired: () => void
}

/** The signed-in app: sidebar with views, the dashboard or task list for the selected view, and the composer. */
export default function Workspace({ user, onLogout, onSessionExpired }: Props) {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [view, setView] = useState<View>({ kind: 'dashboard' })
  const [sortBy, setSortBy] = useState<SortBy>('dueDate')
  const [search, setSearch] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [completingId, setCompletingId] = useState<string | null>(null)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [settings, setSettings] = useState<DashboardSettings>(DEFAULT_DASHBOARD_SETTINGS)
  const [customizing, setCustomizing] = useState(false)
  const [alertOpen, setAlertOpen] = useState(false)

  const today = todayIso()

  /** Shows the failure, or returns to the sign-in screen when the session is gone. */
  function handleFailure(err: unknown, fallback: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else setError(err instanceof ApiError ? err.message : fallback)
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
          else setError('Could not load tasks. Is the backend running?')
        })
    }

    // First load: tasks and dashboard preferences together, so the reminder can be decided once.
    // Preferences that fail to load fall back to the defaults rather than blocking the app.
    Promise.all([tasksApi.list(), dashboardApi.get().catch(() => DEFAULT_DASHBOARD_SETTINGS)])
      .then(([loaded, loadedSettings]) => {
        if (cancelled) return
        setTasks(loaded)
        setSettings(loadedSettings)
        const now = todayIso()
        const needsAttention = loaded.some((t) => !t.isCompleted && t.dueDate !== null && t.dueDate <= now)
        setAlertOpen(loadedSettings.showDueAlert && needsAttention)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Could not load tasks. Is the backend running?')
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
  }, [onSessionExpired])

  const counts = useMemo(() => {
    const open = tasks.filter((t) => !t.isCompleted)
    return {
      all: open.length,
      today: open.filter((t) => t.dueDate === today).length,
      upcoming: open.filter((t) => t.dueDate !== null && t.dueDate > today).length,
      overdue: open.filter((t) => isOverdue(t, today)).length,
    }
  }, [tasks, today])

  const tags = useMemo(() => tagCounts(tasks), [tasks])

  const groups = useMemo(() => {
    const visible = tasksInView(tasks, view, today).filter((t) => matchesSearch(t, search))
    return groupTasks(visible, sortBy, today)
  }, [tasks, view, today, search, sortBy])

  const visibleCount = groups.reduce((sum, group) => sum + group.tasks.length, 0)

  function selectView(next: View) {
    setView(next)
    setSearch('')
    setEditingId(null)
    setCompletingId(null)
    setSidebarOpen(false)
    setCustomizing(false)
    setAlertOpen(false)
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
      handleFailure(err, 'Could not save your dashboard.')
    }
  }

  async function add(draft: TaskDraft): Promise<boolean> {
    try {
      const created = await tasksApi.create(draft)
      setTasks((prev) => [...prev, created])
      setError(null)
      return true
    } catch (err) {
      handleFailure(err, 'Failed to add task.')
      return false
    }
  }

  async function save(task: TaskItem, changes: Partial<TaskItem>): Promise<boolean> {
    try {
      const updated = await tasksApi.update({ ...task, ...changes })
      setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
      setError(null)
      return true
    } catch (err) {
      handleFailure(err, 'Failed to update task.')
      return false
    }
  }

  /** Email tasks are completed through a reply; everything else toggles directly. */
  function toggle(task: TaskItem) {
    if (!task.isCompleted && task.sourceEmailAddress) {
      setEditingId(null)
      setCompletingId(task.id)
    } else {
      void save(task, { isCompleted: !task.isCompleted })
    }
  }

  async function completeWithReply(task: TaskItem, response: string): Promise<string | null> {
    try {
      const updated = await tasksApi.complete(task.id, response)
      setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
      setCompletingId(null)
      setError(null)
      return null
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onSessionExpired()
        return null
      }
      return err instanceof ApiError ? err.message : 'Could not reach Taskel. The task was not completed.'
    }
  }

  async function remove(id: string) {
    try {
      await tasksApi.remove(id)
      setTasks((prev) => prev.filter((t) => t.id !== id))
    } catch (err) {
      handleFailure(err, 'Failed to delete task.')
    }
  }

  // New tasks start in the view they are added from: tagged in a tag view, due today in Today.
  const composerDefaults: TaskDraft = {
    title: '',
    priority: 'medium',
    dueDate: view.kind === 'today' ? today : null,
    tags: view.kind === 'tag' ? [view.tag] : [],
  }
  const composerKey = view.kind === 'tag' ? `tag:${view.tag}` : view.kind
  const isDashboard = view.kind === 'dashboard'
  const showsTasks = !isDashboard && view.kind !== 'integrations'
  const canAdd = (showsTasks && view.kind !== 'completed' && view.kind !== 'overdue') || (isDashboard && !customizing)

  function renderTask(task: TaskItem) {
    if (editingId === task.id) {
      return (
        <li key={task.id} className="task-editor">
          <TaskForm
            initial={task}
            submitLabel="Save"
            placeholder="Task title"
            onCancel={() => setEditingId(null)}
            onSubmit={async (draft) => {
              const saved = await save(task, draft)
              if (saved) setEditingId(null)
              return saved
            }}
          />
        </li>
      )
    }
    if (completingId === task.id && task.sourceEmailAddress) {
      return (
        <li key={task.id} className="task-editor">
          <CompleteWithReply
            title={task.title}
            recipient={task.sourceEmailAddress}
            onCancel={() => setCompletingId(null)}
            onSubmit={(response) => completeWithReply(task, response)}
          />
        </li>
      )
    }
    return (
      <TaskRow
        key={task.id}
        task={task}
        today={today}
        onToggle={() => toggle(task)}
        onEdit={() => {
          setCompletingId(null)
          setEditingId(task.id)
        }}
        onDelete={() => remove(task.id)}
        onTagClick={(tag) => selectView({ kind: 'tag', tag })}
      />
    )
  }

  return (
    <div className={`workspace${sidebarOpen ? ' sidebar-open' : ''}`}>
      <Sidebar user={user} view={view} counts={counts} tags={tags} onSelect={selectView} onLogout={onLogout} />
      <button
        type="button"
        className="sidebar-backdrop"
        aria-label="Close menu"
        tabIndex={sidebarOpen ? 0 : -1}
        onClick={() => setSidebarOpen(false)}
      />

      <main className="main">
        <header className="main-header">
          <button
            type="button"
            className="menu-button"
            aria-label="Open menu"
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
                {visibleCount} {visibleCount === 1 ? 'task' : 'tasks'}
              </span>
            )}
          </div>
          {isDashboard && !loading && (
            <div className="main-tools">
              <button
                type="button"
                className="button ghost"
                aria-pressed={customizing}
                onClick={() => setCustomizing((on) => !on)}
              >
                <Icon name="settings" />
                {customizing ? 'Done' : 'Customize'}
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
                  placeholder="Search tasks"
                  aria-label="Search tasks"
                />
              </label>
              <label className="sort">
                <span className="visually-hidden">Sort tasks by</span>
                <select value={sortBy} onChange={(e) => setSortBy(e.target.value as SortBy)} aria-label="Sort tasks by">
                  <option value="dueDate">By due date</option>
                  <option value="priority">By priority</option>
                  <option value="created">By created</option>
                </select>
              </label>
            </div>
          )}
        </header>

        <div className="main-scroll">
          {error && (
            <div className="banner" role="alert">
              <span>{error}</span>
              <button type="button" onClick={() => setError(null)} aria-label="Dismiss">
                <Icon name="close" />
              </button>
            </div>
          )}

          {view.kind === 'integrations' ? (
            <Integrations onSessionExpired={onSessionExpired} />
          ) : loading ? (
            <p className="empty">Loading…</p>
          ) : isDashboard ? (
            <Dashboard
              tasks={tasks}
              today={today}
              settings={settings}
              customizing={customizing}
              onChange={changeSettings}
              onDone={() => setCustomizing(false)}
              onOpen={selectView}
            />
          ) : visibleCount === 0 ? (
            <p className="empty">{emptyMessage(view, search.trim().length > 0)}</p>
          ) : (
            groups.map((group) => (
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

        {canAdd && (
          <footer className="main-composer">
            <TaskForm
              key={composerKey}
              initial={composerDefaults}
              submitLabel="Add"
              placeholder={view.kind === 'tag' ? `Add a task to #${view.tag}` : 'Add a task'}
              onSubmit={add}
            />
          </footer>
        )}
      </main>

      {alertOpen && (
        <DueAlert
          overdue={tasks.filter((t) => isOverdue(t, today)).sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? ''))}
          dueToday={tasks.filter((t) => !t.isCompleted && t.dueDate === today)}
          onOpen={selectView}
          onClose={() => setAlertOpen(false)}
        />
      )}
    </div>
  )
}
