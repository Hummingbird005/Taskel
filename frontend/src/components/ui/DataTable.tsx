import type { ReactNode } from 'react'

/**
 * The frame around a table. On wide screens the table scrolls inside it; on phones each row
 * becomes a card, and each cell shows its `data-label` as the name of its column.
 */
export default function DataTable({ label, children }: { label?: string; children: ReactNode }) {
  return (
    <section className="team-table-wrap cards" aria-label={label}>
      <table className="team-table">{children}</table>
    </section>
  )
}
