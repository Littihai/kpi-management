import { create } from 'zustand'

/**
 * The numbers behind the ready-made views in the menu. AttentionTracker keeps them current while the app shell is on
 * screen (the same requests that drive the status dot), so the menu does not ask the server again on its own.
 */
export const useAttentionCountsStore = create<{ pending: number; assigned: number; nearSla: number }>(() => ({
  pending: 0,
  assigned: 0,
  nearSla: 0,
}))
