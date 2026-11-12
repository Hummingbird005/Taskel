import { useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import Panel from './ui/Panel'
import { notificationsApi, type NotificationSettings } from '../api/notifications'

/** The user's choice about being emailed when a task reaches them. Saved as soon as it changes. */
export default function EmailNotifications({ onSessionExpired }: { onSessionExpired: () => void }) {
  const [settings, setSettings] = useState<NotificationSettings | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    let cancelled = false
    notificationsApi
      .get()
      .then((loaded) => {
        if (!cancelled) setSettings(loaded)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca setările de notificare.')
      })
    return () => {
      cancelled = true
    }
  }, [onSessionExpired])

  async function change(on: boolean) {
    if (!settings) return
    const previous = settings
    setSettings({ ...settings, emailOnNewTask: on })
    setSaved(false)
    try {
      setSettings(await notificationsApi.save(on))
      setError(null)
      setSaved(true)
    } catch (err) {
      setSettings(previous)
      if (err instanceof ApiError && err.status === 401) onSessionExpired()
      else setError('Nu am putut salva setarea.')
    }
  }

  return (
    <Panel title="Notificări pe email">
      <p className="muted">
        Primești un email când cineva îți alocă sau îți direcționează un task, ori când un task este dat unui grup din
        care faci parte.
      </p>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {settings && (
        <>
          <label className="notify-toggle">
            <input type="checkbox" checked={settings.emailOnNewTask} onChange={(e) => void change(e.target.checked)} />
            <span>
              Trimite-mi un email când primesc un task
              <small className="muted">Către {settings.address}</small>
            </span>
            {saved && (
              <span className="saved" role="status">
                Salvat
              </span>
            )}
          </label>
          {!settings.available && (
            <p className="muted notify-unavailable">
              Trimiterea de emailuri nu este configurată pe serverul Taskel, deci deocamdată nu pleacă nicio
              notificare. Alegerea ta se păstrează.
            </p>
          )}
        </>
      )}
    </Panel>
  )
}
