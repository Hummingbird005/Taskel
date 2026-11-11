import { useCallback, useEffect, useState } from 'react'
import { authApi, type User } from './api/auth'
import AuthForm from './components/AuthForm'
import Workspace from './components/Workspace'

type Session =
  | { status: 'loading' }
  | { status: 'unreachable' }
  | { status: 'signedOut' }
  | { status: 'signedIn'; user: User }

export default function App() {
  const [session, setSession] = useState<Session>({ status: 'loading' })

  useEffect(() => {
    authApi
      .me()
      .then((user) => setSession(user ? { status: 'signedIn', user } : { status: 'signedOut' }))
      .catch(() => setSession({ status: 'unreachable' }))
  }, [])

  const handleSessionExpired = useCallback(() => setSession({ status: 'signedOut' }), [])

  async function logout() {
    try {
      await authApi.logout()
    } finally {
      setSession({ status: 'signedOut' })
    }
  }

  if (session.status === 'signedIn') {
    return (
      <Workspace
        key={session.user.id}
        user={session.user}
        onLogout={logout}
        onSessionExpired={handleSessionExpired}
      />
    )
  }

  return (
    <div className="welcome">
      <aside className="welcome-brand">
        <div className="sidebar-brand">
          <span className="brand-mark" aria-hidden="true">
            ✓
          </span>
          Taskel
        </div>
        <p className="welcome-tagline">Your tasks, organised like a conversation.</p>
        <ul className="welcome-points">
          <li>Views for today, upcoming and overdue work</li>
          <li>Tags that work like channels</li>
          <li>Turn Outlook emails into tasks, and reply when they are done</li>
        </ul>
      </aside>
      <main className="welcome-main">
        {session.status === 'loading' && <p className="empty">Loading…</p>}
        {session.status === 'unreachable' && (
          <p className="error" role="alert">
            Could not reach the Taskel API. Is the backend running?
          </p>
        )}
        {session.status === 'signedOut' && (
          <AuthForm onAuthenticated={(user) => setSession({ status: 'signedIn', user })} />
        )}
      </main>
    </div>
  )
}
