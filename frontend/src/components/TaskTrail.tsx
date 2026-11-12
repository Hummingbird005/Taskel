import type { TaskItem } from '../api/tasks'
import { LOCALE } from '../lib/text'

const when = (iso: string) =>
  new Date(iso).toLocaleString(LOCALE, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/** The path a forwarded task took: who passed it on, to whom, when, and with what note. */
export default function TaskTrail({ task }: { task: TaskItem }) {
  if (task.forwards.length === 0) return null
  return (
    <details className="task-details">
      <summary>Traseu ({task.forwards.length})</summary>
      <ol className="trail">
        <li>
          <span className="trail-step">Creat de {task.createdBy.displayName}</span>
          <span className="muted">{when(task.createdAt)}</span>
        </li>
        {task.forwards.map((step) => (
          <li key={step.at + step.from.id}>
            <span className="trail-step">
              {step.from.displayName} → {step.toUser?.displayName ?? (step.toGroup ? `grupul ${step.toGroup.name}` : '—')}
            </span>
            <span className="muted">{when(step.at)}</span>
            {step.note && <span className="trail-note">„{step.note}”</span>}
          </li>
        ))}
      </ol>
    </details>
  )
}
