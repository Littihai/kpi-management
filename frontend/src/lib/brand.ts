import api from '../services/api'
import { useBrandStore } from '../stores/brandStore'

export const PRODUCT_NAME = 'Helpdesk'

/**
 * The browser/window title: "Acme Co — Helpdesk", or the product's own title when no organisation name is set. With work
 * waiting for the person it starts with the number, "(3) Acme Co — Helpdesk", which also shows on the taskbar and in the tab.
 */
export const documentTitle = (companyName: string, waiting = 0) =>
  `${waiting > 0 ? `(${waiting}) ` : ''}${companyName ? `${companyName} — ${PRODUCT_NAME}` : `${PRODUCT_NAME} — ศูนย์ช่วยเหลือ`}`

/** Asks the server for the organisation's name (public, before anyone signs in). A server that cannot be reached keeps what is shown. */
export async function refreshBrand(signal?: AbortSignal): Promise<void> {
  try {
    const response = await fetch(`${String(api.defaults.baseURL ?? '')}/api/client-config`, { signal, cache: 'no-store' })
    if (!response.ok) return
    const config = (await response.json()) as { companyName?: unknown }
    if (typeof config.companyName === 'string') useBrandStore.getState().setCompanyName(config.companyName.trim().slice(0, 80))
  } catch { /* offline or asleep: leave the name as it is */ }
}
