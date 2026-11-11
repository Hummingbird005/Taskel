import { useState, type FormEvent } from 'react'
import { authApi, type User } from '../api/auth'
import { ApiError } from '../api/client'

type Mode = 'login' | 'register'

export default function AuthForm({ onAuthenticated }: { onAuthenticated: (user: User) => void }) {
  const [mode, setMode] = useState<Mode>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const isRegister = mode === 'register'

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      const user = isRegister
        ? await authApi.register(email, password)
        : await authApi.login(email, password)
      onAuthenticated(user)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not reach the Taskel API.')
    } finally {
      setSubmitting(false)
    }
  }

  function switchMode() {
    setMode(isRegister ? 'login' : 'register')
    setError(null)
  }

  return (
    <form onSubmit={handleSubmit} className="auth-form">
      <h2>{isRegister ? 'Create account' : 'Sign in'}</h2>
      <label>
        Email
        <input
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
        />
      </label>
      <label>
        Password
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete={isRegister ? 'new-password' : 'current-password'}
          minLength={isRegister ? 8 : undefined}
          required
        />
        {isRegister && <small className="muted">At least 8 characters.</small>}
      </label>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" disabled={submitting}>
        {isRegister ? 'Create account' : 'Sign in'}
      </button>
      <p className="muted">
        {isRegister ? 'Already have an account?' : 'No account yet?'}{' '}
        <button type="button" className="link" onClick={switchMode}>
          {isRegister ? 'Sign in' : 'Create one'}
        </button>
      </p>
    </form>
  )
}
