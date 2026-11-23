import type { TaskItem, TaskState } from '../api/tasks'
import type { IconName } from '../components/Icon'

/** How each state of a task is named and drawn. */
export const STATE_LABEL: Record<TaskState, string> = { open: 'Deschis', waiting: 'În așteptare', completed: 'Finalizat' }
export const STATE_ICON: Record<TaskState, IconName> = { open: 'inbox', waiting: 'clock', completed: 'check' }

/**
 * Why the task cannot move to `next` with this message; null when it can. Waiting needs a reason,
 * and completing an email task needs the reply that will be sent.
 */
export function stateChangeProblem(task: TaskItem, next: TaskState, text: string): string | null {
  if (next === 'waiting' && !text) return 'Scrie în câmpul de mai jos motivul pentru care task-ul este în așteptare.'
  if (next === 'completed' && task.answersTo && !text)
    return `Scrie răspunsul: ajunge la ${task.answersTo.displayName}, în conversația task-ului.`
  if (next !== 'completed' || !task.sourceEmailAddress || text) return null
  if (task.returnsTo) return `Scrie rezolvarea: task-ul se întoarce cu ea la ${task.returnsTo.displayName}.`
  return `Scrie răspunsul: se trimite pe email către ${task.replyTo ?? task.sourceEmailAddress}, apoi task-ul se finalizează.`
}

/**
 * Whether the person looking may distribute the task: they answer for it (its assignee, or its creator while it
 * is with nobody else) and it is not completed.
 */
export function canDistribute(task: TaskItem): boolean {
  const responsible = task.access === 'assignee' || (task.access === 'creator' && !task.assignee && !task.group)
  return responsible && task.state !== 'completed'
}

/** The copies of a distributed task that have not been answered yet. */
export function openCopies(task: TaskItem): number {
  return task.distributions?.filter((d) => d.state !== 'completed').length ?? 0
}
