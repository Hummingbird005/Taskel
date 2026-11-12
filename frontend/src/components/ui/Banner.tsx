import type { ReactNode } from 'react'
import Icon from '../Icon'

/** A message across the top of the content: an error by default, or a neutral notice. */
export default function Banner({ tone = 'error', onClose, children }: { tone?: 'error' | 'info'; onClose: () => void; children: ReactNode }) {
  return (
    <div className={`banner${tone === 'info' ? ' info' : ''}`} role={tone === 'info' ? 'status' : 'alert'}>
      <span>{children}</span>
      <button type="button" onClick={onClose} aria-label="Închide mesajul">
        <Icon name="close" />
      </button>
    </div>
  )
}
