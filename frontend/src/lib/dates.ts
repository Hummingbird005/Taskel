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

/** When a message was written, as a mail app shows it: "18:20" today, "13 nov., 18:20" this year, else with the year. */
export function formatMailTime(iso: string, now: Date = new Date()): string {
  const at = new Date(iso)
  const time = at.toLocaleTimeString(LOCALE, { hour: '2-digit', minute: '2-digit' })
  if (at.toDateString() === now.toDateString()) return time
  const sameYear = at.getFullYear() === now.getFullYear()
  const day = at.toLocaleDateString(LOCALE, sameYear ? { day: 'numeric', month: 'short' } : { day: 'numeric', month: 'short', year: 'numeric' })
  return `${day}, ${time}`
}

/** Date and time of a moment, e.g. "13 nov. 2026, 18:20". */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(LOCALE, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/** "miercuri, 11 noiembrie". */
export function formatLongDate(iso: string): string {
  return fromIso(iso).toLocaleDateString(LOCALE, { weekday: 'long', day: 'numeric', month: 'long' })
}

/** Short weekday name, e.g. "mie.". */
export function formatWeekday(iso: string): string {
  return fromIso(iso).toLocaleDateString(LOCALE, { weekday: 'short' })
}

/** Number of days from one date to another (0 for the same day). */
export function daysBetween(start: string, end: string): number {
  return Math.round((fromIso(end).getTime() - fromIso(start).getTime()) / 86_400_000)
}

/** The Monday of the week a date falls in. */
export function mondayOf(iso: string): string {
  const weekday = fromIso(iso).getDay() // 0 = Sunday
  return addDaysIso(iso, weekday === 0 ? -6 : 1 - weekday)
}

/** Every date from `from`, for `count` days. */
export function daysFrom(from: string, count: number): string[] {
  return Array.from({ length: count }, (_, i) => addDaysIso(from, i))
}

/** True for Saturday and Sunday. */
export function isWeekend(iso: string): boolean {
  const weekday = fromIso(iso).getDay()
  return weekday === 0 || weekday === 6
}

export function dayOfMonth(iso: string): number {
  return fromIso(iso).getDate()
}

/** "12–15 nov." within a month, "28 nov. – 3 dec." across months; a single date when both are the same. */
export function formatRange(startIso: string, endIso: string): string {
  if (startIso === endIso) return formatDueDate(endIso)
  const start = fromIso(startIso)
  const end = fromIso(endIso)
  if (start.getFullYear() === end.getFullYear() && start.getMonth() === end.getMonth()) {
    return `${start.getDate()}–${formatDueDate(endIso)}`
  }
  return `${formatDueDate(startIso)} – ${formatDueDate(endIso)}`
}

/** "data de 14 nov." or "perioada 12–15 nov.", for sentences about when a task takes place. */
export function describePeriod(startIso: string | null, endIso: string): string {
  return startIso && startIso !== endIso
    ? `perioada ${formatRange(startIso, endIso)}`
    : `data de ${formatDueDate(endIso)}`
}
