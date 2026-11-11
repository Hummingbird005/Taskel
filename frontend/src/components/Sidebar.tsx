import type { User } from '../api/auth'
import type { TagCount, View } from '../lib/taskViews'
import Icon, { type IconName } from './Icon'

export interface ViewCounts {
  all: number
  today: number
  upcoming: number
  overdue: number
}

interface Props {
  user: User
  view: View
  counts: ViewCounts
  tags: TagCount[]
  onSelect: (view: View) => void
  onLogout: () => void
}

const sameView = (a: View, b: View) =>
  a.kind === b.kind && (a.kind !== 'tag' || (b.kind === 'tag' && a.tag === b.tag))

export default function Sidebar({ user, view, counts, tags, onSelect, onLogout }: Props) {
  function item(target: View, icon: IconName, label: string, count = 0, tone?: 'danger') {
    const active = sameView(view, target)
    return (
      <li key={target.kind === 'tag' ? `tag:${target.tag}` : target.kind}>
        <button
          type="button"
          className={`nav-item${active ? ' active' : ''}`}
          aria-current={active ? 'page' : undefined}
          onClick={() => onSelect(target)}
        >
          <Icon name={icon} />
          <span className="nav-label">{label}</span>
          {count > 0 && <span className={`nav-count${tone ? ` ${tone}` : ''}`}>{count}</span>}
        </button>
      </li>
    )
  }

  return (
    <nav className="sidebar" aria-label="Views">
      <div className="sidebar-brand">
        <span className="brand-mark" aria-hidden="true">
          ✓
        </span>
        Taskel
      </div>

      <div className="sidebar-scroll">
        <ul className="nav-list">
          {item({ kind: 'all' }, 'inbox', 'All tasks', counts.all)}
          {item({ kind: 'today' }, 'sun', 'Today', counts.today)}
          {item({ kind: 'upcoming' }, 'calendar', 'Upcoming', counts.upcoming)}
          {item({ kind: 'overdue' }, 'alert', 'Overdue', counts.overdue, 'danger')}
          {item({ kind: 'completed' }, 'checkCircle', 'Completed')}
        </ul>

        <h2 className="nav-heading">Tags</h2>
        {tags.length === 0 ? (
          <p className="nav-empty">Tags you add to tasks show up here.</p>
        ) : (
          <ul className="nav-list">
            {tags.map(({ tag, open }) => item({ kind: 'tag', tag }, 'hash', tag, open))}
          </ul>
        )}

        <h2 className="nav-heading">Apps</h2>
        <ul className="nav-list">{item({ kind: 'integrations' }, 'plug', 'Integrations')}</ul>
      </div>

      <div className="sidebar-user">
        <span className="avatar" aria-hidden="true">
          {user.email.charAt(0).toUpperCase()}
        </span>
        <span className="sidebar-email" title={user.email}>
          {user.email}
        </span>
        <button type="button" className="sidebar-icon-button" onClick={onLogout} aria-label="Sign out" title="Sign out">
          <Icon name="logout" />
        </button>
      </div>
    </nav>
  )
}
