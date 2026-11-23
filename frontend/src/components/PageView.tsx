import type { User } from '../api/auth'
import type { View } from '../lib/taskViews'
import AdminUsers from './AdminUsers'
import EmailNotifications from './EmailNotifications'
import Groups from './Groups'
import Integrations from './Integrations'
import SystemSettings from './SystemSettings'
import Team from './Team'
import TeamTasks from './TeamTasks'

interface Props {
  kind: View['kind']
  user: User
  today: string
  onSessionExpired: () => void
  /** The signed-in account itself was changed (its role or department). */
  onUserChanged: () => void
  onGroupsChanged: () => void
  onAttachmentLimitChanged: () => void
}

/** The views that are pages of their own rather than lists of tasks. */
export default function PageView({
  kind,
  user,
  today,
  onSessionExpired,
  onUserChanged,
  onGroupsChanged,
  onAttachmentLimitChanged,
}: Props) {
  switch (kind) {
    case 'integrations':
      return (
        <>
          <EmailNotifications onSessionExpired={onSessionExpired} />
          <Integrations onSessionExpired={onSessionExpired} />
        </>
      )
    case 'team':
      return <Team user={user} today={today} onSessionExpired={onSessionExpired} />
    case 'teamTasks':
      return <TeamTasks user={user} today={today} onSessionExpired={onSessionExpired} />
    case 'groups':
      return <Groups user={user} onSessionExpired={onSessionExpired} onChanged={onGroupsChanged} />
    case 'admin':
      return <AdminUsers user={user} onSessionExpired={onSessionExpired} onOwnAccountChanged={onUserChanged} />
    case 'settings':
      return <SystemSettings onSessionExpired={onSessionExpired} onAttachmentLimitChanged={onAttachmentLimitChanged} />
    default:
      return null
  }
}
