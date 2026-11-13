import Button from './ui/Button'
import DataTable from './ui/DataTable'
import EmptyState from './ui/EmptyState'
import { TableSkeleton } from './ui/Skeleton'
import { useEffect, useState, type FormEvent } from 'react'
import type { User } from '../api/auth'
import { ApiError } from '../api/client'
import { GROUP_KIND_LABEL, groupsApi, type GroupDetail, type GroupListItem } from '../api/groups'
import type { UserSummary } from '../api/users'
import { countLabel } from '../lib/text'
import { LIMITS, validateGroupName } from '../lib/validation'
import Icon from './Icon'

interface Props {
  user: User
  onSessionExpired: () => void
  /** Called after a group was created, changed or deleted, so pickers and tasks can catch up. */
  onChanged: () => void
}

/** What is open below the list: a new group, or an existing one to edit or just look at. */
type Panel = { mode: 'new' } | { mode: 'existing'; group: GroupDetail }

/**
 * The groups tasks can be given to. Administrators manage every application group; managers the
 * ones they created, with people from their department. Active Directory groups are shown with
 * the members who have signed in, and are changed in Active Directory.
 */
export default function Groups({ user, onSessionExpired, onChanged }: Props) {
  const [groups, setGroups] = useState<GroupListItem[] | null>(null)
  const [candidates, setCandidates] = useState<UserSummary[]>([])
  const [panel, setPanel] = useState<Panel | null>(null)
  const [name, setName] = useState('')
  const [memberIds, setMemberIds] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    let cancelled = false
    Promise.all([groupsApi.list(), groupsApi.candidates()])
      .then(([loaded, people]) => {
        if (cancelled) return
        setGroups(loaded)
        setCandidates(people)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca grupurile.')
      })
    return () => {
      cancelled = true
    }
  }, [onSessionExpired])

  function fail(err: unknown, fallback: string) {
    if (err instanceof ApiError && err.status === 401) onSessionExpired()
    else if (err instanceof ApiError && err.status === 403) setError('Poți modifica doar grupurile create de tine.')
    else setError(fallback)
  }

  async function reload() {
    setGroups(await groupsApi.list())
    onChanged()
  }

  function openNew() {
    setPanel({ mode: 'new' })
    setName('')
    setMemberIds(new Set())
    setError(null)
  }

  async function open(id: string) {
    try {
      const group = await groupsApi.get(id)
      setPanel({ mode: 'existing', group })
      setName(group.name)
      setMemberIds(new Set(group.members.map((m) => m.id)))
      setError(null)
    } catch (err) {
      fail(err, 'Nu am putut deschide grupul.')
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!panel || saving) return
    const invalid = validateGroupName(name)
    if (invalid) {
      setError(invalid)
      return
    }
    setSaving(true)
    try {
      if (panel.mode === 'new') await groupsApi.create(name.trim(), [...memberIds])
      else await groupsApi.update(panel.group.id, name.trim(), [...memberIds])
      setPanel(null)
      setError(null)
      await reload()
    } catch (err) {
      if (err instanceof ApiError && err.status === 400) {
        setError(
          err.message.includes('already')
            ? 'Există deja un grup cu acest nume.'
            : user.role === 'admin'
              ? 'Unii dintre membrii aleși nu mai există.'
              : 'Poți adăuga doar persoane din departamentul tău.',
        )
      } else {
        fail(err, 'Nu am putut salva grupul.')
      }
    } finally {
      setSaving(false)
    }
  }

  async function remove(group: GroupListItem) {
    const ok = window.confirm(
      `Ștergi grupul „${group.name}”? Task-urile lui nepreluate se întorc la cei care le-au creat.`,
    )
    if (!ok) return
    try {
      await groupsApi.remove(group.id)
      if (panel?.mode === 'existing' && panel.group.id === group.id) setPanel(null)
      setError(null)
      await reload()
    } catch (err) {
      fail(err, 'Nu am putut șterge grupul.')
    }
  }

  function toggleMember(id: string) {
    setError(null)
    setMemberIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  if (!groups) {
    return error ? (
      <EmptyState error title="Grupurile nu au putut fi încărcate">
        {error}
      </EmptyState>
    ) : (
      <TableSkeleton />
    )
  }

  const editable = panel?.mode === 'new' || (panel?.mode === 'existing' && panel.group.canManage)
  // People already in the group stay listed even when the manager could not add them anew.
  const existingMembers = panel?.mode === 'existing' ? panel.group.members : []
  const choices = [...candidates, ...existingMembers.filter((m) => !candidates.some((c) => c.id === m.id))].sort((a, b) =>
    a.displayName.localeCompare(b.displayName, 'ro'),
  )

  return (
    <div className="team">
      <div className="team-head">
        <div>
          <h2>Grupuri</h2>
          <p className="muted">
            Un task dat unui grup este văzut de toți membrii, până când unul dintre ei îl preia.
            {user.role === 'manager' && ' Poți crea grupuri cu oameni din departamentul tău.'}
          </p>
        </div>
        <button type="button" className="button primary" onClick={openNew}>
          <Icon name="plus" />
          Grup nou
        </button>
      </div>

      {error && !panel && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {groups.length === 0 ? (
        !panel && (
          <EmptyState
            icon="users"
            title="Niciun grup încă"
            action={
              <Button variant="primary" onClick={openNew}>
                <Icon name="plus" />
                Grup nou
              </Button>
            }
          >
            Un grup îți permite să dai un task mai multor oameni deodată; îl preia cine are loc.
          </EmptyState>
        )
      ) : (
        <DataTable label="Grupuri">
            <thead>
              <tr>
                <th scope="col">Grup</th>
                <th scope="col">Tip</th>
                <th scope="col" className="num">
                  Membri
                </th>
                <th scope="col">
                  <span className="visually-hidden">Acțiuni</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {groups.map((group) => (
                <tr key={group.id} className={panel?.mode === 'existing' && panel.group.id === group.id ? 'open' : undefined}>
                  <th scope="row">{group.name}</th>
                  <td data-label="Tip">
                    <span className={`kind-badge ${group.kind}`}>
                      {GROUP_KIND_LABEL[group.kind]}
                    </span>
                  </td>
                  <td className="num" data-label="Membri">{group.memberCount}</td>
                  <td className="group-actions">
                    <button type="button" className="button ghost" onClick={() => void open(group.id)}>
                      {group.canManage ? 'Editează' : 'Vezi membrii'}
                    </button>
                    {group.canManage && (
                      <button
                        type="button"
                        className="button ghost danger"
                        onClick={() => void remove(group)}
                        aria-label={`Șterge grupul ${group.name}`}
                      >
                        <Icon name="trash" />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
        </DataTable>
      )}

      {panel && editable && (
        <form className="group-form" onSubmit={handleSubmit} noValidate>
          <h3>{panel.mode === 'new' ? 'Grup nou' : `Editează „${panel.group.name}”`}</h3>
          <label className="group-name">
            <span>Nume</span>
            <input
              value={name}
              onChange={(e) => {
                setError(null)
                setName(e.target.value)
              }}
              maxLength={LIMITS.groupName}
              placeholder="de ex. Echipa de ofertare"
              aria-label="Numele grupului"
              autoFocus
            />
          </label>
          <fieldset className="group-members">
            <legend>
              Membri <span className="muted">· {countLabel(memberIds.size, 'ales', 'aleși')}</span>
            </legend>
            {choices.length === 0 ? (
              <p className="muted">Nu există persoane pe care să le poți adăuga.</p>
            ) : (
              <ul>
                {choices.map((person) => (
                  <li key={person.id}>
                    <label>
                      <input type="checkbox" checked={memberIds.has(person.id)} onChange={() => toggleMember(person.id)} />
                      <span>
                        {person.displayName}
                        {person.displayName !== person.email && <small className="muted">{person.email}</small>}
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
          </fieldset>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <div className="reply-form-actions">
            <Button type="submit" variant="primary" loading={saving}>
              {panel.mode === 'new' ? 'Creează grupul' : 'Salvează'}
            </Button>
            <button type="button" className="button ghost" onClick={() => setPanel(null)} disabled={saving}>
              Anulează
            </button>
          </div>
        </form>
      )}

      {panel?.mode === 'existing' && !editable && (
        <section className="group-form">
          <h3>{panel.group.name}</h3>
          <p className="muted">
            {panel.group.kind === 'app'
              ? 'Grup creat de altcineva: îl poți folosi, dar nu îl poți modifica.'
              : `Grup din ${GROUP_KIND_LABEL[panel.group.kind]}: componența se schimbă acolo. Aici apar membrii care s-au conectat cel puțin o dată la Taskel.`}
          </p>
          {panel.group.members.length === 0 ? (
            <p className="muted">Niciun membru.</p>
          ) : (
            <ul className="group-readonly">
              {panel.group.members.map((member) => (
                <li key={member.id}>
                  {member.displayName}
                  {member.displayName !== member.email && <small className="muted">{member.email}</small>}
                </li>
              ))}
            </ul>
          )}
          <div className="reply-form-actions">
            <button type="button" className="button ghost" onClick={() => setPanel(null)}>
              Închide
            </button>
          </div>
        </section>
      )}

      <p className="muted team-note">
        Grupurile din Active Directory apar singure, pe măsură ce membrii lor se conectează la Taskel cu contul de
        domeniu.
      </p>
    </div>
  )
}
