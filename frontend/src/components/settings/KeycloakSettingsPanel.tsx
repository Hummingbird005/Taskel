import { useCallback, useState, type FormEvent } from 'react'
import {
  integrationsApi,
  type KeycloakCheck,
  type KeycloakImportResult,
  type KeycloakRefusal,
  type KeycloakSettings,
  type KeycloakSettingsForm,
} from '../../api/integrations'
import { collect, orNull, validateIdentifier, validatePlain, validateRealmAddress } from '../../lib/settingsValidation'
import Button from '../ui/Button'
import Panel from '../ui/Panel'
import { TableSkeleton } from '../ui/Skeleton'
import { OutcomeNote, SecretField, StatusLine, TextField, Toggle } from './SettingsFields'
import { useSettingsForm, type Outcome } from './useSettingsForm'

const toForm = ({
  status: _status,
  redirectUri: _redirectUri,
  postLogoutRedirectUri: _postLogout,
  ...rest
}: KeycloakSettings): KeycloakSettingsForm => ({ ...rest, clientSecret: null })

function validate(form: KeycloakSettingsForm, forTest: boolean) {
  const required = form.enabled || forTest
  return collect<KeycloakSettingsForm>({
    authority: validateRealmAddress(form.authority, required, form.requireHttpsMetadata),
    clientId: validateIdentifier(form.clientId, required),
    displayName: validatePlain(form.displayName, true, 60),
    requiredRole: validatePlain(form.requiredRole, false, 255),
    adminRole: validatePlain(form.adminRole, false, 255),
    managerRole: validatePlain(form.managerRole, false, 255),
    departmentClaim: validateIdentifier(form.departmentClaim, false),
    groupsClaim: validateIdentifier(form.groupsClaim, form.syncGroups),
  })
}

function payload(form: KeycloakSettingsForm): KeycloakSettingsForm {
  return {
    ...form,
    authority: orNull(form.authority),
    clientId: orNull(form.clientId),
    displayName: form.displayName.trim(),
    requiredRole: orNull(form.requiredRole),
    adminRole: orNull(form.adminRole),
    managerRole: orNull(form.managerRole),
    departmentClaim: orNull(form.departmentClaim),
    groupsClaim: orNull(form.groupsClaim) ?? 'groups',
  }
}

const yesNo = (value: boolean | null) => (value ? 'da' : 'nu')

function checkOutcome(check: KeycloakCheck): Outcome {
  const serviceOk = check.serviceAccount !== false && check.canListUsers !== false
  return {
    tone: serviceOk ? 'ok' : 'error',
    text: (
      <>
        Realm găsit: <code>{check.issuer}</code>.
        {check.serviceAccount === null ? (
          <> Fără secret de client nu se poate verifica importul de utilizatori.</>
        ) : (
          <>
            {' '}
            Cont de serviciu: {yesNo(check.serviceAccount)}
            {check.canListUsers !== null && <>, citire utilizatori: {yesNo(check.canListUsers)}</>}.
          </>
        )}
        {check.problem && <span className="outcome-detail"> Detalii: {check.problem}</span>}
      </>
    ),
  }
}

const REFUSAL: Record<KeycloakRefusal, string> = {
  noEmail: 'fără adresă de email',
  emailTaken: 'adresa e folosită de un cont de domeniu sau de alt cont Keycloak',
  emailUnverified: 'adresa aparține unui cont local, dar nu e verificată în Keycloak',
  roleMissing: 'fără rolul cerut',
}

function importOutcome(result: KeycloakImportResult): Outcome {
  return {
    tone: 'ok',
    text: (
      <>
        Din {result.total} utilizatori activi: {result.created} conturi noi, {result.updated} actualizate
        {result.skippedCount > 0 && <>, {result.skippedCount} sărite</>}.
        {result.skipped.length > 0 && (
          <ul className="outcome-list">
            {result.skipped.map((skip) => (
              <li key={skip.who}>
                {skip.who}: {REFUSAL[skip.reason]}
              </li>
            ))}
          </ul>
        )}
      </>
    ),
  }
}

