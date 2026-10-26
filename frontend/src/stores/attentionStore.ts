import { create } from 'zustand'
import { attentionStatus, type AttentionStatus } from '../lib/attention'

/**
 * The status of the signed-in person (nothing waiting / work waiting / connecting / not connected). AttentionTracker keeps
 * it current while the app shell is on screen; the status dot and the window title read it. A shell that has just opened
 * is connecting, which is what this starts as.
 */
export const useAttentionStore = create<AttentionStatus>(() => attentionStatus('connecting', 0))
