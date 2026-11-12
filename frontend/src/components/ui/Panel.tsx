import { useId, type ReactNode } from 'react'

/** A bordered card with a title, for settings and forms. */
export default function Panel({ title, children }: { title: string; children: ReactNode }) {
  const id = useId()
  return (
    <section className="panel" aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      {children}
    </section>
  )
}
