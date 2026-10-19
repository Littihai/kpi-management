import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'dark' | 'secondary' | 'ghost' | 'danger' | 'success'
type Size = 'sm' | 'md' | 'lg'

// Same look and states as Lotline's .btn: filled primary with a soft shadow, bordered secondary,
// solid danger / success, and a plain accent link-style ghost.
const variants: Record<Variant, string> = {
  primary: 'bg-accent text-on-accent shadow-card hover:bg-accent-hover active:bg-accent-deep active:shadow-none',
  dark: 'bg-deep text-on-deep hover:brightness-125',
  secondary: 'bg-card text-ink border border-line-strong hover:bg-surface hover:border-ink-4 active:bg-surface-2',
  ghost: 'text-accent-ink hover:bg-surface hover:text-accent-deep',
  danger: 'bg-red-dot text-white hover:brightness-90 active:brightness-75',
  success: 'bg-green-dot text-white hover:brightness-90 active:brightness-75',
}

const sizes: Record<Size, string> = {
  sm: 'min-h-8 px-3 py-1 text-xs font-semibold gap-1.5',
  md: 'min-h-9 px-4 py-1.5 text-[13px] font-medium gap-1.5',
  lg: 'min-h-11 px-6 py-2 text-sm font-medium gap-2',
}

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  icon?: string
  loading?: boolean
  children?: ReactNode
}

export default function Button({
  variant = 'secondary', size = 'md', icon, loading, children, className = '', disabled, type = 'button', ...rest
}: Props) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={`inline-flex items-center justify-center rounded-control border border-transparent leading-snug transition-colors
        disabled:opacity-55 disabled:cursor-not-allowed select-none whitespace-nowrap
        ${variants[variant]} ${sizes[size]} ${className}`}
      {...rest}
    >
      {loading
        ? <i className="ti ti-loader-2 animate-spin" aria-hidden="true" />
        : icon && <i className={`ti ti-${icon} text-[1.15em]`} aria-hidden="true" />}
      {children}
    </button>
  )
}
