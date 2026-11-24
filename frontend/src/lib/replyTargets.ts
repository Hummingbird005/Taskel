import type { TaskItem } from '../api/tasks'

/** Who the reply goes to: whoever the answer is for, one of the people a task was distributed to, or a note. */
export interface ReplyTarget {
  key: string
  kind: 'reply' | 'copy' | 'note'
  label: string
  hint: string
}

/** The answer's recipient, and the people and the note the reader may write to instead. */
export function replyTargets(task: TaskItem): ReplyTarget[] {
  const targets: ReplyTarget[] = []
  if (task.answersTo) {
    targets.push({ key: 'reply', kind: 'reply', label: task.answersTo.displayName, hint: 'ajunge în task-ul din care a fost distribuit, cu fișierele atașate' })
  } else if (task.returnsTo) {
    targets.push({ key: 'reply', kind: 'reply', label: task.returnsTo.displayName, hint: 'la finalizare, task-ul se întoarce cu rezolvarea la cine ți l-a trimis' })
  } else if (task.replyTo) {
    const colleague = task.replyTo === task.createdBy.email
    targets.push({
      key: 'reply',
      kind: 'reply',
      label: colleague ? task.createdBy.displayName : task.replyTo,
      hint: colleague
        ? 'pe email, ca reply la emailul inițial, cu firul de mai sus'
        : 'pe email, ca reply la emailul inițial',
    })
  } else if (task.access === 'assignee') {
    targets.push({ key: 'reply', kind: 'reply', label: task.createdBy.displayName, hint: 'în firul task-ului; primește și un email' })
  }
  if (!task.distributedFrom) {
    for (const copy of task.distributions ?? []) {
      const name = copy.assignee?.displayName ?? copy.group?.name ?? '—'
      targets.push({ key: `copy:${copy.taskId}`, kind: 'copy', label: name, hint: 'mesaj doar pentru această persoană; primește un email' })
    }
    targets.push({ key: 'note', kind: 'note', label: 'Notă pe task', hint: 'rămâne în firul task-ului; nu primește nimeni email' })
  }
  return targets
}
