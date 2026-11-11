import { useCallback, useEffect, useState } from 'react'
import { authApi, type User } from './api/auth'
import AuthForm from './components/AuthForm'
import TaskList from './components/TaskList'

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

  return (
    <main className="container">
      <header className="app-header">
        <h1>Taskel</h1>
        {session.status === 'signedIn' && (
          <div className="user-menu">
            <span className="muted">{session.user.email}</span>
            <button type="button" className="secondary" onClick={logout}>
              Sign out
            </button>
          </div>
        )}
      </header>

      {session.status === 'loading' && <p>Loading…</p>}
      {session.status === 'unreachable' && (
        <p className="error">Could not reach the Taskel API. Is the backend running?</p>
      )}
      {session.status === 'signedOut' && (
        <AuthForm onAuthenticated={(user) => setSession({ status: 'signedIn', user })} />
      )}
      {session.status === 'signedIn' && (
        <TaskList key={session.user.id} onSessionExpired={handleSessionExpired} />
      )}
    </main>
  )
}
