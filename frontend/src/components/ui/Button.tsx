import type { ButtonHTMLAttributes } from 'react'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger'
  /** Shows a spinner in place of the label and blocks further clicks. The button keeps its width. */
  loading?: boolean
  small?: boolean
}

/** The app's button. `secondary` is outlined; `danger` is outlined and turns red on hover. */
export default function Button({
  variant = 'secondary',
  loading = false,
  small = false,
  type = 'button',
  className = '',
  disabled,
  children,
  ...rest
}: Props) {
  const classes = [
    'button',
    variant === 'primary' ? 'primary' : 'ghost',
    variant === 'danger' ? 'danger' : '',
    small ? 'small' : '',
    loading ? 'is-loading' : '',
    className,
  ]
  return (
    <button
      type={type}
      className={classes.filter(Boolean).join(' ')}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {children}
    </button>
  )
}
