import { useCallback, useState, type FormEvent } from 'react'
import {
  integrationsApi,
  type DirectorySecurity,
  type DirectorySettings,
  type DirectorySettingsForm,
  type DirectoryTestResult,
} from '../../api/integrations'
import { collect, orNull, validateHost, validateIdentifier, validatePlain, validatePort } from '../../lib/settingsValidation'
import { validateDirectoryUsername } from '../../lib/validation'
import Button from '../ui/Button'
import Panel from '../ui/Panel'
import { TableSkeleton } from '../ui/Skeleton'
import { OutcomeNote, SelectField, StatusLine, TextField, Toggle } from './SettingsFields'
import { useSettingsForm, type Outcome } from './useSettingsForm'

const toForm = ({ status: _status, ...rest }: DirectorySettings): DirectorySettingsForm => rest

const SECURITY: { value: DirectorySecurity; label: string; port: number }[] = [
  { value: 'ldaps', label: 'LDAPS (TLS, port 636)', port: 636 },
  { value: 'startTls', label: 'StartTLS (port 389)', port: 389 },
  { value: 'none', label: 'Necriptat (doar pentru teste)', port: 389 },
]

function validate(form: DirectorySettingsForm, forTest: boolean) {
  const required = form.enabled || forTest
  return collect<DirectorySettingsForm>({
    host: validateHost(form.host, required),
    port: validatePort(form.port),
    domain: validateHost(form.domain, required),
    baseDn: validatePlain(form.baseDn, required),
    requiredGroupDn: validatePlain(form.requiredGroupDn, false),
    departmentAttribute: validateIdentifier(form.departmentAttribute, false),
    managerGroupDn: validatePlain(form.managerGroupDn, false),
    adminGroupDn: validatePlain(form.adminGroupDn, false),
    groupsBaseDn: validatePlain(form.groupsBaseDn, false),
    timeoutSeconds:
      Number.isInteger(form.timeoutSeconds) && form.timeoutSeconds >= 1 && form.timeoutSeconds <= 60
        ? null
        : 'Între 1 și 60 de secunde.',
  })
}

function payload(form: DirectorySettingsForm): DirectorySettingsForm {
  return {
    ...form,
    host: orNull(form.host),
    domain: orNull(form.domain),
    baseDn: orNull(form.baseDn),
    requiredGroupDn: orNull(form.requiredGroupDn),
    departmentAttribute: orNull(form.departmentAttribute),
    managerGroupDn: orNull(form.managerGroupDn),
    adminGroupDn: orNull(form.adminGroupDn),
    groupsBaseDn: orNull(form.groupsBaseDn),
  }
}

/** What a test sign-in found, in words. */
function testOutcome(result: DirectoryTestResult): Outcome {
  if (!result.signedIn) {
    return {
      tone: 'error',
      text:
        'Serverul a răspuns, dar conectarea a fost refuzată: utilizatorul sau parola sunt greșite, contul nu este în grupul ' +
        'cerut, ori nu are adresă de email. Detaliile sunt în jurnalul serverului.',
    }
  }
  const role = result.isAdmin ? 'administrator' : result.isManager ? 'manager' : null
  return {
    tone: 'ok',
    text: (
      <>
        Conectare reușită: <strong>{result.displayName ?? result.email}</strong> ({result.email})
        {result.department && <>, departamentul {result.department}</>}
        {role && <>, rol {role}</>}.
        {result.groups.length > 0 && <> Grupuri: {result.groups.join(', ')}.</>}
      </>
    ),
  }
}

