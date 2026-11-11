import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import { tokensApi, type ApiToken, type CreatedApiToken } from '../api/tokens'
import { formatTimestamp as formatDate } from '../lib/dates'

export default function Integrations({ onSessionExpired }: { onSessionExpired: () => void }) {
  const [tokens, setTokens] = useState<ApiToken[]>([])
  const [name, setName] = useState('Outlook')
  const [created, setCreated] = useState<CreatedApiToken | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function handleFailure(err: unknown, fallback: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else setError(err instanceof ApiError && err.status === 400 ? err.message : fallback)
  }

  useEffect(() => {
    tokensApi
      .list()
      .then(setTokens)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Could not load API tokens.')
      })
  }, [onSessionExpired])

  async function create(e: FormEvent) {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) return
    try {
      const token = await tokensApi.create(trimmed)
      setCreated(token)
      setCopied(false)
      setTokens((prev) => [...prev, { ...token, lastUsedAt: null }])
      setError(null)
    } catch (err) {
      handleFailure(err, 'Failed to create the token.')
    }
  }

  async function revoke(token: ApiToken) {
    if (!window.confirm(`Revoke "${token.name}"? Anything using this token will stop working.`)) return
    try {
      await tokensApi.revoke(token.id)
      setTokens((prev) => prev.filter((t) => t.id !== token.id))
      if (created?.id === token.id) setCreated(null)
    } catch (err) {
      handleFailure(err, 'Failed to revoke the token.')
    }
  }

  async function copy() {
    if (!created) return
    try {
      await navigator.clipboard.writeText(created.token)
      setCopied(true)
    } catch {
      setCopied(false)
      setError('Could not copy automatically. Select the token and copy it manually.')
    }
  }

  return (
    <section className="panel" aria-labelledby="integrations-title">
      <h2 id="integrations-title">Outlook</h2>
      <p className="muted">
        Send emails from Outlook to Taskel as tasks. Install the macro from{' '}
        <code>integrations/outlook</code> in the Taskel repository, then give it an API token created here.
      </p>
      <h3>API tokens</h3>

      <form onSubmit={create} className="token-form">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Token name, e.g. Outlook"
          maxLength={50}
          aria-label="Token name"
        />
        <button type="submit" className="button primary" disabled={!name.trim()}>
          New token
        </button>
      </form>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {created && (
        <div className="new-token" role="status">
          <p>
            <strong>Copy this token now.</strong> It will not be shown again.
          </p>
          <div className="token-value">
            <code>{created.token}</code>
            <button type="button" className="button ghost" onClick={copy}>
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
        </div>
      )}

      {tokens.length === 0 ? (
        <p className="muted">No API tokens yet.</p>
      ) : (
        <ul className="token-list">
          {tokens.map((token) => (
            <li key={token.id}>
              <div>
                <span className="token-name">{token.name}</span>
                <small className="muted">
                  Created {formatDate(token.createdAt)} ·{' '}
                  {token.lastUsedAt ? `last used ${formatDate(token.lastUsedAt)}` : 'never used'}
                </small>
              </div>
              <button type="button" className="button ghost" onClick={() => revoke(token)}>
                Revoke
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
