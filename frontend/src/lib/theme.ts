/** A look the user picked. `system` follows the device: light or dark. */
export type Theme = 'system' | 'light' | 'dark' | 'frost' | 'amber'

export const THEMES: { id: Theme; label: string }[] = [
  { id: 'system', label: 'Automată' },
  { id: 'light', label: 'Luminoasă' },
  { id: 'dark', label: 'Întunecată' },
  { id: 'frost', label: 'Arctic Frost' },
  { id: 'amber', label: 'Amber Glass' },
]

/** Themes whose surfaces are translucent glass over blurred shapes. Keep in step with index.html. */
const GLASS_THEMES: Theme[] = ['frost', 'amber']

const KEY = 'taskel.theme'
const isTheme = (value: unknown): value is Theme => THEMES.some((theme) => theme.id === value)

/** The saved choice. It is kept in this browser, so it also applies to the sign-in screen. */
export function loadTheme(): Theme {
  try {
    const saved = localStorage.getItem(KEY)
    return isTheme(saved) ? saved : 'system'
  } catch {
    // Storage can be blocked; the app then simply follows the system.
    return 'system'
  }
}

/** Applies a theme to the page. `index.html` does the same before the first paint, to avoid a flash. */
export function applyTheme(theme: Theme) {
  const root = document.documentElement
  if (theme === 'system') delete root.dataset.theme
  else root.dataset.theme = theme
  // The glass themes share their structure, which the stylesheet keys on this attribute.
  if (GLASS_THEMES.includes(theme)) root.dataset.glass = ''
  else delete root.dataset.glass
}

export function saveTheme(theme: Theme) {
  applyTheme(theme)
  try {
    if (theme === 'system') localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, theme)
  } catch {
    // Not saved: the choice still holds until the page is closed.
  }
}
