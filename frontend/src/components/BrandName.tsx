import { PRODUCT_NAME } from '../lib/brand'
import { useBrandStore } from '../stores/brandStore'

/** The organisation's name where the app shows its own name (login page, sidebar); "Helpdesk" when none is set. */
export default function BrandName({ className = '' }: { className?: string }) {
  const companyName = useBrandStore((s) => s.companyName)
  return <span className={className} title={companyName ? `${companyName} · ${PRODUCT_NAME}` : undefined}>{companyName || PRODUCT_NAME}</span>
}
