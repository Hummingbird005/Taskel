const PATHS = {
  inbox: 'M3 13l2.5-8h13L21 13M3 13v5a1 1 0 001 1h16a1 1 0 001-1v-5M3 13h5l1.5 2.5h5L16 13h5',
  sun: 'M12 8a4 4 0 100 8 4 4 0 000-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  calendar: 'M7 3v3M17 3v3M4 9h16M5 5h14a1 1 0 011 1v13a1 1 0 01-1 1H5a1 1 0 01-1-1V6a1 1 0 011-1z',
  alert: 'M12 8v5M12 16.5v.5M12 3a9 9 0 100 18 9 9 0 000-18z',
  checkCircle: 'M8 12.5l2.8 2.8L16.5 9.5M12 3a9 9 0 100 18 9 9 0 000-18z',
  hash: 'M9 3L7 21M17 3l-2 18M4 8.5h17M3 15.5h17',
  plug: 'M9 3v5M15 3v5M6 8h12v3a6 6 0 01-12 0V8zM12 17v4',
  logout: 'M15 4h3a1 1 0 011 1v14a1 1 0 01-1 1h-3M10 8l-4 4 4 4M6 12h10',
  edit: 'M4 20h4L19 9l-4-4L4 16v4zM13.5 6.5l4 4',
  trash: 'M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6',
  search: 'M11 4a7 7 0 100 14 7 7 0 000-14zM20 20l-4-4',
  menu: 'M4 6h16M4 12h16M4 18h16',
  send: 'M4 12l16-8-6 16-3-7-7-1z',
  flag: 'M5 21V4M5 4h12l-2.5 4L17 12H5',
  mail: 'M4 6h16a1 1 0 011 1v10a1 1 0 01-1 1H4a1 1 0 01-1-1V7a1 1 0 011-1zM3.5 7l8.5 6 8.5-6',
  reply: 'M10 8L5 12l5 4M5 12h9a5 5 0 015 5v1',
  close: 'M6 6l12 12M18 6L6 18',
  home: 'M4 11l8-7 8 7M6 10v9a1 1 0 001 1h3v-6h4v6h3a1 1 0 001-1v-9',
  settings: 'M4 7h9M17 7h3M4 17h3M11 17h9M15 5v4M9 15v4',
  arrowLeft: 'M11 6l-6 6 6 6M5 12h14',
  arrowRight: 'M13 6l6 6-6 6M19 12H5',
  plus: 'M12 5v14M5 12h14',
  user: 'M12 4a4 4 0 100 8 4 4 0 000-8zM4.5 20a7.5 7.5 0 0115 0',
  bell: 'M6 16V11a6 6 0 0112 0v5l1.5 2h-15L6 16zM10 20a2 2 0 004 0',
} as const

export type IconName = keyof typeof PATHS

/** A small line icon that takes the current text colour. Decorative: label the control around it. */
export default function Icon({ name, size = 16 }: { name: IconName; size?: number }) {
  return (
    <svg
      className="icon-svg"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={PATHS[name]} />
    </svg>
  )
}
