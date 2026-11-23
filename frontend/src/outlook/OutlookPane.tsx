import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import Icon from '../components/Icon'
import { paneApi, PaneError, savedToken, saveToken, type Group, type Me, type Person } from './api'
import {
  attachmentFile,
  markAsSent,
  officeReady,
  onItemChanged,
  openInBrowser,
  readEmail,
  TASKEL_CATEGORY,
  type OutlookEmail,
} from './office'

const MAX_RECIPIENTS = 50
const MAX_INSTRUCTIONS = 2000
const MAX_RESULTS = 8

/** Someone or something the email can go to, as Taskel's distribute endpoint names it. */
interface Recipient {
  value: string
  name: string
  detail: string
  kind: 'person' | 'group'
}

type Stage =
  | { name: 'starting' }
  | { name: 'outside' }
  | { name: 'connect'; error?: string }
  | { name: 'ready' }
  | { name: 'sending'; progress: string }
  | { name: 'done'; tasks: number; to: string[]; attached: number; problems: string[]; mark: Mark; due: string | null }

/** Whether the email got the "Taskel" category in Outlook. */
type Mark = 'marked' | 'missing' | 'unsupported'

/** Lower case, without Romanian diacritics, so "stefan" finds "Ștefan". */
function plain(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase() || '?'
}

/** A stable colour for an avatar, from its name. */
function hue(name: string): number {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) % 360
  return h
}

