import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'dark' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md' | 'lg'

const variants: Record<Variant, string> = {
  primary: 'bg-accent text-white hover:bg-accent-hover shadow-card',
  dark: 'bg-ink text-white hover:bg-black',
  secondary: 'bg-white text-ink border border-line hover:bg-surface-2',
  ghost: 'text-ink-2 hover:bg-surface-2 hover:text-ink',
  danger: 'bg-white text-red-ink border border-red-soft hover:bg-red-soft',
}

const sizes: Record<Size, string> = {
  sm: 'h-8 px-3 text-[13px] gap-1.5',
  md: 'h-10 px-4 text-sm gap-2',
  lg: 'h-12 px-6 text-base gap-2',
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
      className={`inline-flex items-center justify-center rounded-control font-medium transition-colors
        disabled:opacity-50 disabled:cursor-not-allowed select-none whitespace-nowrap
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
