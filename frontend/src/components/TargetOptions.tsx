import type { GroupListItem } from '../api/groups'
import type { UserSummary } from '../api/users'
import { GROUP_PREFIX } from '../lib/taskViews'
import { countLabel } from '../lib/text'

/** The options of a "who gets this task" picker: people first, then groups. */
export default function TargetOptions({ people, groups }: { people: UserSummary[]; groups: GroupListItem[] }) {
  const options = people.map((person) => (
    <option key={person.id} value={person.id}>
      {person.displayName}
    </option>
  ))
  if (groups.length === 0) return <>{options}</>
  return (
    <>
      <optgroup label="Persoane">{options}</optgroup>
      <optgroup label="Grupuri">
        {groups.map((group) => (
          <option key={group.id} value={GROUP_PREFIX + group.id}>
            {group.name}
            {group.kind === 'directory' ? ' (AD)' : ''} · {countLabel(group.memberCount, 'membru', 'membri')}
          </option>
        ))}
      </optgroup>
    </>
  )
}
