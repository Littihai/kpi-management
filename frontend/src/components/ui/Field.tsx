import { useId } from 'react'
import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'

const controlBase =
  // Lotline fields: neutral-300 border; the accent border + 3px ring on focus is drawn in index.css.
  'rounded-control border border-line-strong bg-card px-3 text-sm text-ink placeholder:text-ink-3 ' +
  'transition-colors hover:border-ink-4 focus:outline-none ' +
  'disabled:bg-surface-2 disabled:text-ink-3'
const control = `w-full ${controlBase}`

interface FieldProps {
  label: string
  hint?: string
  error?: string
  required?: boolean
  children: (id: string, describedBy: string | undefined) => ReactNode
  className?: string
}

/** Label + control + hint/error, wired together for screen readers. */
export function Field({ label, hint, error, required, children, className = '' }: FieldProps) {
  const id = useId()
  const noteId = hint || error ? `${id}-note` : undefined
  return (
    <div className={className}>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold text-ink-2">
        {label}
        {required && <span className="ml-0.5 text-accent" aria-hidden="true">*</span>}
      </label>
      {children(id, noteId)}
      {(error || hint) && (
        <p id={noteId} className={`mt-1.5 text-xs ${error ? 'text-red-ink' : 'text-ink-2'}`}>{error ?? hint}</p>
      )}
    </div>
  )
}

type TextInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> & Pick<FieldProps, 'label' | 'hint' | 'error'> & {
  wrapperClassName?: string
}

export function TextInput({ label, hint, error, required, wrapperClassName, className = '', ...rest }: TextInputProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={wrapperClassName}>
      {(id, describedBy) => (
        <input id={id} aria-describedby={describedBy} aria-invalid={!!error} required={required}
          className={`${control} h-9 ${error ? 'border-red-dot' : ''} ${className}`} {...rest} />
      )}
    </Field>
  )
}

type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id'> & Pick<FieldProps, 'label' | 'hint' | 'error'> & {
  options: { value: string | number; label: string }[]
  placeholder?: string
  wrapperClassName?: string
}

export function SelectInput({ label, hint, error, required, options, placeholder, wrapperClassName, className = '', ...rest }: SelectProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={wrapperClassName}>
      {(id, describedBy) => (
        <select id={id} aria-describedby={describedBy} aria-invalid={!!error} required={required}
          className={`${control} h-9 pr-8 ${className}`} {...rest}>
          {placeholder !== undefined && <option value="">{placeholder}</option>}
          {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )}
    </Field>
  )
}

type TextAreaProps = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'id'> & Pick<FieldProps, 'label' | 'hint' | 'error'> & {
  wrapperClassName?: string
}

export function TextArea({ label, hint, error, required, wrapperClassName, className = '', ...rest }: TextAreaProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={wrapperClassName}>
      {(id, describedBy) => (
        <textarea id={id} aria-describedby={describedBy} aria-invalid={!!error} required={required}
          className={`${control} py-2.5 resize-y min-h-[88px] ${error ? 'border-red-dot' : ''} ${className}`} {...rest} />
      )}
    </Field>
  )
}

/** Bare select for toolbars (no label); pass an aria-label. */
export function CompactSelect({ className = '', children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select className={`${controlBase} h-8 pr-8 text-[13px] ${className}`} {...rest}>{children}</select>
  )
}

/** Choose one of a few cards (used for "how badly does this affect you"). */
export function ChoiceCards<T extends number>({
  label, value, onChange, options, required,
}: {
  label: string
  value: T
  onChange: (value: T) => void
  options: { value: T; title: string; hint: string; icon: string }[]
  required?: boolean
}) {
  return (
    <fieldset>
      <legend className="mb-1 text-xs font-semibold text-ink-2">
        {label}{required && <span className="ml-0.5 text-accent" aria-hidden="true">*</span>}
      </legend>
      <div className="grid gap-2 sm:grid-cols-3">
        {options.map((o) => {
          const active = o.value === value
          return (
            <label key={o.value}
              className={`relative cursor-pointer rounded-control border p-3 transition-colors
                ${active ? 'border-accent bg-accent-soft' : 'border-line bg-card hover:border-line-strong'}`}>
              <input type="radio" className="sr-only" checked={active} onChange={() => onChange(o.value)} />
              <i className={`ti ti-${o.icon} text-lg ${active ? 'text-accent' : 'text-ink-3'}`} aria-hidden="true" />
              <span className="mt-1 block text-[13px] font-medium text-ink">{o.title}</span>
              <span className="block text-xs text-ink-2">{o.hint}</span>
              {active && <i className="ti ti-circle-check-filled absolute right-2.5 top-2.5 text-accent" aria-hidden="true" />}
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}
