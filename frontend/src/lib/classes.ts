/** Joins the class names that apply: `classes('day', isToday && 'today')`. */
export function classes(...names: (string | false | null | undefined)[]): string {
  return names.filter(Boolean).join(' ')
}
