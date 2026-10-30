import { create } from 'zustand'

/** Whether the Ctrl+K search box is open. A store, so the menu buttons and the keyboard shortcut open the same box. */
export const usePaletteStore = create<{ open: boolean; setOpen: (open: boolean) => void }>((set) => ({
  open: false,
  setOpen: (open) => set({ open }),
}))
