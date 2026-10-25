/** An on/off switch. It is a real button with role="switch", so the keyboard (Space / Enter) and screen readers work. */
export default function Switch({ checked, onChange, disabled, label, labelledBy, describedBy }: {
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  /** accessible name when no visible label points at the switch */
  label?: string
  labelledBy?: string
  describedBy?: string
}) {
  return (
    <button type="button" role="switch" aria-checked={checked} aria-label={label} aria-labelledby={labelledBy} aria-describedby={describedBy}
      disabled={disabled} onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full border border-transparent transition-colors
        disabled:cursor-not-allowed disabled:opacity-50 ${checked ? 'bg-accent' : 'bg-line-strong'}`}>
      <span aria-hidden="true"
        className={`h-5 w-5 rounded-full bg-white shadow-card transition-transform ${checked ? 'translate-x-[22px]' : 'translate-x-0.5'}`} />
    </button>
  )
}
