import { useEffect } from 'react'
import { documentTitle, refreshBrand } from '../lib/brand'
import { useBrandStore } from '../stores/brandStore'

/** Draws nothing: fetches the organisation's name once and keeps the window title in step with it. */
export default function BrandLoader() {
  const companyName = useBrandStore((s) => s.companyName)

  useEffect(() => {
    const controller = new AbortController()
    void refreshBrand(controller.signal)
    return () => controller.abort()
  }, [])

  useEffect(() => { document.title = documentTitle(companyName) }, [companyName])

  return null
}
