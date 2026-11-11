import type { DashboardSettings } from '../api/dashboard'
import type { TaskItem } from '../api/tasks'
import { hiddenWidgets, visibleWidgets, type WidgetInfo } from '../lib/dashboard'
import { formatLongDate } from '../lib/dates'
import { isOverdue, type View } from '../lib/taskViews'
import DashboardWidget from './DashboardWidget'
import Icon from './Icon'

interface Props {
  tasks: TaskItem[]
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
  if (open.length === 0) return 'You have no open tasks.'
  const parts = [`${dueToday} due today`, `${overdue} overdue`]
  return `${open.length} open ${open.length === 1 ? 'task' : 'tasks'}: ${parts.join(', ')}.`
}

/** The home screen: the user's chosen widgets, in their chosen order. */
export default function Dashboard({ tasks, today, settings, customizing, onChange, onDone, onOpen }: Props) {
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
        <section className="customize" aria-label="Customize dashboard">
          <div className="customize-head">
            <div>
              <h3>Customize your dashboard</h3>
              <p className="muted">Reorder or remove widgets below, and add more from the list. Changes are saved as you go.</p>
            </div>
            <div className="customize-actions">
              <button type="button" className="button ghost" onClick={() => onChange({ ...settings, widgets: null })}>
                Reset to default
              </button>
              <button type="button" className="button primary" onClick={onDone}>
                Done
              </button>
            </div>
          </div>
          <label className="customize-toggle">
            <input
              type="checkbox"
              checked={settings.showDueAlert}
              onChange={(e) => onChange({ ...settings, showDueAlert: e.target.checked })}
            />
            Show a reminder for overdue and due-today tasks when I open Taskel
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
                    aria-label={`Add ${widget.title}`}
                  >
                    <Icon name="plus" />
                    Add
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {visible.length === 0 ? (
        <p className="empty">Your dashboard is empty. {customizing ? 'Add a widget from the list above.' : 'Use Customize to add widgets.'}</p>
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
                        aria-label={`Move ${widget.title} earlier`}
                        title="Move earlier"
                      >
                        <Icon name="arrowLeft" />
                      </button>
                      <button
                        type="button"
                        onClick={() => move(index, 1)}
                        disabled={index === visible.length - 1}
                        aria-label={`Move ${widget.title} later`}
                        title="Move later"
                      >
                        <Icon name="arrowRight" />
                      </button>
                      <button
                        type="button"
                        className="danger"
                        onClick={() => setWidgets(visible.filter((w) => w.id !== widget.id))}
                        aria-label={`Remove ${widget.title}`}
                        title="Remove"
                      >
                        <Icon name="close" />
                      </button>
                    </div>
                  )}
                </header>
                <DashboardWidget id={widget.id} tasks={tasks} today={today} onOpen={onOpen} />
              </section>
            )
          })}
        </div>
      )}
    </div>
  )
}
