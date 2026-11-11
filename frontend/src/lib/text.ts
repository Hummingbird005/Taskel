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
