const AVATAR_COLOURS = ['#7c3aed', '#0e7490', '#c2410c', '#1d4ed8', '#047857', '#be185d', '#4d7c0f', '#9333ea']

/** Someone shown as an avatar: their name, and an id or address that keeps their colour the same everywhere. */
export interface AvatarPerson {
  id?: string
  name: string
  email?: string
}

/** Two letters and a colour that stays the same for the same person. */
export function avatar(person: AvatarPerson): { letters: string; colour: string } {
  const words = person.name.replace(/[<>@].*$/, '').trim().split(/\s+/).filter(Boolean)
  const letters = ((words[0]?.[0] ?? '?') + (words.length > 1 ? words[words.length - 1][0] : (words[0]?.[1] ?? ''))).toUpperCase()
  let hash = 0
  for (const ch of person.id ?? person.email ?? person.name) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0
  return { letters, colour: AVATAR_COLOURS[hash % AVATAR_COLOURS.length] }
}
