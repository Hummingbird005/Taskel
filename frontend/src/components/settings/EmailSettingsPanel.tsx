import { useCallback, type FormEvent } from 'react'
import { integrationsApi, type EmailSettings, type EmailSettingsForm } from '../../api/integrations'
import {
  collect,
  orNull,
  validateHost,
  validateHttpUrl,
  validateOptionalEmail,
  validatePlain,
  validatePort,
} from '../../lib/settingsValidation'
import Button from '../ui/Button'
import Panel from '../ui/Panel'
import { TableSkeleton } from '../ui/Skeleton'
import { OutcomeNote, SecretField, StatusLine, TextField, Toggle } from './SettingsFields'
import { useSettingsForm } from './useSettingsForm'

const toForm = ({ status: _status, ...rest }: EmailSettings): EmailSettingsForm => ({ ...rest, password: null })

function validate(form: EmailSettingsForm) {
  return collect<EmailSettingsForm>({
    host: validateHost(form.host, false),
    port: validatePort(form.port),
    username: validatePlain(form.username, false, 256),
    fromAddress: validateOptionalEmail(form.fromAddress),
    fromName: validatePlain(form.fromName, false, 100),
    publicUrl: validateHttpUrl(form.publicUrl, false),
  })
}

/** What the API receives: empty fields as null, the password as the field says. */
function payload(form: EmailSettingsForm): EmailSettingsForm {
  return {
    ...form,
    host: orNull(form.host),
    username: orNull(form.username),
    fromAddress: orNull(form.fromAddress),
    fromName: orNull(form.fromName),
    publicUrl: orNull(form.publicUrl),
  }
}

/** The mail server Taskel sends through: replies to email tasks, notifications, test messages. */
export default function EmailSettingsPanel({ onSessionExpired }: { onSessionExpired: () => void }) {
  const load = useCallback(() => integrationsApi.email(), [])
  const { settings, form, loadError, busy, outcome, errors, setErrors, update, run, accept } = useSettingsForm(
    load,
    toForm,
    onSessionExpired,
  )

  function checked(): EmailSettingsForm | null {
    if (!form) return null
    const invalid = validate(form)
    setErrors(invalid)
    return Object.keys(invalid).length === 0 ? payload(form) : null
  }

  function save(e: FormEvent) {
    e.preventDefault()
    const valid = checked()
    if (!valid) return
    run(
      'save',
      async () => {
        accept(await integrationsApi.saveEmail(valid))
        return { tone: 'ok', text: 'Setările serverului de email au fost salvate și se aplică imediat.' }
      },
      'Setările nu au putut fi salvate.',
    )
  }

  function test() {
    const valid = checked()
    if (!valid) return
    if (!valid.host || !(valid.fromAddress ?? valid.username)) {
      setErrors({ host: valid.host ? undefined : 'Completează serverul ca să poți trimite un test.' })
      return
    }
    run(
      'test',
      async () => {
        const { sentTo } = await integrationsApi.testEmail(valid)
        return { tone: 'ok', text: `Mesajul de test a fost trimis la ${sentTo}. Verifică-ți căsuța de email.` }
      },
      'Mesajul de test nu a putut fi trimis.',
    )
  }

  function reset() {
    if (!window.confirm('Renunți la setările salvate aici și revii la cele din configurația serverului?')) return
    run(
      'reset',
      async () => {
        accept(await integrationsApi.resetEmail())
        return { tone: 'ok', text: 'Se folosesc din nou setările din configurația serverului.' }
      },
      'Nu am putut reveni la configurația serverului.',
    )
  }

  return (
    <Panel title="Server de email (SMTP)">
      <p className="muted">
        Prin acest server Taskel trimite răspunsurile la task-urile venite pe email și notificările despre task-uri noi.
      </p>
      {loadError && (
        <p className="error" role="alert">
          Nu am putut încărca setările de email.
        </p>
      )}
      {!settings || !form ? (
        !loadError && <TableSkeleton rows={3} />
      ) : (
        <form onSubmit={save} noValidate>
          <StatusLine status={settings.status} readyText="Configurat" offText="Neconfigurat: Taskel nu trimite emailuri" />
          <div className="settings-grid">
            <TextField
              label="Server SMTP"
              value={form.host}
              onChange={(v) => update('host', v)}
              placeholder="smtp.firma.ro"
              error={errors.host}
            />
            <TextField
              label="Port"
              type="number"
              value={String(form.port)}
              onChange={(v) => update('port', Number(v))}
              hint="587 cu STARTTLS, 465 cu TLS de la început."
              error={errors.port}
            />
            <TextField
              label="Utilizator"
              value={form.username}
              onChange={(v) => update('username', v)}
              placeholder="taskel@firma.ro"
              error={errors.username}
            />
            <SecretField label="Parolă" status={settings.status} value={form.password} onChange={(v) => update('password', v)} />
            <TextField
              label="Adresa expeditorului"
              type="email"
              value={form.fromAddress}
              onChange={(v) => update('fromAddress', v)}
              hint="Implicit, utilizatorul de mai sus."
              error={errors.fromAddress}
            />
            <TextField
              label="Numele expeditorului"
              value={form.fromName}
              onChange={(v) => update('fromName', v)}
              placeholder="Taskel"
              maxLength={100}
              error={errors.fromName}
            />
            <Toggle
              label="Cere conexiune criptată (TLS)"
              hint="Dezactivează doar pentru un server de test care nu oferă TLS."
              checked={form.requireTls}
              onChange={(v) => update('requireTls', v)}
            />
            <Toggle
              label="Trimite notificări când cineva primește un task"
              hint="Fiecare utilizator le poate opri pentru el, din pagina Integrări."
              checked={form.notificationsEnabled}
              onChange={(v) => update('notificationsEnabled', v)}
            />
            <TextField
              wide
              label="Adresa aplicației"
              type="url"
              value={form.publicUrl}
              onChange={(v) => update('publicUrl', v)}
              placeholder="https://taskel.firma.ro"
              hint="Pusă ca link în emailuri și folosită ca adresă de întoarcere după conectarea cu Keycloak."
              error={errors.publicUrl}
            />
          </div>
          <div className="settings-actions">
            <Button type="submit" variant="primary" loading={busy === 'save'}>
              Salvează
            </Button>
            <Button onClick={test} loading={busy === 'test'}>
              Trimite un email de test
            </Button>
            {settings.status.saved && (
              <Button variant="danger" onClick={reset} loading={busy === 'reset'}>
                Revino la configurația serverului
              </Button>
            )}
          </div>
          <OutcomeNote outcome={outcome} />
        </form>
      )}
    </Panel>
  )
}
