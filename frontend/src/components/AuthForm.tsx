import { useEffect, useState, type FormEvent } from 'react'
import { authApi, DEFAULT_AUTH_OPTIONS, type AuthOptions, type User } from '../api/auth'
import { ApiError } from '../api/client'
import { firstError, validateDirectoryUsername, validateEmail, validatePassword } from '../lib/validation'

/** How the user is signing in: a domain account, a local account, or creating a local account. */
type Mode = 'directory' | 'login' | 'register'

function errorText(err: unknown, mode: Mode): string {
  if (!(err instanceof ApiError)) return 'Nu am putut contacta serverul Taskel.'
  switch (err.status) {
    case 401:
      return mode === 'directory' ? 'Nume de utilizator sau parolă greșită.' : 'Email sau parolă greșită.'
    case 409:
      return mode === 'directory'
        ? 'Adresa de email a acestui cont este folosită deja de alt cont de domeniu în Taskel.'
        : 'Există deja un cont cu acest email.'
    case 429:
      return 'Prea multe încercări de conectare. Așteaptă un minut și încearcă din nou.'
    case 503:
      return 'Active Directory nu este disponibil acum. Încearcă din nou mai târziu.'
    case 403:
      return 'Crearea de conturi locale este dezactivată.'
    case 400:
      return mode === 'register'
        ? 'Verifică adresa de email și parola (cel puțin 8 caractere).'
        : 'Verifică datele introduse.'
    default:
      return 'A apărut o eroare. Încearcă din nou.'
  }
}

export default function AuthForm({ onAuthenticated }: { onAuthenticated: (user: User) => void }) {
  const [options, setOptions] = useState<AuthOptions>(DEFAULT_AUTH_OPTIONS)
  const [mode, setMode] = useState<Mode>('login')
  const [identifier, setIdentifier] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    let cancelled = false
    authApi
      .options()
      .then((loaded) => {
        if (cancelled) return
        setOptions(loaded)
        // A domain account is the usual way in when the directory is connected.
        if (loaded.activeDirectory) setMode('directory')
      })
      .catch(() => {
        // Keep the defaults: local sign-in still works without knowing the options.
      })
    return () => {
      cancelled = true
    }
  }, [])

  const isDirectory = mode === 'directory'
  const isRegister = mode === 'register'

  function switchMode(next: Mode) {
    setMode(next)
    setIdentifier('')
    setPassword('')
    setError(null)
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    const invalid = firstError(
      isDirectory ? validateDirectoryUsername(identifier) : validateEmail(identifier),
      validatePassword(password, isRegister),
    )
    if (invalid) {
      setError(invalid)
      return
    }

    setSubmitting(true)
    setError(null)
    try {
      const name = identifier.trim()
      const user = isDirectory
        ? await authApi.loginDirectory(name, password)
        : isRegister
          ? await authApi.register(name, password)
          : await authApi.login(name, password)
      onAuthenticated(user)
    } catch (err) {
      setError(errorText(err, mode))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="auth-form" noValidate>
      <h2>{isRegister ? 'Creează-ți contul' : 'Conectează-te la Taskel'}</h2>

      {options.activeDirectory && !isRegister && (
        <div className="auth-tabs" role="tablist" aria-label="Tipul contului">
          <button type="button" role="tab" aria-selected={isDirectory} onClick={() => switchMode('directory')}>
            Cont de domeniu
          </button>
          <button type="button" role="tab" aria-selected={!isDirectory} onClick={() => switchMode('login')}>
            Cont local
          </button>
        </div>
      )}

      {isDirectory ? (
        <label>
          Utilizator
          <input
            value={identifier}
            onChange={(e) => setIdentifier(e.target.value)}
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            maxLength={128}
            placeholder="ion.popescu"
          />
          <small className="muted">
            Contul tău de Windows{options.activeDirectoryDomain ? ` din domeniul ${options.activeDirectoryDomain}` : ''}.
          </small>
        </label>
      ) : (
        <label>
          Email
          <input
            type="email"
            value={identifier}
            onChange={(e) => setIdentifier(e.target.value)}
            autoComplete="email"
            maxLength={254}
          />
        </label>
      )}

      <label>
        Parolă
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete={isRegister ? 'new-password' : 'current-password'}
          maxLength={256}
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

      {isRegister ? (
        <p className="muted">
          Ai deja un cont?{' '}
          <button type="button" className="link" onClick={() => switchMode(options.activeDirectory ? 'directory' : 'login')}>
            Conectează-te
          </button>
        </p>
      ) : (
        !isDirectory &&
        options.localRegistration && (
          <p className="muted">
            Nu ai încă un cont?{' '}
            <button type="button" className="link" onClick={() => switchMode('register')}>
              Creează unul
            </button>
          </p>
        )
      )}
    </form>
  )
}
