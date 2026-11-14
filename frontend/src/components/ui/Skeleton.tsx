import type { CSSProperties } from 'react'

/** A grey block standing in for content that is loading. */
export function Skeleton({ width = '100%', height = 14, style }: { width?: number | string; height?: number; style?: CSSProperties }) {
  return <span className="skeleton" style={{ width, height, ...style }} aria-hidden="true" />
}

/** Wraps skeletons so assistive technology hears one "loading" instead of a pile of empty blocks. */
function Loading({ className, children }: { className: string; children: React.ReactNode }) {
  return (
    <output className={className} aria-live="polite">
      <span className="visually-hidden">Se încarcă…</span>
      {children}
    </output>
  )
}

const WIDTHS = ['62%', '44%', '71%', '38%', '55%', '48%']

/** Rows shaped like tasks: a round checkbox, a title and a line of chips. */
export function TaskListSkeleton({ rows = 6 }: { rows?: number }) {
  return (
    <Loading className="skeleton-rows">
      {Array.from({ length: rows }, (_, i) => (
        <div className="skeleton-row" key={i}>
          <Skeleton width={20} height={20} style={{ borderRadius: '50%', flexShrink: 0 }} />
          <div>
            <Skeleton width={WIDTHS[i % WIDTHS.length]} height={15} />
            <Skeleton width={i % 2 ? 150 : 220} height={12} />
          </div>
        </div>
      ))}
    </Loading>
  )
}

/** A heading and a grid of cards, shaped like the dashboard. */
export function CardsSkeleton({ cards = 4 }: { cards?: number }) {
  return (
    <Loading className="skeleton-stack">
      <div>
        <Skeleton width={260} height={28} />
        <Skeleton width={180} height={13} style={{ marginTop: 10 }} />
      </div>
      <div className="skeleton-cards">
        {Array.from({ length: cards }, (_, i) => (
          <div className="skeleton-card" key={i}>
            <Skeleton width={110} height={11} />
            <Skeleton width={WIDTHS[i % WIDTHS.length]} />
            <Skeleton width={WIDTHS[(i + 2) % WIDTHS.length]} />
            <Skeleton width={WIDTHS[(i + 4) % WIDTHS.length]} />
          </div>
        ))}
      </div>
    </Loading>
  )
}

/** A heading and table rows, shaped like the team, groups and users screens. */
export function TableSkeleton({ rows = 5, heading = true }: { rows?: number; heading?: boolean }) {
  return (
    <Loading className="skeleton-stack">
      {heading && (
        <div>
          <Skeleton width={220} height={28} />
          <Skeleton width={320} height={13} style={{ marginTop: 10, maxWidth: '100%' }} />
        </div>
      )}
      <div className="skeleton-table">
        {Array.from({ length: rows }, (_, i) => (
          <div key={i}>
            <Skeleton width={WIDTHS[i % WIDTHS.length]} style={{ maxWidth: 240 }} />
            <Skeleton width={80} style={{ marginLeft: 'auto' }} />
            <Skeleton width={48} />
          </div>
        ))}
      </div>
    </Loading>
  )
}
