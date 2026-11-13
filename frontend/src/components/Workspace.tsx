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
  tagCounts,
  tasksInView,
  viewHint,
  viewTitle,
  type SortBy,
  type View,
} from '../lib/taskViews'
import CompleteWithReply from './CompleteWithReply'
import Dashboard from './Dashboard'
import EmailNotifications from './EmailNotifications'
import ForwardTask from './ForwardTask'
import NewTaskDialog from './NewTaskDialog'
import Groups from './Groups'
import ReadOnlyTask from './ReadOnlyTask'
import DueAlert from './DueAlert'
import Icon from './Icon'
import Banner from './ui/Banner'
import Button from './ui/Button'
import EmptyState from './ui/EmptyState'
import { CardsSkeleton, TaskListSkeleton } from './ui/Skeleton'
import Integrations from './Integrations'
import Sidebar from './Sidebar'
import TaskForm from './TaskForm'
import Team from './Team'
import AdminUsers from './AdminUsers'
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
  const [editingId, setEditingId] = useState<string | null>(null)
  const [completingId, setCompletingId] = useState<string | null>(null)
  const [forwardingId, setForwardingId] = useState<string | null>(null)
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
    setEditingId(null)
    setCompletingId(null)
    setForwardingId(null)
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

  /**
   * Saves changes to a task. Who it goes to is applied separately and only when it differs,
   * since reassigning is its own request that only the creator may make.
   */
  async function save(task: TaskItem, changes: Partial<TaskItem> & Partial<TaskTarget>): Promise<boolean> {
    try {
      let updated = await tasksApi.update({ ...task, ...changes })
      if (changes.assigneeId !== undefined) {
        const next: TaskTarget = { assigneeId: changes.assigneeId, groupId: changes.groupId ?? null }
        // A group task someone took shows that person in the form; leaving them there changes nothing.
        const current: TaskTarget = { assigneeId: task.assignee?.id ?? null, groupId: task.assignee ? null : (task.group?.id ?? null) }
        if (next.assigneeId !== current.assigneeId || next.groupId !== current.groupId) {
          updated = await tasksApi.assign(task.id, next)
        }
      }
      const saved = updated
      setTasks((prev) => prev.map((t) => (t.id === saved.id ? saved : t)))
      setError(null)
      return true
    } catch (err) {
      handleFailure(err, 'Nu am putut salva task-ul.')
      return false
    }
  }

  async function claim(task: TaskItem) {
    try {
      const taken = await tasksApi.claim(task.id)
      setTasks((prev) => prev.map((t) => (t.id === taken.id ? taken : t)))
      setError(null)
      setNotice(`Ai preluat „${taken.title}”. Îl găsești la „Toate task-urile”.`)
    } catch (err) {
      if (err instanceof ApiError && (err.status === 404 || err.status === 409)) {
        setError('Task-ul a fost preluat între timp de altcineva.')
        await refresh()
      } else {
        handleFailure(err, 'Nu am putut prelua task-ul.')
      }
    }
  }

  async function release(task: TaskItem) {
    try {
      await tasksApi.release(task.id)
      setNotice(`„${task.title}” s-a întors la grupul ${task.group?.name ?? ''}.`.replace(' .', '.'))
      setError(null)
      // Whether the task is still visible depends on the user still being in the group.
      await refresh()
    } catch (err) {
      handleFailure(err, 'Nu am putut da task-ul înapoi grupului.')
    }
  }

  async function forward(task: TaskItem, target: TaskTarget, note: string | null): Promise<string | null> {
    try {
      const updated = await tasksApi.forward(task.id, target, note)
      setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
      setForwardingId(null)
      setError(null)
      const to = updated.assignee?.displayName ?? (updated.group ? `grupul ${updated.group.name}` : updated.createdBy.displayName)
      setNotice(`Task direcționat către ${to}. Îl poți urmări la „Direcționate”.`)
      return null
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onSessionExpired()
        return null
      }
      if (err instanceof ApiError && err.status === 400) return 'Persoana sau grupul ales nu mai există. Alege altceva.'
      if (err instanceof ApiError && (err.status === 403 || err.status === 404)) {
        return 'Task-ul nu mai este la tine, deci nu îl mai poți direcționa.'
      }
      return 'Nu am putut direcționa task-ul. Încearcă din nou.'
    }
  }

  /** Email tasks are completed through a reply; everything else toggles directly. */
  function toggle(task: TaskItem) {
    if (!task.isCompleted && task.sourceEmailAddress) {
      setEditingId(null)
      setForwardingId(null)
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
      return replyErrorText(err)
    }
  }

  async function remove(id: string) {
    try {
      await tasksApi.remove(id)
      setTasks((prev) => prev.filter((t) => t.id !== id))
    } catch (err) {
      handleFailure(
        err,
        err instanceof ApiError && err.status === 403
          ? 'Doar persoana care a creat task-ul îl poate șterge.'
          : 'Nu am putut șterge task-ul.',
      )
    }
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
    !isDashboard && view.kind !== 'integrations' && view.kind !== 'team' && view.kind !== 'groups' && view.kind !== 'admin'
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
    if (task.access === 'viewer') return <ReadOnlyTask key={task.id} task={task} today={today} showHolder />
    if (forwardingId === task.id) {
      return (
        <li key={task.id} className="task-editor">
          <ForwardTask
            task={task}
            people={people}
            groups={groups}
            onCancel={() => setForwardingId(null)}
            onSubmit={(target, note) => forward(task, target, note)}
          />
        </li>
      )
    }
    if (editingId === task.id) {
      return (
        <li key={task.id} className="task-editor">
          <TaskForm
            initial={{ ...task, assigneeId: task.assignee?.id ?? null, groupId: task.assignee ? null : (task.group?.id ?? null) }}
            // Only the creator can reassign, so only they are offered the choice.
            people={task.access === 'creator' ? people : []}
            groups={groups}
            currentUser={user}
            taskId={task.id}
            submitLabel="Salvează"
            placeholder="Titlul task-ului"
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
          setForwardingId(null)
          setEditingId(task.id)
        }}
        onClaim={() => void claim(task)}
        onRelease={() => void release(task)}
        onForward={() => {
          setCompletingId(null)
          setEditingId(null)
          setForwardingId(task.id)
        }}
        onDelete={() => remove(task.id)}
        onTagClick={(tag) => selectView({ kind: 'tag', tag })}
        attachmentLimits={attachmentLimits}
        onAttachmentsChange={(attachments) =>
          setTasks((prev) => prev.map((t) => (t.id === task.id ? { ...t, attachments } : t)))
        }
        onSessionExpired={onSessionExpired}
      />
    )
  }

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

      <main className={`main${isDashboard || view.kind === 'team' || view.kind === 'groups' || view.kind === 'admin' ? ' wide' : ''}`}>
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
            <AdminUsers
              user={user}
              onSessionExpired={onSessionExpired}
              onOwnAccountChanged={onUserChanged}
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