function Avatar({ recipient, size = 28 }: { recipient: Recipient; size?: number }) {
  return (
    <span
      className={`ox-avatar ${recipient.kind}`}
      style={{ width: size, height: size, ['--hue' as string]: hue(recipient.name) }}
      aria-hidden="true"
    >
      {recipient.kind === 'group' ? <Icon name="users" size={Math.round(size * 0.55)} /> : initials(recipient.name)}
    </span>
  )
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`
}

/** A date as yyyy-mm-dd, in local time. */
function isoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function daysFromToday(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() + days)
  return isoDate(date)
}

/** The next Friday; a week from today when today is Friday. */
function nextFriday(): string {
  const days = (5 - new Date().getDay() + 7) % 7 || 7
  return daysFromToday(days)
}

/** "luni, 24 noiembrie · mâine": the chosen date in words, so a wrong click shows at once. */
function dueText(value: string): { text: string; past: boolean } {
  const [y, m, d] = value.split('-').map(Number)
  const date = new Date(y, m - 1, d)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const days = Math.round((date.getTime() - today.getTime()) / 86_400_000)
  const words = new Intl.DateTimeFormat('ro-RO', { weekday: 'long', day: 'numeric', month: 'long' }).format(date)
  const when =
    days === 0
      ? 'azi'
      : days === 1
        ? 'mâine'
        : days > 1
          ? `peste ${days} zile`
          : days === -1
            ? 'ieri, în trecut'
            : `acum ${-days} zile, în trecut`
  return { text: `${words} · ${when}`, past: days < 0 }
}

function tasksText(n: number): string {
  return n === 1 ? '1 task creat' : `${n} task-uri create`
}

export default function OutlookPane() {
  const [stage, setStage] = useState<Stage>({ name: 'starting' })
  const [token, setToken] = useState<string | null>(savedToken())
  const [me, setMe] = useState<Me | null>(null)
  const [everyone, setEveryone] = useState<Recipient[]>([])
  const [email, setEmail] = useState<OutlookEmail | null>(null)
  const [chosen, setChosen] = useState<Recipient[]>([])
  const [instructions, setInstructions] = useState('')
  const [withFiles, setWithFiles] = useState(true)
  const [due, setDue] = useState('')
  const [error, setError] = useState<string | null>(null)

  const loadEmail = useCallback(async () => {
    setEmail(await readEmail())
    setChosen([])
    setInstructions('')
    setDue('')
    setWithFiles(true)
    setError(null)
    setStage((s) => (s.name === 'done' || s.name === 'ready' ? { name: 'ready' } : s))
  }, [])

  // Signs in with the token: who the user is, and who the email can go to.
  const connect = useCallback(
    async (candidate: string) => {
      try {
        const [who, people, groups] = await Promise.all([
          paneApi.me(candidate),
          paneApi.people(candidate),
          paneApi.groups(candidate),
        ])
        setMe(who)
        setEveryone([
          ...people
            .map<Recipient>((p: Person) => ({
              value: p.id,
              name: p.displayName || p.email,
              detail: p.id === who.id ? `${p.email} · tu` : p.email,
              kind: 'person',
            }))
            .sort((a, b) => a.name.localeCompare(b.name, 'ro')),
          ...groups
            .map<Recipient>((g: Group) => ({
              value: `group:${g.id}`,
              name: g.name,
              detail: g.memberCount === 1 ? 'grup · 1 membru' : `grup · ${g.memberCount} membri`,
              kind: 'group',
            }))
            .sort((a, b) => a.name.localeCompare(b.name, 'ro')),
        ])
        saveToken(candidate)
        setToken(candidate)
        await loadEmail()
        setStage({ name: 'ready' })
      } catch (err) {
        const message = err instanceof PaneError ? err.message : 'Nu m-am putut conecta la Taskel.'
        if (err instanceof PaneError && err.status === 401) {
          saveToken(null)
          setToken(null)
        }
        setStage({ name: 'connect', error: message })
      }
    },
    [loadEmail],
  )

  useEffect(() => {
    let cancelled = false
    officeReady().then((inOutlook) => {
      if (cancelled) return
      if (!inOutlook) {
        setStage({ name: 'outside' })
        return
      }
      onItemChanged(() => void loadEmail())
      const saved = savedToken()
      if (saved) void connect(saved)
      else setStage({ name: 'connect' })
    })
    return () => {
      cancelled = true
    }
  }, [connect, loadEmail])

  function signOut() {
    saveToken(null)
    setToken(null)
    setMe(null)
    setStage({ name: 'connect' })
  }

  async function send() {
    if (!token || !email) return
    if (chosen.length > MAX_RECIPIENTS) {
      setError(`Poți alege cel mult ${MAX_RECIPIENTS} destinatari.`)
      return
    }
    setError(null)
    setStage({ name: 'sending', progress: 'Se creează task-urile…' })
    let taskIds: string[]
    try {
      taskIds = (
        await paneApi.distribute(token, email, chosen.map((r) => r.value), instructions.trim(), due || null)
      ).taskIds
    } catch (err) {
      if (err instanceof PaneError && err.status === 401) {
        signOut()
        setStage({ name: 'connect', error: err.message })
        return
      }
      setError(err instanceof PaneError ? err.message : 'Nu am putut trimite emailul.')
      setStage({ name: 'ready' })
      return
    }

    // The tasks exist from here on; a file that fails is reported, and does not undo them.
    let attached = 0
    const problems: string[] = []
    const files = withFiles && email.canReadAttachments ? email.attachments : []
    const limits = files.length > 0 ? await paneApi.limits(token) : null
    for (const [index, attachment] of files.entries()) {
      setStage({ name: 'sending', progress: `Se atașează fișierele: ${index + 1} din ${files.length}…` })
      if (limits && attachment.size > limits.maxFileSizeBytes) {
        problems.push(`${attachment.name}: mai mare decât limita de ${formatSize(limits.maxFileSizeBytes)}`)
        continue
      }
      try {
        const file = await attachmentFile(attachment)
        if (!file) {
          problems.push(`${attachment.name}: este doar un link (fișier în cloud)`)
          continue
        }
        let stored = false
        let problem: string | null = null
        for (const id of taskIds) {
          try {
            await paneApi.upload(token, id, file)
            stored = true
          } catch (err) {
            problem ??= err instanceof PaneError ? err.message : 'nu a putut fi trimis'
          }
        }
        if (stored) attached++
        if (problem) problems.push(`${attachment.name}: ${problem}${taskIds.length > 1 && stored ? ' (la unii destinatari)' : ''}`)
      } catch {
        problems.push(`${attachment.name}: Outlook nu a dat conținutul fișierului`)
      }
    }

    setStage({ name: 'sending', progress: 'Se marchează emailul…' })
    const mark: Mark = await markAsSent().catch(() => 'missing' as const)

    setStage({
      name: 'done',
      tasks: taskIds.length,
      to: chosen.length === 0 ? ['tine'] : chosen.map((r) => r.name),
      attached,
      problems,
      mark,
      due: due || null,
    })
  }

  if (stage.name === 'starting') {
    return (
      <div className="ox-center" aria-busy="true">
        <span className="ox-spinner" aria-hidden="true" />
        <p className="ox-muted">Se pornește Taskel…</p>
      </div>
    )
  }

  if (stage.name === 'outside') {
    return (
      <div className="ox-center">
        <Brand />
        <p>Această pagină este panoul Taskel pentru Outlook.</p>
        <p className="ox-muted">Adaugă add-in-ul în Outlook, apoi deschide un email și apasă „Trimite în Taskel”.</p>
      </div>
    )
  }

  if (stage.name === 'connect') return <Connect error={stage.error} onConnect={connect} />

  return (
    <div className="ox-pane">
      <header className="ox-head">
        <Brand />
        {me && (
          <div className="ox-me">
            <span className="ox-me-name" title={me.email}>
              {me.displayName}
            </span>
            <button type="button" className="ox-icon-button" onClick={signOut} title="Deconectează" aria-label="Deconectează">
              <Icon name="logout" size={16} />
            </button>
          </div>
        )}
      </header>

      {stage.name === 'done' ? (
        <Done stage={stage} onAgain={() => void loadEmail()} />
      ) : !email ? (
        <div className="ox-center">
          <Icon name="mail" size={28} />
          <p className="ox-muted">Selectează un email ca să îl trimiți în Taskel.</p>
        </div>
      ) : (
        <form
          className="ox-form"
          onSubmit={(e: FormEvent) => {
            e.preventDefault()
            void send()
          }}
        >
          <EmailCard email={email} />

          <section className="ox-section">
            <h2 className="ox-label">Cui trimiți</h2>
            <RecipientPicker everyone={everyone} chosen={chosen} onChange={setChosen} disabled={stage.name === 'sending'} />
            <p className="ox-hint">
              {chosen.length === 0
                ? 'Fără destinatari, task-ul rămâne la tine.'
                : chosen.length === 1
                  ? 'Primește task-ul; răspunsul lui vine la tine pe email.'
                  : `Fiecare dintre cei ${chosen.length} primește task-ul lui; răspunsurile vin la tine.`}
            </p>
          </section>

          <DueField value={due} onChange={setDue} disabled={stage.name === 'sending'} />

          <section className="ox-section">
            <label className="ox-label" htmlFor="ox-instructions">
              Indicații <span className="ox-optional">opțional</span>
            </label>
            <textarea
              id="ox-instructions"
              className="ox-textarea"
              value={instructions}
              onChange={(e) => setInstructions(e.target.value)}
              maxLength={MAX_INSTRUCTIONS}
              rows={4}
              placeholder="Ce aștepți de la cei care primesc task-ul"
              disabled={stage.name === 'sending'}
            />
            {instructions.length > MAX_INSTRUCTIONS - 200 && (
              <p className="ox-hint ox-count">
                {instructions.length} / {MAX_INSTRUCTIONS}
              </p>
            )}
          </section>

          {email.attachments.length > 0 && (
            <section className="ox-section">
              <label className="ox-switch">
                <input
                  type="checkbox"
                  checked={withFiles && email.canReadAttachments}
                  disabled={!email.canReadAttachments || stage.name === 'sending'}
                  onChange={(e) => setWithFiles(e.target.checked)}
                />
                <span className="ox-switch-track" aria-hidden="true" />
                <span>
                  Atașează fișierele ({email.attachments.length})
                  <small className="ox-muted">
                    {email.canReadAttachments
                      ? email.attachments.map((a) => a.name).join(', ')
                      : 'Această versiune de Outlook nu permite trimiterea fișierelor.'}
                  </small>
                </span>
              </label>
            </section>
          )}

          {error && (
            <p className="ox-error" role="alert">
              <Icon name="alert" size={16} />
              {error}
            </p>
          )}

          <footer className="ox-footer">
            <button type="submit" className="ox-primary" disabled={stage.name === 'sending'}>
              {stage.name === 'sending' ? (
                <>
                  <span className="ox-spinner small" aria-hidden="true" />
                  {stage.progress}
                </>
              ) : (
                <>
                  <Icon name="send" size={16} />
                  {chosen.length === 0
                    ? 'Trimite în Taskel'
                    : chosen.length === 1
                      ? `Trimite la ${chosen[0].name}`
                      : `Trimite la ${chosen.length} destinatari`}
                </>
              )}
            </button>
          </footer>
        </form>
      )}
    </div>
  )
}

function DueField({ value, onChange, disabled }: { value: string; onChange: (next: string) => void; disabled: boolean }) {
  const quick = [
    { label: 'Azi', value: daysFromToday(0) },
    { label: 'Mâine', value: daysFromToday(1) },
    { label: 'Vineri', value: nextFriday() },
    { label: '+1 săpt.', value: daysFromToday(7) },
  ]
  const words = value ? dueText(value) : null
  return (
    <section className="ox-section">
      <label className="ox-label" htmlFor="ox-due">
        Termen <span className="ox-optional">opțional</span>
      </label>
      <div className="ox-due">
        <input
          id="ox-due"
          type="date"
          className="ox-input ox-date"
          value={value}
          min="2000-01-01"
          max="2100-12-31"
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
        />
        {value && (
          <button
            type="button"
            className="ox-icon-button"
            onClick={() => onChange('')}
            title="Fără termen"
            aria-label="Șterge termenul"
            disabled={disabled}
          >
            <Icon name="close" size={14} />
          </button>
        )}
      </div>
      <div className="ox-quick" role="group" aria-label="Termene rapide">
        {quick.map((q) => (
          <button
            key={q.label}
            type="button"
            className={`ox-pill ${value === q.value ? 'active' : ''}`}
            aria-pressed={value === q.value}
            onClick={() => onChange(q.value)}
            disabled={disabled}
          >
            {q.label}
          </button>
        ))}
      </div>
      <p className={`ox-hint ${words?.past ? 'ox-past' : ''}`}>{words ? words.text : 'Fără termen.'}</p>
    </section>
  )
}

function Brand() {
  return (
    <span className="ox-brand">
      <span className="ox-logo" aria-hidden="true">
        <Icon name="check" size={14} />
      </span>
      Taskel
    </span>
  )
}

function EmailCard({ email }: { email: OutlookEmail }) {
  return (
    <section className="ox-card" aria-label="Emailul">
      <p className="ox-subject">{email.subject || '(fără subiect)'}</p>
      {email.sender && <p className="ox-from">{email.sender}</p>}
      <div className="ox-tags">
        {email.importance === 'high' && <span className="ox-tag high">Important</span>}
        {email.attachments.length > 0 && (
          <span className="ox-tag">
            <Icon name="paperclip" size={12} />
            {email.attachments.length === 1 ? '1 fișier' : `${email.attachments.length} fișiere`}
          </span>
        )}
        {email.senderEmail && (
          <span className="ox-tag">
            <Icon name="reply" size={12} />
            Răspunsul ajunge în Outlook
          </span>
        )}
      </div>
    </section>
  )
}

function RecipientPicker({
  everyone,
  chosen,
  onChange,
  disabled,
}: {
  everyone: Recipient[]
  chosen: Recipient[]
  onChange: (next: Recipient[]) => void
  disabled: boolean
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const input = useRef<HTMLInputElement>(null)

  const matches = useMemo(() => {
    const q = plain(query.trim())
    const taken = new Set(chosen.map((r) => r.value))
    return everyone
      .filter((r) => !taken.has(r.value) && (!q || plain(`${r.name} ${r.detail}`).includes(q)))
      .slice(0, MAX_RESULTS)
  }, [everyone, chosen, query])

  function add(recipient: Recipient) {
    onChange([...chosen, recipient])
    setQuery('')
    setActive(0)
    input.current?.focus()
  }

  function onKey(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setOpen(true)
      setActive((a) => Math.min(a + 1, matches.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      if (open && matches[active]) {
        e.preventDefault()
        add(matches[active])
      }
    } else if (e.key === 'Escape') {
      setOpen(false)
    } else if (e.key === 'Backspace' && !query && chosen.length > 0) {
      onChange(chosen.slice(0, -1))
    }
  }

  return (
    <div className="ox-picker">
      <div className={`ox-field ${disabled ? 'disabled' : ''}`} onClick={() => input.current?.focus()}>
        {chosen.map((r) => (
          <span key={r.value} className="ox-chip">
            <Avatar recipient={r} size={20} />
            <span className="ox-chip-name">{r.name}</span>
            <button
              type="button"
              className="ox-chip-remove"
              onClick={(e) => {
                e.stopPropagation()
                onChange(chosen.filter((c) => c.value !== r.value))
              }}
              aria-label={`Scoate ${r.name}`}
              disabled={disabled}
            >
              <Icon name="close" size={12} />
            </button>
          </span>
        ))}
        <span className="ox-search">
          {chosen.length === 0 && <Icon name="search" size={15} />}
          <input
            ref={input}
            value={query}
            onChange={(e) => {
              setQuery(e.target.value)
              setActive(0)
              setOpen(true)
            }}
            onFocus={() => setOpen(true)}
            onBlur={() => setTimeout(() => setOpen(false), 150)}
            onKeyDown={onKey}
            placeholder={chosen.length === 0 ? 'Caută o persoană sau un grup' : 'Adaugă'}
            aria-label="Caută destinatari"
            role="combobox"
            aria-expanded={open && matches.length > 0}
            aria-controls="ox-results"
            disabled={disabled}
          />
        </span>
      </div>
      {open && !disabled && (
        <ul className="ox-results" id="ox-results" role="listbox">
          {matches.length === 0 ? (
            <li className="ox-empty">{query ? 'Nimeni cu acest nume.' : 'Toți sunt deja aleși.'}</li>
          ) : (
            matches.map((r, i) => (
              <li
                key={r.value}
                role="option"
                aria-selected={i === active}
                className={i === active ? 'active' : ''}
                onMouseDown={(e) => {
                  e.preventDefault()
                  add(r)
                }}
                onMouseEnter={() => setActive(i)}
              >
                <Avatar recipient={r} />
                <span className="ox-result-text">
                  <span className="ox-result-name">{r.name}</span>
                  <span className="ox-result-detail">{r.detail}</span>
                </span>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  )
}

function Connect({ error, onConnect }: { error?: string; onConnect: (token: string) => Promise<void> }) {
  const [value, setValue] = useState('')
  const [busy, setBusy] = useState(false)
  const trimmed = value.trim()

  return (
    <div className="ox-pane">
      <header className="ox-head">
        <Brand />
      </header>
      <form
        className="ox-form"
        onSubmit={async (e) => {
          e.preventDefault()
          if (!trimmed) return
          setBusy(true)
          await onConnect(trimmed)
          setBusy(false)
        }}
      >
        <section className="ox-welcome">
          <span className="ox-welcome-icon" aria-hidden="true">
            <Icon name="plug" size={22} />
          </span>
          <h1>Conectează Outlook la Taskel</h1>
          <p className="ox-muted">O singură dată. Ai nevoie de un token API, pe care îl creezi în Taskel.</p>
        </section>
        <ol className="ox-steps">
          <li>
            Deschide{' '}
            <button type="button" className="ox-link" onClick={() => openInBrowser(`${location.origin}/`)}>
              Taskel
            </button>{' '}
            → <b>Integrări</b> → <b>Token nou</b>.
          </li>
          <li>Copiază token-ul (începe cu tsk_) și lipește-l mai jos.</li>
        </ol>
        <section className="ox-section">
          <label className="ox-label" htmlFor="ox-token">
            Token API
          </label>
          <input
            id="ox-token"
            className="ox-input"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            placeholder="tsk_…"
            autoComplete="off"
            spellCheck={false}
            disabled={busy}
          />
        </section>
        {error && (
          <p className="ox-error" role="alert">
            <Icon name="alert" size={16} />
            {error}
          </p>
        )}
        <footer className="ox-footer">
          <button type="submit" className="ox-primary" disabled={busy || !trimmed}>
            {busy ? <span className="ox-spinner small" aria-hidden="true" /> : <Icon name="plug" size={16} />}
            Conectează
          </button>
        </footer>
      </form>
    </div>
  )
}

function Done({ stage, onAgain }: { stage: Extract<Stage, { name: 'done' }>; onAgain: () => void }) {
  return (
    <div className="ox-done" role="status">
      <span className="ox-done-icon" aria-hidden="true">
        <Icon name="check" size={26} />
      </span>
      <h1>{tasksText(stage.tasks)}</h1>
      <p className="ox-muted">
        {stage.to.length === 1 && stage.to[0] === 'tine' ? 'Task-ul este la tine.' : `Pentru ${stage.to.join(', ')}.`}
        {stage.attached > 0 && ` ${stage.attached === 1 ? '1 fișier atașat' : `${stage.attached} fișiere atașate`}.`}
        {stage.due && ` Termen: ${dueText(stage.due).text.split(' · ')[0]}.`}
      </p>
      {stage.mark === 'marked' && (
        <p className="ox-marked">
          <span className="ox-category" aria-hidden="true" />
          Emailul are categoria „{TASKEL_CATEGORY}” în Outlook.
        </p>
      )}
      {stage.mark === 'missing' && (
        <div className="ox-warning">
          <p>
            <Icon name="alert" size={15} /> Emailul nu a putut fi marcat în Outlook.
          </p>
          <p className="ox-plain">
            Creează o dată categoria „{TASKEL_CATEGORY}”: clic dreapta pe un email → <b>Categorize</b> →{' '}
            <b>All Categories</b> → <b>New</b>. De atunci, emailurile trimise primesc categoria singure.
          </p>
        </div>
      )}
      {stage.problems.length > 0 && (
        <div className="ox-warning">
          <p>
            <Icon name="alert" size={15} /> Unele fișiere nu au fost atașate:
          </p>
          <ul>
            {stage.problems.map((p) => (
              <li key={p}>{p}</li>
            ))}
          </ul>
        </div>
      )}
      <div className="ox-done-actions">
        <button type="button" className="ox-primary" onClick={() => openInBrowser(`${location.origin}/`)}>
          Deschide Taskel
        </button>
        <button type="button" className="ox-secondary" onClick={onAgain}>
          Trimite din nou
        </button>
      </div>
    </div>
  )
}
