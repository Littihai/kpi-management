import { create } from 'zustand'
import type { SetupStatus } from '../services/setup.service'

/** 'unknown' until the server has answered (it may be asleep or starting); then either running normally or waiting to be connected to a database. */
export type ServerState = 'unknown' | 'running' | 'setup'

interface SetupState {
  server: ServerState
  /** Set while the server waits to be told where its database is. */
  status: SetupStatus | null
  setStatus: (status: SetupStatus | null) => void
  setRunning: () => void
}

export const useSetupStore = create<SetupState>((set) => ({
  server: 'unknown',
  status: null,
  setStatus: (status) => set({ status, server: status ? 'setup' : 'unknown' }),
  setRunning: () => set({ status: null, server: 'running' }),
}))
