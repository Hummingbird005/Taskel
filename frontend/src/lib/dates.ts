import { LOCALE } from './text'

const pad = (n: number) => String(n).padStart(2, '0')

function toIso(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** Parses YYYY-MM-DD as a local date, so it never shifts across time zones. */
function fromIso(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number)
  return new Date(year, month - 1, day)
}

/** Today's date as YYYY-MM-DD in the user's own time zone. */
export function todayIso(): string {
  return toIso(new Date())
}

export function addDaysIso(iso: string, days: number): string {
  const date = fromIso(iso)
  date.setDate(date.getDate() + days)
  return toIso(date)
}

/** "15 mar.", with the year added when it is not the current one. */
export function formatDueDate(iso: string): string {
  const date = fromIso(iso)
  return date.toLocaleDateString(LOCALE, {
    day: 'numeric',
    month: 'short',
    year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric',
  })
}

export function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleDateString(LOCALE, { day: 'numeric', month: 'short', year: 'numeric' })
}

/** "miercuri, 11 noiembrie". */
export function formatLongDate(iso: string): string {
  return fromIso(iso).toLocaleDateString(LOCALE, { weekday: 'long', day: 'numeric', month: 'long' })
}

/** Short weekday name, e.g. "mie.". */
export function formatWeekday(iso: string): string {
  return fromIso(iso).toLocaleDateString(LOCALE, { weekday: 'short' })
}
