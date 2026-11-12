/**
 * Checks applied in the forms before anything is sent. They mirror the API's rules, so the
 * user gets a specific message in Romanian instead of a generic failure. Each function returns
 * an error message, or null when the value is fine.
 */

export const LIMITS = {
  title: 200,
  notes: 2000,
  response: 4000,
  tags: 10,
  tagLength: 30,
  email: 254,
  passwordMin: 8,
  passwordMax: 128,
  username: 128,
  tokenName: 50,
  department: 100,
  dueDateMin: '2000-01-01',
  dueDateMax: '2100-12-31',
} as const

// Local part, then dot-separated host labels, ending in a label of two or more characters.
const EMAIL = /^[^\s<>@,;:"()[\]\\]+@(?:[^\s<>@,;:"()[\]\\.]+\.)+[^\s<>@,;:"()[\]\\.]{2,}$/
// user, DOMAIN\user or user@domain
const DIRECTORY_USERNAME = /^(?:[A-Za-z0-9][A-Za-z0-9-]{0,14}\\)?[A-Za-z0-9][A-Za-z0-9._-]{0,63}(?:@[A-Za-z0-9.-]+)?$/
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/
// Control characters other than tab and line breaks.
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/

export function validateEmail(value: string): string | null {
  const email = value.trim()
  if (!email) return 'Introdu adresa de email.'
  if (email.length > LIMITS.email || !EMAIL.test(email)) return 'Adresa de email nu este validă.'
  return null
}

export function validatePassword(value: string, isNew: boolean): string | null {
  if (!value) return 'Introdu parola.'
  if (value.length > LIMITS.passwordMax) return `Parola poate avea cel mult ${LIMITS.passwordMax} de caractere.`
  if (isNew && value.trim().length === 0) return 'Parola nu poate fi formată doar din spații.'
  if (isNew && value.length < LIMITS.passwordMin) return `Parola trebuie să aibă cel puțin ${LIMITS.passwordMin} caractere.`
  return null
}

export function validateDirectoryUsername(value: string): string | null {
  const username = value.trim()
  if (!username) return 'Introdu numele de utilizator.'
  if (username.length > LIMITS.username || !DIRECTORY_USERNAME.test(username)) {
    return 'Numele de utilizator nu este valid. Exemple: ion.popescu, FIRMA\\ion.popescu sau ion.popescu@firma.ro.'
  }
  return null
}

export function validateTitle(value: string): string | null {
  const title = value.trim()
  if (!title) return 'Scrie un titlu pentru task.'
  if (title.length > LIMITS.title) return `Titlul poate avea cel mult ${LIMITS.title} de caractere.`
  if (CONTROL.test(title) || /[\n\r\t]/.test(title)) return 'Titlul conține caractere nepermise.'
  return null
}

export function validateNotes(value: string, max: number = LIMITS.notes): string | null {
  if (value.length > max) return `Textul poate avea cel mult ${max} de caractere.`
  if (CONTROL.test(value)) return 'Textul conține caractere nepermise.'
  return null
}

export function validateDueDate(value: string): string | null {
  if (!value) return null
  if (!ISO_DATE.test(value) || Number.isNaN(Date.parse(value))) return 'Termenul nu este o dată validă.'
  if (value < LIMITS.dueDateMin || value > LIMITS.dueDateMax) return 'Termenul trebuie să fie între anii 2000 și 2100.'
  return null
}

/** A start date needs a due date on or after it. */
export function validateInterval(startDate: string, dueDate: string): string | null {
  if (!startDate) return null
  const invalidStart = validateDueDate(startDate)
  if (invalidStart) return invalidStart.replace('Termenul', 'Începutul')
  if (!dueDate) return 'Un task cu dată de început are nevoie și de un termen.'
  if (startDate > dueDate) return 'Data de început nu poate fi după termen.'
  return null
}

export function validateTags(tags: string[]): string | null {
  if (tags.length > LIMITS.tags) return `Un task poate avea cel mult ${LIMITS.tags} etichete.`
  if (tags.some((tag) => tag.length > LIMITS.tagLength)) {
    return `O etichetă poate avea cel mult ${LIMITS.tagLength} de caractere.`
  }
  if (tags.some((tag) => CONTROL.test(tag))) return 'Etichetele conțin caractere nepermise.'
  return null
}

export function validateTokenName(value: string): string | null {
  const name = value.trim()
  if (!name) return 'Dă un nume token-ului.'
  if (name.length > LIMITS.tokenName) return `Numele poate avea cel mult ${LIMITS.tokenName} de caractere.`
  if (CONTROL.test(name)) return 'Numele conține caractere nepermise.'
  return null
}

export function validateDepartment(value: string): string | null {
  const department = value.trim()
  if (department.length > LIMITS.department) return `Departamentul poate avea cel mult ${LIMITS.department} de caractere.`
  if (CONTROL.test(department) || /[\n\r\t]/.test(department)) return 'Departamentul conține caractere nepermise.'
  return null
}

/** The first error among several checks, or null. */
export const firstError = (...errors: (string | null)[]): string | null => errors.find((e) => e !== null) ?? null
