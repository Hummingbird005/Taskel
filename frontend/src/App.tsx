import { useCallback, useEffect, useState } from 'react'
import { authApi, type User } from './api/auth'
import AuthForm from './components/AuthForm'
import Workspace from './components/Workspace'
import { Skeleton } from './components/ui/Skeleton'

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

  /** Reads the account again after its role or department was changed. */
  const refreshUser = useCallback(() => {
    authApi
      .me()
      .then((user) => setSession(user ? { status: 'signedIn', user } : { status: 'signedOut' }))
      .catch(() => {
        // Keep the current session; the next request will show any real problem.
      })
  }, [])

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
        onUserChanged={refreshUser}
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
        <p className="welcome-tagline">Task-urile tale, organizate ca o conversație.</p>
        <ul className="welcome-points">
          <li>Vederi pentru ce ai de făcut azi, ce urmează și ce a întârziat</li>
          <li>Etichete care funcționează ca niște canale</li>
          <li>Transformi emailurile din Outlook în task-uri și răspunzi când sunt gata</li>
        </ul>
      </aside>
      <main className="welcome-main">
        {session.status === 'loading' && (
          <div className="auth-form" role="status">
            <span className="visually-hidden">Se încarcă…</span>
            <Skeleton width={180} height={26} />
            <Skeleton height={40} />
            <Skeleton height={40} />
            <Skeleton height={42} />
          </div>
        )}
        {session.status === 'unreachable' && (
          <p className="error" role="alert">
            Nu am putut contacta serverul Taskel. Verifică dacă este pornit.
          </p>
        )}
        {session.status === 'signedOut' && (
          <AuthForm onAuthenticated={(user) => setSession({ status: 'signedIn', user })} />
        )}
      </main>
    </div>
  )
}
