/** A look the user picked. `system` follows the device: light or dark. */
export type Theme = 'system' | 'light' | 'dark' | 'frost'

export const THEMES: { id: Theme; label: string }[] = [
  { id: 'system', label: 'Automată' },
  { id: 'light', label: 'Luminoasă' },
  { id: 'dark', label: 'Întunecată' },
  { id: 'frost', label: 'Arctic Frost' },
]

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
  if (theme === 'system') delete document.documentElement.dataset.theme
  else document.documentElement.dataset.theme = theme
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
