import { useEffect, useState } from 'react'
import { adminApi, type AdminUser } from '../api/admin'
import type { User, UserRole } from '../api/auth'
import { ApiError } from '../api/client'
import { validateDepartment } from '../lib/validation'

interface Props {
  user: User
  onSessionExpired: () => void
  /** Called after the signed-in user's own account changed, so the rest of the app can catch up. */
  onOwnAccountChanged: () => void
}

/** The administrator's screen: each account's role and department. */
export default function AdminUsers({ user, onSessionExpired, onOwnAccountChanged }: Props) {
  const [users, setUsers] = useState<AdminUser[] | null>(null)
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [savedId, setSavedId] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    adminApi
      .users()
      .then((loaded) => {
        if (!cancelled) setUsers(loaded)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onSessionExpired()
        else setError('Nu am putut încărca utilizatorii.')
      })
    return () => {
      cancelled = true
    }
  }, [onSessionExpired])

  async function save(account: AdminUser, role: UserRole, department: string) {
    const invalid = validateDepartment(department)
    if (invalid) {
      setError(invalid)
      return
    }
    try {
      const updated = await adminApi.updateUser(account.id, role, department.trim() || null)
      setUsers((prev) => prev?.map((u) => (u.id === updated.id ? updated : u)) ?? null)
      setDrafts((prev) => {
        const next = { ...prev }
        delete next[account.id]
        return next
      })
      setError(null)
      setSavedId(account.id)
      if (account.id === user.id) onOwnAccountChanged()
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired()
      else if (err instanceof ApiError && err.status === 403) setError('Doar administratorii pot modifica utilizatorii.')
      else setError(`Nu am putut salva modificarea pentru ${account.displayName}.`)
    }
  }

  if (!users) {
    return error ? (
      <p className="empty error" role="alert">
        {error}
      </p>
    ) : (
      <p className="empty">Se încarcă…</p>
    )
  }

  const departments = [...new Set(users.map((u) => u.department).filter((d): d is string => Boolean(d)))].sort((a, b) =>
    a.localeCompare(b, 'ro'),
  )

  return (
    <div className="team">
      <div className="team-head">
        <div>
          <h2>Roluri și departamente</h2>
          <p className="muted">
            Managerii văd încărcarea oamenilor din departamentul lor. Modificările se salvează imediat.
          </p>
        </div>
      </div>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      <section className="team-table-wrap">
        <table className="team-table">
          <thead>
            <tr>
              <th scope="col">Utilizator</th>
              <th scope="col">Cont</th>
              <th scope="col">Rol</th>
              <th scope="col">Departament</th>
            </tr>
          </thead>
          <tbody>
            {users.map((account) => {
              const isSelf = account.id === user.id
              const department = drafts[account.id] ?? account.department ?? ''
              return (
                <tr key={account.id}>
                  <th scope="row">
                    <span className="member-name">
                      {account.displayName}
                      {account.displayName !== account.email && <small className="muted">{account.email}</small>}
                    </span>
                  </th>
                  <td>{account.isDirectoryAccount ? 'Domeniu' : 'Local'}</td>
                  <td>
                    <select
                      value={account.role}
                      disabled={isSelf || account.roleIsFixed}
                      title={
                        account.roleIsFixed
                          ? 'Rol stabilit în configurarea serverului'
                          : isSelf
                            ? 'Nu îți poți schimba propriul rol'
                            : undefined
                      }
                      onChange={(e) => save(account, e.target.value as UserRole, department)}
                      aria-label={`Rol: ${account.displayName}`}
                    >
                      <option value="user">Utilizator</option>
                      <option value="manager">Manager</option>
                      <option value="admin">Administrator</option>
                    </select>
                  </td>
                  <td>
                    <span className="department-cell">
                      <input
                        value={department}
                        list="departments"
                        maxLength={100}
                        placeholder="Fără departament"
                        onChange={(e) => {
                          setSavedId(null)
                          setDrafts((prev) => ({ ...prev, [account.id]: e.target.value }))
                        }}
                        onBlur={() => {
                          if (department.trim() !== (account.department ?? '')) void save(account, account.role, department)
                        }}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') e.currentTarget.blur()
                        }}
                        aria-label={`Departament: ${account.displayName}`}
                      />
                      {savedId === account.id && (
                        <span className="saved" role="status">
                          Salvat
                        </span>
                      )}
                    </span>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
        <datalist id="departments">
          {departments.map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
      </section>
      <p className="muted team-note">
        Pentru conturile de domeniu, departamentul și rolurile stabilite prin grupuri în Active Directory se reaplică la
        fiecare conectare și pot înlocui ce setezi aici.
      </p>
    </div>
  )
}
