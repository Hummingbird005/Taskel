import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import type { GroupKind, GroupListItem } from '../api/groups'

/** Added after "grup" in the search results, to tell where the group comes from. */
const GROUP_SUFFIX: Record<GroupKind, string> = { app: '', directory: ' AD', keycloak: ' Keycloak' }
import type { UserSummary } from '../api/users'
import { GROUP_PREFIX } from '../lib/taskViews'
import { countLabel } from '../lib/text'
import Icon, { type IconName } from './Icon'

interface Props {
  people: UserSummary[]
  groups: GroupListItem[]
  /** The choice: '' for nobody, a user id, or a group id with the group prefix. */
  value: string
  onChange: (value: string) => void
  /** What the field is called, for assistive technology. */
  label: string
  /** Shown when nothing is chosen and the field is not being typed in. */
  placeholder: string
  /**
   * What "nobody chosen" means, offered as the first option, e.g. "Eu" when a task stays with its
   * creator. Left out when a choice is required.
   */
  emptyLabel?: string
  autoFocus?: boolean
}

interface Option {
  value: string
  label: string
  detail: string
  kind: 'self' | 'person' | 'group'
}

const MAX_RESULTS = 8

/** Lower-cased and without diacritics, so "stefan" finds "Ștefan". */
const fold = (text: string) =>
  text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()

/**
 * Chooses who a task goes to by searching: type part of a name, an email or a group, then pick
 * from the matches. Nothing is listed until something is typed, so it works the same with ten
 * people or with a thousand.
 */
/** The field's icon: the chosen group, the search while typing, or a person. */
function pickerIcon(kind: Option['kind'] | undefined, open: boolean): IconName {
  if (kind === 'group') return 'users'
  return open ? 'search' : 'user'
}

export default function TargetPicker({ people, groups, value, onChange, label, placeholder, emptyLabel, autoFocus }: Props) {
  const listId = useId()
  const input = useRef<HTMLInputElement>(null)
  const menu = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)

  const selected = useMemo<Option | null>(() => {
    if (!value) return null
    if (value.startsWith(GROUP_PREFIX)) {
      const group = groups.find((g) => GROUP_PREFIX + g.id === value)
      return group ? { value, label: group.name, detail: 'grup', kind: 'group' } : null
    }
    const person = people.find((p) => p.id === value)
    return person ? { value, label: person.displayName, detail: person.email, kind: 'person' } : null
  }, [value, people, groups])

  const needle = fold(query.trim())
  const options = useMemo<Option[]>(() => {
    const self: Option[] = emptyLabel ? [{ value: '', label: emptyLabel, detail: '', kind: 'self' }] : []
    if (!needle) return self
    const matches: Option[] = [
      ...people
        .filter((p) => fold(p.displayName).includes(needle) || fold(p.email).includes(needle))
        .map((p): Option => ({ value: p.id, label: p.displayName, detail: p.displayName === p.email ? '' : p.email, kind: 'person' })),
      ...groups
        .filter((g) => fold(g.name).includes(needle))
        .map((g): Option => ({
          value: GROUP_PREFIX + g.id,
          label: g.name,
          detail: `grup${GROUP_SUFFIX[g.kind]} · ${countLabel(g.memberCount, 'membru', 'membri')}`,
          kind: 'group',
        })),
    ]
    // Names that start with what was typed come first.
    matches.sort((a, b) => Number(fold(b.label).startsWith(needle)) - Number(fold(a.label).startsWith(needle)))
    const selfMatches = self.filter((o) => fold(o.label).includes(needle))
    return [...selfMatches, ...matches].slice(0, MAX_RESULTS)
  }, [needle, people, groups, emptyLabel])

  // The suggestions can open below the visible part of a dialog or of the list: bring them into view.
  useEffect(() => {
    if (open) menu.current?.scrollIntoView({ block: 'nearest' })
  }, [open, options.length])

  function choose(option: Option) {
    onChange(option.value)
    setOpen(false)
    setQuery('')
  }

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      if (!open) setOpen(true)
      else if (options.length > 0) setActive((i) => (i + (e.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length)
    } else if (e.key === 'Enter' && open) {
      // Enter picks a match; it must not send the form the field is in.
      e.preventDefault()
      if (options[active]) choose(options[active])
    } else if (e.key === 'Escape' && open) {
      // Closes the suggestions only, not the dialog or form around them.
      e.preventDefault()
      e.stopPropagation()
      setOpen(false)
      setQuery('')
    }
  }

  const shown = open ? query : (selected?.label ?? '')
  const empty = open && needle.length > 0 && options.length === 0

  return (
    <div className={open ? 'target-picker open' : 'target-picker'}>
      <Icon name={pickerIcon(selected?.kind, open)} />
      <input
        ref={input}
        role="combobox"
        aria-label={label}
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && options[active] ? `${listId}-${active}` : undefined}
        autoComplete="off"
        spellCheck={false}
        autoFocus={autoFocus}
        value={shown}
        placeholder={open ? 'Caută după nume, email sau grup' : (emptyLabel ?? placeholder)}
        onFocus={() => {
          setOpen(true)
          setActive(0)
        }}
        onChange={(e) => {
          setQuery(e.target.value)
          setOpen(true)
          setActive(0)
        }}
        onKeyDown={handleKeyDown}
        onBlur={(e) => {
          // Suggestions are chosen on mouse down and keep the focus here; only leaving the picker closes it.
          if (!e.currentTarget.parentElement?.contains(e.relatedTarget)) {
            setOpen(false)
            setQuery('')
          }
        }}
      />
      {selected && !open && (
        <button
          type="button"
          className="target-clear"
          aria-label={emptyLabel ? `Renunță la ${selected.label}` : 'Șterge alegerea'}
          title={emptyLabel ? `Înapoi la „${emptyLabel}”` : 'Șterge alegerea'}
          onClick={() => {
            onChange('')
            input.current?.focus()
          }}
        >
          <Icon name="close" size={14} />
        </button>
      )}
      {open && (
        <div className="target-menu" ref={menu}>
          <ul id={listId} role="listbox" aria-label={label}>
            {options.map((option, index) => (
              <li
                key={option.value || 'self'}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={option.value === value}
                className={index === active ? 'active' : undefined}
                // Chosen on mouse down, before the input loses focus and closes the list.
                onMouseDown={(e) => {
                  e.preventDefault()
                  choose(option)
                }}
                onMouseEnter={() => setActive(index)}
              >
                <Icon name={option.kind === 'group' ? 'users' : 'user'} size={15} />
                <span className="target-name">{option.label}</span>
                {option.detail && <span className="target-detail">{option.detail}</span>}
              </li>
            ))}
          </ul>
          {empty && <p className="target-hint">Nicio persoană și niciun grup nu se potrivește.</p>}
          {!needle && <p className="target-hint">Scrie câteva litere din nume, email sau grup.</p>}
        </div>
      )}
    </div>
  )
}
