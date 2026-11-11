import { useState, type FormEvent } from 'react'
import { authApi, type User } from '../api/auth'
import { ApiError } from '../api/client'

type Mode = 'login' | 'register'

function errorText(err: unknown, isRegister: boolean): string {
  if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel.'
  if (err.status === 401) return 'Email sau parolă greșită.'
  if (err.status === 409) return 'Există deja un cont cu acest email.'
  if (err.status === 400) {
    return isRegister
      ? 'Verifică adresa de email și parola (cel puțin 8 caractere).'
      : 'Completează emailul și parola.'
  }
  return 'A apărut o eroare. Încearcă din nou.'
}

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
      setError(errorText(err, isRegister))
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
      <h2>{isRegister ? 'Creează-ți contul' : 'Conectează-te la Taskel'}</h2>
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
        Parolă
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete={isRegister ? 'new-password' : 'current-password'}
          minLength={isRegister ? 8 : undefined}
          required
        />
        {isRegister && <small className="muted">Cel puțin 8 caractere.</small>}
      </label>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" className="button primary" disabled={submitting}>
        {isRegister ? 'Creează contul' : 'Conectare'}
      </button>
      <p className="muted">
        {isRegister ? 'Ai deja un cont?' : 'Nu ai încă un cont?'}{' '}
        <button type="button" className="link" onClick={switchMode}>
          {isRegister ? 'Conectează-te' : 'Creează unul'}
        </button>
      </p>
    </form>
  )
}
