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
        <p className="welcome-tagline">Task-urile tale, organizate ca o conversație.</p>
        <ul className="welcome-points">
          <li>Vederi pentru ce ai de făcut azi, ce urmează și ce a întârziat</li>
          <li>Etichete care funcționează ca niște canale</li>
          <li>Transformi emailurile din Outlook în task-uri și răspunzi când sunt gata</li>
        </ul>
      </aside>
      <main className="welcome-main">
        {session.status === 'loading' && <p className="empty">Se încarcă…</p>}
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
