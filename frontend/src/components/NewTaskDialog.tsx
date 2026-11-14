import { useEffect, useRef } from 'react'
import type { AttachmentLimits } from '../api/attachments'
import type { GroupListItem } from '../api/groups'
import type { TaskDraft } from '../api/tasks'
import type { UserSummary } from '../api/users'
import Icon from './Icon'
import TaskForm from './TaskForm'

interface Props {
  /** Starting values, e.g. the tag of the view the dialog was opened from. */
  initial: TaskDraft
  people: UserSummary[]
  groups: GroupListItem[]
  currentUser: { id: string }
  attachmentLimits: AttachmentLimits
  /** Creates the task. Resolves to true when it was saved, which closes the dialog. */
  onSubmit: (draft: TaskDraft, files: File[]) => Promise<boolean>
  onClose: () => void
}

/** The dialog a new task is written in: title, message, dates, priority, tags, who it is for, files. */
export default function NewTaskDialog({ initial, people, groups, currentUser, attachmentLimits, onSubmit, onClose }: Props) {
  const dialog = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const element = dialog.current
    if (!element || element.open) return
    element.showModal()
    // Opening a dialog puts focus on its first control, the close button; writing starts at the title.
    element.querySelector<HTMLInputElement>('.composer-title')?.focus()
  }, [])

  return (
    // Escape and the close button both end up in onClose, through the dialog's own close event.
    <dialog ref={dialog} className="task-dialog" aria-labelledby="new-task-title" onClose={onClose}>
      <header className="task-dialog-head">
        <h2 id="new-task-title">Task nou</h2>
        <button type="button" className="dialog-close" onClick={() => dialog.current?.close()} aria-label="Închide">
          <Icon name="close" />
        </button>
      </header>
      <TaskForm
        initial={initial}
        people={people}
        groups={groups}
        currentUser={currentUser}
        attachmentLimits={attachmentLimits}
        submitLabel="Adaugă task-ul"
        placeholder="Ce este de făcut?"
        onCancel={() => dialog.current?.close()}
        onSubmit={async (draft, files) => {
          const saved = await onSubmit(draft, files)
          if (saved) dialog.current?.close()
          return saved
        }}
      />
    </dialog>
  )
}
