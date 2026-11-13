/** Locale used for dates and numbers shown in the interface. */
export const LOCALE = 'ro-RO'

/**
 * A count with its noun, following Romanian grammar: "1 task", "5 task-uri", "20 de task-uri".
 * Numbers ending in 00 or 20–99 take "de" before the noun.
 */
export function countLabel(n: number, one: string, many: string): string {
  if (n === 1) return `1 ${one}`
  const lastTwo = n % 100
  const needsDe = n !== 0 && (lastTwo === 0 || lastTwo >= 20)
  return `${n} ${needsDe ? 'de ' : ''}${many}`
}

export const tasksLabel = (n: number) => countLabel(n, 'task', 'task-uri')

/** A file size as people read it: "640 B", "12,4 KB", "3,1 MB", "1,2 GB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toLocaleString(LOCALE, { maximumFractionDigits: value < 10 ? 1 : 0 })} ${units[unit]}`
}
