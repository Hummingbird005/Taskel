/**
 * Checks for the administrator's integration settings, mirroring the API's rules so a mistake is
 * explained in Romanian before anything is sent. Each returns a message, or null when the value is fine.
 */
import { CONTROL, validateEmail } from './validation'

const blank = (value: string | null) => !value || value.trim() === ''

/** A server name or IPv4 address, or an IPv6 address in brackets; no scheme, no port. */
export function validateHost(value: string | null, required: boolean): string | null {
  if (blank(value)) return required ? 'Completează numele serverului.' : null
  const host = value!.trim()
  const bracketed = host.startsWith('[') && host.endsWith(']')
  const valid = bracketed ? /^\[[0-9A-Fa-f:.]+\]$/.test(host) : /^[A-Za-z0-9.-]+$/.test(host)
  if (host.length > 253 || !valid) return 'Doar numele serverului, fără http:// și fără port (portul are câmpul lui).'
  return null
}

export function validatePort(value: number): string | null {
  return Number.isInteger(value) && value >= 1 && value <= 65535 ? null : 'Portul este un număr între 1 și 65535.'
}

/** An absolute http(s) address without query, fragment or credentials. */
export function validateHttpUrl(value: string | null, required: boolean, httpsOnly = false): string | null {
  if (blank(value)) return required ? 'Completează adresa.' : null
  let url: URL
  try {
    url = new URL(value!.trim())
  } catch {
    return 'Adresa trebuie să înceapă cu https://.'
  }
  if (url.protocol !== 'https:' && (httpsOnly || url.protocol !== 'http:')) return 'Adresa trebuie să înceapă cu https://.'
  if (url.search || url.hash || url.username || url.password) return 'Adresa nu poate avea parametri (?…), # sau date de autentificare.'
  return null
}

/** A protocol name (LDAP attribute, claim, client id): printable characters, no spaces or filter characters. */
export function validateIdentifier(value: string | null, required: boolean): string | null {
  if (blank(value)) return required ? 'Completează câmpul.' : null
  return /^[!-~]+$/.test(value!.trim()) && !/[()*\\]/.test(value!)
    ? null
    : 'Doar litere, cifre și semne simple, fără spații și fără ( ) * \\.'
}

/** Free text such as a distinguished name or a role name: no control characters. */
export function validatePlain(value: string | null, required: boolean, max = 1024): string | null {
  if (blank(value)) return required ? 'Completează câmpul.' : null
  if (value!.length > max) return `Cel mult ${max} de caractere.`
  return CONTROL.test(value!) ? 'Conține caractere nepermise.' : null
}

export function validateOptionalEmail(value: string | null): string | null {
  return blank(value) ? null : validateEmail(value!)
}

/** A Keycloak realm address: …/realms/<name>, https unless explicitly allowed. */
export function validateRealmAddress(value: string | null, required: boolean, httpsOnly: boolean): string | null {
  const invalid = validateHttpUrl(value, required, httpsOnly)
  if (invalid || blank(value)) return invalid
  return /\/realms\/[^/]+\/?$/.test(new URL(value!.trim()).pathname)
    ? null
    : 'Adresa realm-ului se termină cu /realms/<nume realm>, de exemplu https://sso.firma.ro/realms/firma.'
}

/** Drops empty values, so the API receives null rather than an empty string. */
export function orNull(value: string | null): string | null {
  return blank(value) ? null : value!.trim()
}

/** Keeps only the messages that are set; empty means the form is valid. */
export function collect<F>(checks: Partial<Record<keyof F, string | null>>): Partial<Record<keyof F, string>> {
  const errors: Partial<Record<keyof F, string>> = {}
  for (const key of Object.keys(checks) as (keyof F)[]) {
    const message = checks[key]
    if (message) errors[key] = message
  }
  return errors
}
