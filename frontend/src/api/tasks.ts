import { request } from './client'
import type { Attachment } from './attachments'
import type { GroupSummary } from './groups'
import type { UserSummary } from './users'

export type TaskPriority = 'low' | 'medium' | 'high'

/**
 * What the signed-in user may do with a task: its creator everything; its assignee edit, complete
 * and forward; a member of the group it waits on take it; a viewer only read it.
 */
export type TaskAccess = 'creator' | 'assignee' | 'group' | 'viewer'

/** Where a task stands. A task on hold (`waiting`) is still open work, with a reason. */
export type TaskState = 'open' | 'waiting' | 'completed'

/** One time a task was passed on. */
export interface TaskForward {
  from: UserSummary
  toUser: UserSummary | null
  toGroup: GroupSummary | null
  /** For a return, the resolution of whoever completed the task. */
  note: string | null
  at: string
  /** The task coming back to someone who asked for it back, rather than being passed on. */
  isReturn: boolean
  /** The sender asked for the task back and has not had it back yet. */
  returnPending: boolean
}

/** Who a task goes to: a user, a group, or nobody else (both null). */
export interface TaskTarget {
  assigneeId: string | null
  groupId: string | null
}

export interface TaskItem {
  id: string
  title: string
  description?: string | null
  isCompleted: boolean
  /**
   * First day of the period the task is worked on (YYYY-MM-DD), or null. With the due date it
   * forms the task's interval; a task without it falls on its due date alone.
   */
  startDate: string | null
  /** Calendar date as YYYY-MM-DD, or null when the task has no due date. */
  dueDate: string | null
  priority: TaskPriority
  tags: string[]
  createdAt: string
  /**
   * Sender of the email this task came from. When set, the task can only be completed with a
   * response, which Taskel emails to this address.
   */
  sourceEmailAddress: string | null
  /** The response written when the task was completed. */
  completionResponse: string | null
  completedAt: string | null
  state: TaskState
  /** What the task waits for, while it is on hold. */
  waitingReason: string | null
  waitingSince: string | null
  /** Who created the task. Only they can delete or reassign it. */
  createdBy: UserSummary
  /** Who the task is assigned to, when that is someone other than its creator. */
  assignee: UserSummary | null
  /** The group the task was given to. Without an assignee it is waiting for a member to take it. */
  group: GroupSummary | null
  /** Each time the task was forwarded, oldest first. */
  forwards: TaskForward[]
  /** Files attached to the task, oldest first. */
  attachments: Attachment[]
  access: TaskAccess
  /** Guidance from whoever sent the task, e.g. when an email was given to several people from Outlook. */
  instructions: string | null
  /**
   * For the person asking: where the completion message of this email task is emailed (the email's
   * sender when the creator completes it, otherwise the creator). Null when nothing is emailed.
   */
  replyTo: string | null
  /** For the person asking: who the task goes back to when they complete it, instead of being completed. */
  returnsTo: UserSummary | null
}

/** The editable fields of a task. */
export type TaskDraft = Pick<TaskItem, 'title' | 'startDate' | 'dueDate' | 'priority' | 'tags'> & {
  description?: string | null
  /** User to assign the task to; null keeps it with its creator. Left out when not being changed. */
  assigneeId?: string | null
  /** Group to give the task to instead of a user. */
  groupId?: string | null
}

export const tasksApi = {
  list: () => request<TaskItem[]>('/tasks'),
  create: (draft: TaskDraft) => request<TaskItem>('/tasks', { method: 'POST', body: JSON.stringify(draft) }),
  update: (task: TaskItem) =>
    request<TaskItem>(`/tasks/${task.id}`, {
      method: 'PUT',
      body: JSON.stringify({
        title: task.title,
        description: task.description,
        isCompleted: task.isCompleted,
        startDate: task.startDate,
        dueDate: task.dueDate,
        priority: task.priority,
        tags: task.tags,
      }),
    }),
  /** Assigns a task to a user or a group, or back to its creator. Only the creator may do this. */
  assign: (id: string, target: TaskTarget) =>
    request<TaskItem>(`/tasks/${id}/assignee`, { method: 'PUT', body: JSON.stringify(target) }),
  /** Passes a received task on to a user or a group. The sender can still read it afterwards. */
  /** Passes a received task on. With `returnToMe`, it comes back to the caller once completed. */
  forward: (id: string, target: TaskTarget, note: string | null, returnToMe: boolean) =>
    request<TaskItem>(`/tasks/${id}/forward`, { method: 'POST', body: JSON.stringify({ ...target, note, returnToMe }) }),
  /** Puts a task on hold. The reason is required. */
  wait: (id: string, reason: string) =>
    request<TaskItem>(`/tasks/${id}/wait`, { method: 'POST', body: JSON.stringify({ reason }) }),
  /** Takes a task off hold. */
  resume: (id: string) => request<TaskItem>(`/tasks/${id}/resume`, { method: 'POST' }),
  /** Takes a task that waits on one of the user's groups. */
  claim: (id: string) => request<TaskItem>(`/tasks/${id}/claim`, { method: 'POST' }),
  /** Hands a task taken from a group back to it. */
  release: (id: string) => request<TaskItem>(`/tasks/${id}/release`, { method: 'POST' }),
  /** Completes a task. For email tasks the response is required and is emailed to the sender. */
  complete: (id: string, response: string | null) =>
    request<TaskItem>(`/tasks/${id}/complete`, { method: 'POST', body: JSON.stringify({ response }) }),
  remove: (id: string) => request<void>(`/tasks/${id}`, { method: 'DELETE' }),
}
