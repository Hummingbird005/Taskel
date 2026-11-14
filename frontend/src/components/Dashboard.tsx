import EmptyState from './ui/EmptyState'
import type { DashboardSettings } from '../api/dashboard'
import type { TaskItem } from '../api/tasks'
import { hiddenWidgets, visibleWidgets, type WidgetInfo } from '../lib/dashboard'
import { formatLongDate } from '../lib/dates'
import { isOverdue, type View } from '../lib/taskViews'
import { tasksLabel } from '../lib/text'
import DashboardWidget from './DashboardWidget'
import Icon from './Icon'

interface Props {
  tasks: TaskItem[]
  delegated: TaskItem[]
  today: string
  settings: DashboardSettings
  customizing: boolean
  onChange: (settings: DashboardSettings) => void
  onDone: () => void
  onOpen: (view: View) => void
}

function summary(tasks: TaskItem[], today: string): string {
  const open = tasks.filter((t) => !t.isCompleted)
  const dueToday = open.filter((t) => t.dueDate === today).length
  const overdue = open.filter((t) => isOverdue(t, today)).length
  if (open.length === 0) return 'Nu ai niciun task deschis.'
  const overdueText = overdue === 1 ? '1 întârziat' : `${overdue} întârziate`
  return `${tasksLabel(open.length)} ${open.length === 1 ? 'deschis' : 'deschise'}: ${dueToday} cu termen azi, ${overdueText}.`
}

/** The home screen: the user's chosen widgets, in their chosen order. */
export default function Dashboard({ tasks, delegated, today, settings, customizing, onChange, onDone, onOpen }: Props) {
  const visible = visibleWidgets(settings.widgets)
  const hidden = hiddenWidgets(visible)

  const setWidgets = (widgets: WidgetInfo[]) => onChange({ ...settings, widgets: widgets.map((w) => w.id) })

  function move(index: number, by: -1 | 1) {
    const next = [...visible]
    const [widget] = next.splice(index, 1)
    next.splice(index + by, 0, widget)
    setWidgets(next)
  }

  return (
    <div className="dashboard">
      <div className="dashboard-intro">
        <h2>{formatLongDate(today)}</h2>
        <p className="muted">{summary(tasks, today)}</p>
      </div>

      {customizing && (
        <section className="customize" aria-label="Personalizează dashboard-ul">
          <div className="customize-head">
            <div>
              <h3>Personalizează-ți dashboard-ul</h3>
              <p className="muted">Reordonează sau elimină widget-urile de mai jos și adaugă altele din listă. Modificările se salvează pe loc.</p>
            </div>
            <div className="customize-actions">
              <button type="button" className="button ghost" onClick={() => onChange({ ...settings, widgets: null })}>
                Revino la varianta implicită
              </button>
              <button type="button" className="button primary" onClick={onDone}>
                Gata
              </button>
            </div>
          </div>
          <label className="customize-toggle">
            <input
              type="checkbox"
              checked={settings.showDueAlert}
              onChange={(e) => onChange({ ...settings, showDueAlert: e.target.checked })}
            />
            <span>Arată-mi o atenționare pentru task-urile întârziate și cele cu termen azi când deschid Taskel</span>
          </label>
          {hidden.length > 0 && (
            <ul className="available-widgets">
              {hidden.map((widget) => (
                <li key={widget.id}>
                  <div>
                    <strong>{widget.title}</strong>
                    <span className="muted">{widget.description}</span>
                  </div>
                  <button
                    type="button"
                    className="button ghost"
                    onClick={() => setWidgets([...visible, widget])}
                    aria-label={`Adaugă: ${widget.title}`}
                  >
                    <Icon name="plus" />
                    Adaugă
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {visible.length === 0 ? (
        <EmptyState icon="home" title="Dashboard gol">
          {customizing ? 'Adaugă un widget din lista de mai sus.' : 'Folosește „Personalizează” ca să adaugi widget-uri.'}
        </EmptyState>
      ) : (
        <div className="widget-grid">
          {visible.map((widget, index) => {
            return (
              <section key={widget.id} className={`widget${widget.wide ? ' wide' : ''}${customizing ? ' editing' : ''}`}>
                <header className="widget-head">
                  <h3>{widget.title}</h3>
                  {customizing && (
                    <div className="widget-controls">
                      <button
                        type="button"
                        onClick={() => move(index, -1)}
                        disabled={index === 0}
                        aria-label={`Mută mai devreme: ${widget.title}`}
                        title="Mută mai devreme"
                      >
                        <Icon name="arrowLeft" />
                      </button>
                      <button
                        type="button"
                        onClick={() => move(index, 1)}
                        disabled={index === visible.length - 1}
                        aria-label={`Mută mai târziu: ${widget.title}`}
                        title="Mută mai târziu"
                      >
                        <Icon name="arrowRight" />
                      </button>
                      <button
                        type="button"
                        className="danger"
                        onClick={() => setWidgets(visible.filter((w) => w.id !== widget.id))}
                        aria-label={`Elimină: ${widget.title}`}
                        title="Elimină"
                      >
                        <Icon name="close" />
                      </button>
                    </div>
                  )}
                </header>
                <DashboardWidget id={widget.id} tasks={tasks} delegated={delegated} today={today} onOpen={onOpen} />
              </section>
            )
          })}
        </div>
      )}
    </div>
  )
}
