import { useEffect } from 'react'
import { documentTitle, refreshBrand } from '../lib/brand'
import { useAttentionStore } from '../stores/attentionStore'
import { useBrandStore } from '../stores/brandStore'

/** Draws nothing: fetches the organisation's name once and keeps the window title in step with it and with the number of issues waiting for the person. */
export default function BrandLoader() {
  const companyName = useBrandStore((s) => s.companyName)
  const waiting = useAttentionStore((s) => s.count)

  useEffect(() => {
    const controller = new AbortController()
    void refreshBrand(controller.signal)
    return () => controller.abort()
  }, [])

  useEffect(() => { document.title = documentTitle(companyName, waiting) }, [companyName, waiting])

  return null
}