/** Sign-in with Active Directory accounts over LDAP. */
export default function DirectorySettingsPanel({ onSessionExpired }: { onSessionExpired: () => void }) {
  const load = useCallback(() => integrationsApi.directory(), [])
  const { settings, form, loadError, busy, outcome, errors, setErrors, update, run, accept } = useSettingsForm(
    load,
    toForm,
    onSessionExpired,
  )
  const [testUser, setTestUser] = useState('')
  const [testPassword, setTestPassword] = useState('')
  const [testError, setTestError] = useState<string | null>(null)

  function checked(forTest: boolean): DirectorySettingsForm | null {
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
        accept(await integrationsApi.saveDirectory(valid))
        return { tone: 'ok', text: 'Setările Active Directory au fost salvate și se aplică la următoarea conectare.' }
      },
      'Setările nu au putut fi salvate.',
    )
  }

  function test() {
    const valid = checked(true)
    const invalidUser = validateDirectoryUsername(testUser) ?? (testPassword ? null : 'Introdu parola contului de test.')
    setTestError(invalidUser)
    if (!valid || invalidUser) return
    void run(
      'test',
      async () => {
        const result = await integrationsApi.testDirectory(valid, testUser.trim(), testPassword)
        setTestPassword('')
        return testOutcome(result)
      },
      'Serverul de domeniu nu a putut fi contactat.',
    )
  }

  function reset() {
    if (!window.confirm('Renunți la setările salvate aici și revii la cele din configurația serverului?')) return
    void run(
      'reset',
      async () => {
        accept(await integrationsApi.resetDirectory())
        return { tone: 'ok', text: 'Se folosesc din nou setările din configurația serverului.' }
      },
      'Nu am putut reveni la configurația serverului.',
    )
  }

  function changeSecurity(security: DirectorySecurity) {
    if (!form) return
    const before = SECURITY.find((s) => s.value === form.security)
    update('security', security)
    // Follow the usual port, unless a custom one was set.
    if (before && form.port === before.port) update('port', SECURITY.find((s) => s.value === security)!.port)
  }

  return (
    <Panel title="Active Directory (LDAP)">
      <p className="muted">
        Utilizatorii se conectează cu contul de Windows. Taskel verifică parola direct la controlerul de domeniu, deci nu
        are nevoie de un cont de serviciu, și citește la fiecare conectare emailul, numele, departamentul și grupurile.
      </p>
      {loadError && (
        <p className="error" role="alert">
          Nu am putut încărca setările Active Directory.
        </p>
      )}
      {!settings || !form ? (
        !loadError && <TableSkeleton rows={3} />
      ) : (
        <form onSubmit={save} noValidate>
          <StatusLine status={settings.status} readyText="Activ" offText="Inactiv: conectarea cu cont de domeniu nu este oferită" />
          <div className="settings-grid">
            <Toggle
              label="Permite conectarea cu cont de domeniu"
              checked={form.enabled}
              onChange={(v) => update('enabled', v)}
            />
            <TextField
              label="Controler de domeniu"
              value={form.host}
              onChange={(v) => update('host', v)}
              placeholder="dc1.firma.local"
              hint="Un controler de domeniu sau chiar numele domeniului."
              error={errors.host}
            />
            <TextField
              label="Domeniu"
              value={form.domain}
              onChange={(v) => update('domain', v)}
              placeholder="firma.local"
              hint="Completează numele scurt: ion.popescu devine ion.popescu@firma.local."
              error={errors.domain}
            />
            <SelectField label="Securitate" value={form.security} options={SECURITY} onChange={changeSecurity} />
            <TextField
              label="Port"
              type="number"
              value={String(form.port)}
              onChange={(v) => update('port', Number(v))}
              error={errors.port}
            />
            <TextField
              wide
              label="Unde se caută utilizatorii (Base DN)"
              value={form.baseDn}
              onChange={(v) => update('baseDn', v)}
              placeholder="DC=firma,DC=local"
              maxLength={1024}
              error={errors.baseDn}
            />
            <TextField
              wide
              label="Grup obligatoriu (opțional)"
              value={form.requiredGroupDn}
              onChange={(v) => update('requiredGroupDn', v)}
              placeholder="CN=Taskel,OU=Grupuri,DC=firma,DC=local"
              hint="Dacă e completat, doar membrii direcți ai grupului se pot conecta."
              maxLength={1024}
              error={errors.requiredGroupDn}
            />
            <TextField
              wide
              label="Grupul administratorilor (opțional)"
              value={form.adminGroupDn}
              onChange={(v) => update('adminGroupDn', v)}
              placeholder="CN=Taskel Admins,OU=Grupuri,DC=firma,DC=local"
              hint="Membrii sunt administratori în Taskel, iar ceilalți nu (rolul se actualizează la fiecare conectare)."
              maxLength={1024}
              error={errors.adminGroupDn}
            />
            <TextField
              wide
              label="Grupul managerilor (opțional)"
              value={form.managerGroupDn}
              onChange={(v) => update('managerGroupDn', v)}
              placeholder="CN=Taskel Manageri,OU=Grupuri,DC=firma,DC=local"
              maxLength={1024}
              error={errors.managerGroupDn}
            />
            <TextField
              label="Atributul departamentului"
              value={form.departmentAttribute}
              onChange={(v) => update('departmentAttribute', v)}
              placeholder="department"
              maxLength={64}
              error={errors.departmentAttribute}
            />
            <TextField
              label="Timp de așteptare (secunde)"
              type="number"
              value={String(form.timeoutSeconds)}
              onChange={(v) => update('timeoutSeconds', Number(v))}
              error={errors.timeoutSeconds}
            />
            <Toggle
              label="Adu grupurile din domeniu ca grupuri Taskel"
              hint="Task-urile pot fi date unui grup; membrii apar pe măsură ce se conectează."
              checked={form.syncGroups}
              onChange={(v) => update('syncGroups', v)}
            />
            {form.syncGroups && (
              <TextField
                wide
                label="Doar grupurile din (opțional)"
                value={form.groupsBaseDn}
                onChange={(v) => update('groupsBaseDn', v)}
                placeholder="OU=Echipe,DC=firma,DC=local"
                maxLength={1024}
                error={errors.groupsBaseDn}
              />
            )}
          </div>

          <fieldset className="settings-test">
            <legend>Testează cu un cont de domeniu</legend>
            <p className="muted">Folosește valorile din formular, fără să le salveze, și nu creează niciun cont în Taskel.</p>
            <div className="settings-grid">
              <TextField label="Utilizator de test" value={testUser} onChange={setTestUser} placeholder="ion.popescu" maxLength={128} />
              <TextField label="Parola contului de test" type="password" value={testPassword} onChange={setTestPassword} maxLength={256} />
            </div>
            {testError && (
              <p className="error" role="alert">
                {testError}
              </p>
            )}
          </fieldset>

          <div className="settings-actions">
            <Button type="submit" variant="primary" loading={busy === 'save'}>
              Salvează
            </Button>
            <Button onClick={test} loading={busy === 'test'}>
              Testează conectarea
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
