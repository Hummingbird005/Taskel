import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import Panel from './ui/Panel'
import { tokensApi, type ApiToken, type CreatedApiToken } from '../api/tokens'
import { formatTimestamp as formatDate } from '../lib/dates'
import { validateTokenName } from '../lib/validation'

export default function Integrations({ onSessionExpired }: { onSessionExpired: () => void }) {
  const [tokens, setTokens] = useState<ApiToken[]>([])
  const [name, setName] = useState('Outlook')
  const [created, setCreated] = useState<CreatedApiToken | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function handleFailure(err: unknown, fallback: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else setError(fallback)
  }

  useEffect(() => {
    tokensApi
      .list()
      .then(setTokens)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca token-urile API.')
      })
  }, [onSessionExpired])

  async function create(e: FormEvent) {
    e.preventDefault()
    const trimmed = name.trim()
    const invalid = validateTokenName(trimmed)
    if (invalid) {
      setError(invalid)
      return
    }
    try {
      const token = await tokensApi.create(trimmed)
      setCreated(token)
      setCopied(false)
      setTokens((prev) => [...prev, { ...token, lastUsedAt: null }])
      setError(null)
    } catch (err) {
      handleFailure(
        err,
        err instanceof ApiError && err.status === 400
          ? 'Poți avea cel mult 10 token-uri API. Revocă unul mai întâi.'
          : 'Nu am putut crea token-ul.',
      )
    }
  }

  async function revoke(token: ApiToken) {
    if (!window.confirm(`Revoci „${token.name}”? Tot ce folosește acest token nu va mai funcționa.`)) return
    try {
      await tokensApi.revoke(token.id)
      setTokens((prev) => prev.filter((t) => t.id !== token.id))
      if (created?.id === token.id) setCreated(null)
    } catch (err) {
      handleFailure(err, 'Nu am putut revoca token-ul.')
    }
  }

  async function copy() {
    if (!created) return
    try {
      await navigator.clipboard.writeText(created.token)
      setCopied(true)
    } catch {
      setCopied(false)
      setError('Nu am putut copia automat. Selectează token-ul și copiază-l manual.')
    }
  }

  return (
    <Panel title="Outlook">
      <p className="muted">
        Trimite emailuri din Outlook în Taskel, ca task-uri. Instalează macro-ul din{' '}
        <code>integrations/outlook</code> din repository-ul Taskel, apoi dă-i un token API creat aici.
      </p>
      <h3>Token-uri API</h3>

      <form onSubmit={create} className="token-form">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Numele token-ului, de ex. Outlook"
          maxLength={50}
          aria-label="Numele token-ului"
        />
        <button type="submit" className="button primary" disabled={!name.trim()}>
          Token nou
        </button>
      </form>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {created && (
        <output className="new-token">
          <p>
            <strong>Copiază acum acest token.</strong> Nu va mai fi afișat.
          </p>
          <div className="token-value">
            <code>{created.token}</code>
            <button type="button" className="button ghost" onClick={copy}>
              {copied ? 'Copiat' : 'Copiază'}
            </button>
          </div>
        </output>
      )}

      {tokens.length === 0 ? (
        <p className="muted">Niciun token API încă.</p>
      ) : (
        <ul className="token-list">
          {tokens.map((token) => (
            <li key={token.id}>
              <div>
                <span className="token-name">{token.name}</span>
                <small className="muted">
                  Creat pe {formatDate(token.createdAt)} ·{' '}
                  {token.lastUsedAt ? `folosit ultima dată pe ${formatDate(token.lastUsedAt)}` : 'nefolosit'}
                </small>
              </div>
              <button type="button" className="button ghost" onClick={() => revoke(token)}>
                Revocă
              </button>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}
