import type { User } from '../api/auth'
import type { TagCount, View } from '../lib/taskViews'
import Icon, { type IconName } from './Icon'
import ThemePicker from './ThemePicker'

export interface ViewCounts {
  all: number
  today: number
  upcoming: number
  overdue: number
  delegated: number
  claimable: number
  forwarded: number
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
          {count > 0 && <span className={tone ? `nav-count ${tone}` : 'nav-count'}>{count}</span>}
        </button>
      </li>
    )
  }

  return (
    <nav className="sidebar" aria-label="Vederi">
      <div className="sidebar-brand">
        <span className="brand-mark" aria-hidden="true">
          ✓
        </span>
        <span>Taskel</span>
      </div>

      <div className="sidebar-scroll">
        <ul className="nav-list">
          {item({ kind: 'dashboard' }, 'home', 'Acasă')}
          {item({ kind: 'all' }, 'inbox', 'Toate task-urile', counts.all)}
          {item({ kind: 'today' }, 'sun', 'Azi', counts.today)}
          {item({ kind: 'upcoming' }, 'calendar', 'Urmează', counts.upcoming)}
          {item({ kind: 'overdue' }, 'alert', 'Întârziate', counts.overdue, 'danger')}
          {item({ kind: 'completed' }, 'checkCircle', 'Finalizate')}
          {item({ kind: 'claimable' }, 'hand', 'De preluat', counts.claimable)}
          {item({ kind: 'delegated' }, 'send', 'Delegate', counts.delegated)}
          {item({ kind: 'forwarded' }, 'forward', 'Direcționate', counts.forwarded)}
        </ul>

        <h2 className="nav-heading">Etichete</h2>
        {tags.length === 0 ? (
          <p className="nav-empty">Etichetele pe care le pui pe task-uri apar aici.</p>
        ) : (
          <ul className="nav-list">
            {tags.map(({ tag, open }) => item({ kind: 'tag', tag }, 'hash', tag, open))}
          </ul>
        )}

        {user.role !== 'user' && (
          <>
            <h2 className="nav-heading">Organizare</h2>
            <ul className="nav-list">
              {item({ kind: 'team' }, 'users', 'Echipă')}
              {item({ kind: 'groups' }, 'hand', 'Grupuri')}
              {user.role === 'admin' && item({ kind: 'admin' }, 'shield', 'Utilizatori')}
              {user.role === 'admin' && item({ kind: 'settings' }, 'settings', 'Setări sistem')}
            </ul>
          </>
        )}

        <h2 className="nav-heading">Aplicații</h2>
        <ul className="nav-list">{item({ kind: 'integrations' }, 'plug', 'Integrări')}</ul>
      </div>

      <ThemePicker />
      <div className="sidebar-user">
        <span className="avatar" aria-hidden="true">
          {user.displayName.charAt(0).toUpperCase()}
        </span>
        <span className="sidebar-email" title={user.email}>
          {user.displayName}
          {user.department && <small>{user.department}</small>}
        </span>
        <button type="button" className="sidebar-icon-button" onClick={onLogout} aria-label="Deconectare" title="Deconectare">
          <Icon name="logout" />
        </button>
      </div>
    </nav>
  )
}
