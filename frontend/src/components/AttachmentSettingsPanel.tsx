import { useEffect, useState, type FormEvent } from 'react'
import { adminApi, type AttachmentSettings } from '../api/admin'
import { ApiError } from '../api/client'
import Button from './ui/Button'
import Panel from './ui/Panel'

/** The administrator's setting for how large an attached file may be. */
export default function AttachmentSettingsPanel({ onSessionExpired, onChanged }: { onSessionExpired: () => void; onChanged: () => void }) {
  const [settings, setSettings] = useState<AttachmentSettings | null>(null)
  const [value, setValue] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    let cancelled = false
    adminApi
      .attachmentSettings()
      .then((loaded) => {
        if (cancelled) return
        setSettings(loaded)
        setValue(String(loaded.maxFileSizeMb))
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca setarea pentru fișiere.')
      })
    return () => {
      cancelled = true
    }
  }, [onSessionExpired])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!settings || saving) return
    const megabytes = Number(value)
    if (!/^\d+$/.test(value.trim()) || megabytes < 1 || megabytes > settings.maxConfigurableMb) {
      setError(`Introdu un număr întreg între 1 și ${settings.maxConfigurableMb}.`)
      return
    }
    setSaving(true)
    try {
      const updated = await adminApi.saveAttachmentSettings(megabytes)
      setSettings(updated)
      setValue(String(updated.maxFileSizeMb))
      setError(null)
      setSaved(true)
      onChanged()
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired()
      else setError('Nu am putut salva limita.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Panel title="Fișiere atașate">
      <p className="muted">
        Mărimea maximă a unui fișier atașat la un task, fie din aplicație, fie dintr-un email trimis din Outlook.
        Se aplică imediat, pentru toți utilizatorii.
      </p>
      {settings && (
        <form className="limit-form" onSubmit={handleSubmit} noValidate>
          <label className="field">
            <span>Mărime maximă pe fișier (MB)</span>
            <input
              type="number"
              inputMode="numeric"
              min={1}
              max={settings.maxConfigurableMb}
              step={1}
              value={value}
              onChange={(e) => {
                setValue(e.target.value)
                setError(null)
                setSaved(false)
              }}
              aria-label="Mărime maximă pe fișier, în MB"
            />
          </label>
          <Button type="submit" variant="primary" loading={saving} disabled={value.trim() === String(settings.maxFileSizeMb)}>
            Salvează
          </Button>
          {saved && (
            <span className="saved" role="status">
              Salvat
            </span>
          )}
        </form>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {settings && (
        <p className="muted team-note">
          Valoarea implicită a serverului este {settings.defaultMaxFileSizeMb} MB. Un proxy aflat în fața serverului
          poate avea propria limită, mai mică.
        </p>
      )}
    </Panel>
  )
}
