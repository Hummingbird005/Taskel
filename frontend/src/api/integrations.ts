import { request } from './client'

/** Where an integration's settings come from, and whether they are complete enough to use. */
export interface IntegrationStatus {
  /** True when saved in the interface; false when the server configuration applies. */
  saved: boolean
  updatedAt: string | null
  updatedBy: string | null
  /** A password or client secret was saved here. It is never sent back. */
  secretSaved: boolean
  /** The server configuration has one, used when none is saved here. */
  secretInConfiguration: boolean
  /** The saved secret can no longer be decrypted and must be entered again. */
  secretUnreadable: boolean
  /** The settings are complete: the integration is in use. */
  ready: boolean
}

export interface EmailSettings {
  host: string | null
  port: number
  username: string | null
  fromAddress: string | null
  fromName: string | null
  requireTls: boolean
  notificationsEnabled: boolean
  publicUrl: string | null
  status: IntegrationStatus
}

/**
 * What the forms send. A secret field is null to keep the secret in effect, '' to drop the one
 * saved here (back to the server configuration's), or the new value.
 */
export type EmailSettingsForm = Omit<EmailSettings, 'status'> & { password: string | null }

export type DirectorySecurity = 'ldaps' | 'startTls' | 'none'

export interface DirectorySettings {
  enabled: boolean
  host: string | null
  port: number
  security: DirectorySecurity
  domain: string | null
  baseDn: string | null
  requiredGroupDn: string | null
  departmentAttribute: string | null
  managerGroupDn: string | null
  adminGroupDn: string | null
  syncGroups: boolean
  groupsBaseDn: string | null
  timeoutSeconds: number
  status: IntegrationStatus
}

export type DirectorySettingsForm = Omit<DirectorySettings, 'status'>

export interface DirectoryTestResult {
  signedIn: boolean
  email: string | null
  displayName: string | null
  department: string | null
  isManager: boolean | null
  isAdmin: boolean | null
  groups: string[]
}

export interface KeycloakSettings {
  enabled: boolean
  authority: string | null
  clientId: string | null
  displayName: string
  requireHttpsMetadata: boolean
  requiredRole: string | null
  adminRole: string | null
  managerRole: string | null
  departmentClaim: string | null
  syncGroups: boolean
  groupsClaim: string
  /** To add to the client's valid redirect URIs in Keycloak. */
  redirectUri: string
  /** To add to the client's valid post logout redirect URIs. */
  postLogoutRedirectUri: string
  status: IntegrationStatus
}

export type KeycloakSettingsForm = Omit<KeycloakSettings, 'status' | 'redirectUri' | 'postLogoutRedirectUri'> & {
  clientSecret: string | null
}

export interface KeycloakCheck {
  issuer: string
  hasEndSession: boolean
  /** Whether the client secret works for the service account; null when there is no secret. */
  serviceAccount: boolean | null
  /** Whether the service account may read the users; null when not tried. */
  canListUsers: boolean | null
  problem: string | null
}

export type KeycloakRefusal = 'noEmail' | 'emailTaken' | 'emailUnverified' | 'roleMissing'

export interface KeycloakImportResult {
  total: number
  created: number
  updated: number
  skippedCount: number
  skipped: { who: string; reason: KeycloakRefusal }[]
}

const base = '/admin/integrations'
const json = (method: string, body?: object): RequestInit => ({
  method,
  body: body === undefined ? undefined : JSON.stringify(body),
})

export const integrationsApi = {
  email: () => request<EmailSettings>(`${base}/email`),
  saveEmail: (form: EmailSettingsForm) => request<EmailSettings>(`${base}/email`, json('PUT', form)),
  resetEmail: () => request<EmailSettings>(`${base}/email`, json('DELETE')),
  testEmail: (form: EmailSettingsForm) =>
    request<{ sentTo: string }>(`${base}/email/test`, json('POST', { settings: form })),

  directory: () => request<DirectorySettings>(`${base}/directory`),
  saveDirectory: (form: DirectorySettingsForm) => request<DirectorySettings>(`${base}/directory`, json('PUT', form)),
  resetDirectory: () => request<DirectorySettings>(`${base}/directory`, json('DELETE')),
  testDirectory: (form: DirectorySettingsForm, username: string, password: string) =>
    request<DirectoryTestResult>(`${base}/directory/test`, json('POST', { settings: form, username, password })),

  keycloak: () => request<KeycloakSettings>(`${base}/keycloak`),
  saveKeycloak: (form: KeycloakSettingsForm) => request<KeycloakSettings>(`${base}/keycloak`, json('PUT', form)),
  resetKeycloak: () => request<KeycloakSettings>(`${base}/keycloak`, json('DELETE')),
  testKeycloak: (form: KeycloakSettingsForm) => request<KeycloakCheck>(`${base}/keycloak/test`, json('POST', { settings: form })),
  importKeycloakUsers: () => request<KeycloakImportResult>(`${base}/keycloak/import`, json('POST')),
}
