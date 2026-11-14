import { useId, type ReactNode } from 'react'
import type { IntegrationStatus } from '../../api/integrations'
import { formatDateTime } from '../../lib/dates'
import type { Outcome } from './useSettingsForm'

interface FieldProps {
  label: string
  hint?: ReactNode
  error?: string
  /** Spans both columns of the settings grid. */
  wide?: boolean
}

function FieldFrame({ label, hint, error, wide, id, children }: FieldProps & { id: string; children: ReactNode }) {
  return (
    <div className={`field${wide ? ' wide' : ''}${error ? ' invalid' : ''}`}>
      <label htmlFor={id}>{label}</label>
      {children}
      {error ? (
        <small className="field-error" id={`${id}-error`}>
          {error}
        </small>
      ) : (
        hint && <small className="muted">{hint}</small>
      )}
    </div>
  )
}

export function TextField({
  label,
  hint,
  error,
  wide,
  value,
  onChange,
  placeholder,
  type = 'text',
  maxLength = 255,
  disabled,
}: FieldProps & {
  value: string | null
  onChange: (value: string) => void
  placeholder?: string
  type?: 'text' | 'url' | 'email' | 'password' | 'number'
  maxLength?: number
  disabled?: boolean
}) {
  const id = useId()
  return (
    <FieldFrame label={label} hint={hint} error={error} wide={wide} id={id}>
      <input
        id={id}
        type={type}
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        maxLength={type === 'number' ? undefined : maxLength}
        inputMode={type === 'number' ? 'numeric' : undefined}
        autoComplete={type === 'password' ? 'new-password' : 'off'}
        autoCapitalize="none"
        spellCheck={false}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
      />
    </FieldFrame>
  )
}

export function SelectField<T extends string>({
  label,
  hint,
  error,
  wide,
  value,
  options,
  onChange,
}: FieldProps & { value: T; options: { value: T; label: string }[]; onChange: (value: T) => void }) {
  const id = useId()
  return (
    <FieldFrame label={label} hint={hint} error={error} wide={wide} id={id}>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value as T)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldFrame>
  )
}

export function Toggle({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string
  hint?: ReactNode
  checked: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <label className="toggle wide">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>
        {label}
        {hint && <small className="muted">{hint}</small>}
      </span>
    </label>
  )
}

/**
 * A password or client secret. It is never shown: the field says where the one in effect comes
 * from. Empty keeps it (null); "Șterge" drops the saved one (''); typing replaces it.
 */
export function SecretField({
  label,
  status,
  value,
  onChange,
  wide,
}: {
  label: string
  status: IntegrationStatus
  value: string | null
  onChange: (value: string | null) => void
  wide?: boolean
}) {
  const id = useId()
  const clearing = value === ''
  let hint: string
  if (status.secretUnreadable) hint = 'Valoarea salvată nu mai poate fi citită (cheile serverului s-au schimbat). Introdu-o din nou.'
  else if (clearing)
    hint = status.secretInConfiguration
      ? 'Va fi ștearsă la salvare; se folosește cea din configurația serverului.'
      : 'Va fi ștearsă la salvare.'
  else if (status.secretSaved) hint = 'Salvată (nu se afișează). Lasă gol ca să o păstrezi.'
  else if (status.secretInConfiguration) hint = 'Din configurația serverului. Completează doar ca să o înlocuiești.'
  else hint = 'Nesetată.'

  return (
    <FieldFrame label={label} hint={hint} wide={wide} id={id}>
      <div className="secret-input">
        <input
          id={id}
          type="password"
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
          placeholder={status.secretSaved || status.secretInConfiguration ? '••••••••' : ''}
          autoComplete="new-password"
          maxLength={512}
          disabled={clearing}
        />
        {status.secretSaved && (
          <button type="button" className="link" onClick={() => onChange(clearing ? null : '')}>
            {clearing ? 'Anulează' : 'Șterge'}
          </button>
        )}
      </div>
    </FieldFrame>
  )
}

/** "Salvate în aplicație de Ana, 13 nov. 2026, 18:20". */
function savedBy(status: IntegrationStatus): string {
  const who = status.updatedBy ? ` de ${status.updatedBy}` : ''
  const when = status.updatedAt ? `, ${formatDateTime(status.updatedAt)}` : ''
  return `Salvate în aplicație${who}${when}`
}

/** Whether the integration is in use, and where its settings come from. */
export function StatusLine({ status, readyText, offText }: { status: IntegrationStatus; readyText: string; offText: string }) {
  const source = status.saved ? savedBy(status) : 'Din configurația serverului'
  return (
    <p className="status-line">
      <span className={status.ready ? 'status-dot on' : 'status-dot'} aria-hidden="true" />
      <strong>{status.ready ? readyText : offText}</strong>
      <span className="muted">· {source}</span>
    </p>
  )
}

export function OutcomeNote({ outcome }: { outcome: Outcome | null }) {
  if (!outcome) return null
  if (outcome.tone === 'error') {
    return (
      <div className="outcome error" role="alert">
        {outcome.text}
      </div>
    )
  }
  return <output className="outcome ok">{outcome.text}</output>
}
