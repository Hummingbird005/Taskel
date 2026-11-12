import { useState } from 'react'
import { loadTheme, saveTheme, THEMES, type Theme } from '../lib/theme'
import Icon from './Icon'

/** Chooses how Taskel looks in this browser. Applied at once. */
export default function ThemePicker() {
  const [theme, setTheme] = useState<Theme>(loadTheme)

  return (
    <label className="theme-picker" title="Temă">
      <Icon name="sun" />
      <span className="visually-hidden">Temă</span>
      <select
        value={theme}
        aria-label="Temă"
        onChange={(e) => {
          const next = e.target.value as Theme
          setTheme(next)
          saveTheme(next)
        }}
      >
        {THEMES.map((option) => (
          <option key={option.id} value={option.id}>
            Temă: {option.label}
          </option>
        ))}
      </select>
    </label>
  )
}
