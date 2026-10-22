import { create } from 'zustand'
import type { SetupStatus } from '../services/setup.service'

interface SetupState {
  /** Set while the server waits to be told where its database is; null when it runs (or has not answered yet). */
  status: SetupStatus | null
  setStatus: (status: SetupStatus | null) => void
}

export const useSetupStore = create<SetupState>((set) => ({
  status: null,
  setStatus: (status) => set({ status }),
}))