/** Single sign-on through Keycloak (OpenID Connect), and importing its users. */
export default function KeycloakSettingsPanel({ onSessionExpired }: { onSessionExpired: () => void }) {
  const load = useCallback(() => integrationsApi.keycloak(), [])
  const { settings, form, loadError, busy, outcome, errors, setErrors, update, run, accept } = useSettingsForm(
    load,
    toForm,
    onSessionExpired,
  )
  const [confirmImport, setConfirmImport] = useState(false)

  function checked(forTest: boolean): KeycloakSettingsForm | null {
    if (!form) return null
    const invalid = validate(form, forTest)
    setErrors(invalid)
    return Object.keys(invalid).length === 0 ? payload(form) : null
  }

  function save(e: FormEvent) {
    e.preventDefault()
    const valid = checked(false)
    if (!valid) return
    void run(
      'save',
      async () => {
        accept(await integrationsApi.saveKeycloak(valid))
        return { tone: 'ok', text: 'Setările Keycloak au fost salvate și se aplică la următoarea conectare.' }
      },
      'Setările nu au putut fi salvate.',
    )
  }

  function test() {
    const valid = checked(true)
    if (!valid) return
    void run('test', async () => checkOutcome(await integrationsApi.testKeycloak(valid)), 'Keycloak nu a putut fi verificat.')
  }

  function importUsers() {
    setConfirmImport(false)
    void run('import', async () => importOutcome(await integrationsApi.importKeycloakUsers()), 'Importul nu a reușit.')
  }

  function reset() {
    if (!window.confirm('Renunți la setările salvate aici și revii la cele din configurația serverului?')) return
    void run(
      'reset',
      async () => {
        accept(await integrationsApi.resetKeycloak())
        return { tone: 'ok', text: 'Se folosesc din nou setările din configurația serverului.' }
      },
      'Nu am putut reveni la configurația serverului.',
    )
  }

  return (
    <Panel title="Keycloak (autentificare unică)">
      <p className="muted">
        Utilizatorii se conectează prin Keycloak, cu OpenID Connect. La fiecare conectare Taskel preia numele, emailul,
        departamentul, rolurile și, la cerere, grupurile.
      </p>
      {loadError && (
        <p className="error" role="alert">
          Nu am putut încărca setările Keycloak.
        </p>
      )}
      {!settings || !form ? (
        !loadError && <TableSkeleton rows={3} />
      ) : (
        <form onSubmit={save} noValidate>
          <StatusLine status={settings.status} readyText="Activ" offText="Inactiv: butonul Keycloak nu apare la conectare" />
          <div className="settings-grid">
            <Toggle label="Permite conectarea cu Keycloak" checked={form.enabled} onChange={(v) => update('enabled', v)} />
            <TextField
              wide
              label="Adresa realm-ului"
              type="url"
              value={form.authority}
              onChange={(v) => update('authority', v)}
              placeholder="https://sso.firma.ro/realms/firma"
              maxLength={2048}
              error={errors.authority}
            />
            <TextField
              label="Client ID"
              value={form.clientId}
              onChange={(v) => update('clientId', v)}
              placeholder="taskel"
              error={errors.clientId}
            />
            <SecretField
              label="Secretul clientului"
              status={settings.status}
              value={form.clientSecret}
              onChange={(v) => update('clientSecret', v)}
            />
            <TextField
              label="Textul butonului"
              value={form.displayName}
              onChange={(v) => update('displayName', v)}
              hint="Apare pe ecranul de conectare: „Conectează-te cu …”."
              maxLength={60}
              error={errors.displayName}
            />
            <TextField
              label="Rol obligatoriu (opțional)"
              value={form.requiredRole}
              onChange={(v) => update('requiredRole', v)}
              placeholder="taskel-user"
              hint="Doar cine are acest rol (de realm sau de client) se poate conecta."
              error={errors.requiredRole}
            />
            <TextField
              label="Rolul administratorilor (opțional)"
              value={form.adminRole}
              onChange={(v) => update('adminRole', v)}
              placeholder="taskel-admin"
              error={errors.adminRole}
            />
            <TextField
              label="Rolul managerilor (opțional)"
              value={form.managerRole}
              onChange={(v) => update('managerRole', v)}
              placeholder="taskel-manager"
              error={errors.managerRole}
            />
            <TextField
              label="Claim-ul departamentului"
              value={form.departmentClaim}
              onChange={(v) => update('departmentClaim', v)}
              placeholder="department"
              hint="Un mapper „User Attribute” în Keycloak; la import, atributul cu același nume."
              maxLength={100}
              error={errors.departmentClaim}
            />
            <Toggle
              label="Adu grupurile din Keycloak ca grupuri Taskel"
              hint="Necesită mapper-ul „Group Membership” pe client."
              checked={form.syncGroups}
              onChange={(v) => update('syncGroups', v)}
            />
            {form.syncGroups && (
              <TextField
                label="Claim-ul grupurilor"
                value={form.groupsClaim}
                onChange={(v) => update('groupsClaim', v)}
                placeholder="groups"
                maxLength={100}
                error={errors.groupsClaim}
              />
            )}
            <Toggle
              label="Cere HTTPS pentru Keycloak"
              hint="Dezactivează doar pentru un Keycloak de test servit prin http://."
              checked={form.requireHttpsMetadata}
              onChange={(v) => update('requireHttpsMetadata', v)}
            />
          </div>

          <div className="settings-note">
            <p>De configurat pe clientul din Keycloak (Client authentication pornit, Standard flow):</p>
            <dl>
              <dt>Valid redirect URIs</dt>
              <dd>
                <code>{settings.redirectUri}</code>
              </dd>
              <dt>Valid post logout redirect URIs</dt>
              <dd>
                <code>{settings.postLogoutRedirectUri}</code>
              </dd>
            </dl>
            <p className="muted">
              Pentru import, pornește Service accounts pe client și dă contului de serviciu rolul <code>view-users</code> al
              clientului <code>realm-management</code>.
            </p>
          </div>

          <div className="settings-actions">
            <Button type="submit" variant="primary" loading={busy === 'save'}>
              Salvează
            </Button>
            <Button onClick={test} loading={busy === 'test'}>
              Testează conexiunea
            </Button>
            {settings.status.ready && (
              <Button onClick={() => setConfirmImport(true)} loading={busy === 'import'} disabled={confirmImport}>
                Importă utilizatorii
              </Button>
            )}
            {settings.status.saved && (
              <Button variant="danger" onClick={reset} loading={busy === 'reset'}>
                Revino la configurația serverului
              </Button>
            )}
          </div>

          {confirmImport && (
            <div className="confirm-box" role="group" aria-label="Confirmă importul">
              <p>
                Se creează un cont Taskel pentru fiecare utilizator activ din Keycloak, ca să le poți da task-uri înainte să se
                conecteze. Un cont local cu aceeași adresă, verificată în Keycloak, trece pe Keycloak: își păstrează
                task-urile, dar parola locală nu mai funcționează.
              </p>
              <div className="settings-actions">
                <Button variant="primary" onClick={importUsers}>
                  Importă
                </Button>
                <Button onClick={() => setConfirmImport(false)}>Renunță</Button>
              </div>
            </div>
          )}
          <OutcomeNote outcome={outcome} />
        </form>
      )}
    </Panel>
  )
}
