import { create } from 'zustand'
import api from '../services/api'

// The name is remembered per server, so the right one is on screen at once next time and another company's name never shows
// after the app is pointed at a different server.
const storageKey = () => `brand.companyName:${String(api.defaults.baseURL ?? '')}`

const remembered = () => {
  try { return localStorage.getItem(storageKey()) ?? '' } catch { return '' }
}

interface BrandState {
  /** The organisation's name as the server reports it; empty when none was set (the app then says just "Helpdesk"). */
  companyName: string
  setCompanyName: (name: string) => void
}

export const useBrandStore = create<BrandState>((set) => ({
  companyName: remembered(),
  setCompanyName: (name) => {
    try { localStorage.setItem(storageKey(), name) } catch { /* private window: it is only a convenience */ }
    set({ companyName: name })
  },
}))
