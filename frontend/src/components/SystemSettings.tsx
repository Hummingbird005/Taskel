import AttachmentSettingsPanel from './AttachmentSettingsPanel'
import DirectorySettingsPanel from './settings/DirectorySettingsPanel'
import EmailSettingsPanel from './settings/EmailSettingsPanel'
import KeycloakSettingsPanel from './settings/KeycloakSettingsPanel'

interface Props {
  onSessionExpired: () => void
  /** Called after the attachment size limit was changed. */
  onAttachmentLimitChanged: () => void
}

/**
 * The administrator's server settings: the mail server, Active Directory, Keycloak and the
 * attachment limit. What is saved here replaces the server configuration and applies at once.
 */
export default function SystemSettings({ onSessionExpired, onAttachmentLimitChanged }: Props) {
  return (
    <div className="system-settings">
      <p className="muted settings-intro">
        Valorile de pornire vin din configurația serverului (appsettings sau variabile de mediu). Ce salvezi aici le
        înlocuiește pentru toți utilizatorii. Parolele și secretele se păstrează criptat și nu se mai afișează.
      </p>
      <EmailSettingsPanel onSessionExpired={onSessionExpired} />
      <DirectorySettingsPanel onSessionExpired={onSessionExpired} />
      <KeycloakSettingsPanel onSessionExpired={onSessionExpired} />
      <AttachmentSettingsPanel onSessionExpired={onSessionExpired} onChanged={onAttachmentLimitChanged} />
    </div>
  )
}
