import type { ReactNode } from 'react'
import Icon, { type IconName } from '../Icon'

interface Props {
  icon?: IconName
  title: string
  /** One sentence saying why there is nothing here, or what to do about it. */
  children?: ReactNode
  /** Usually a button that starts filling the place. */
  action?: ReactNode
  /** For a failed load rather than an empty list. */
  error?: boolean
  compact?: boolean
}

/** What a screen shows in place of a list that has nothing in it. */
export default function EmptyState({ icon = 'inbox', title, children, action, error = false, compact = false }: Props) {
  return (
    <div className={`empty-state${error ? ' is-error' : ''}${compact ? ' compact' : ''}`} role={error ? 'alert' : undefined}>
      <span className="empty-icon" aria-hidden="true">
        <Icon name={error ? 'alert' : icon} size={22} />
      </span>
      <h2>{title}</h2>
      {children && <p>{children}</p>}
      {action}
    </div>
  )
}
