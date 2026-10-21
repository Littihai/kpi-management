import { createContext, useContext } from 'react'

/** True while a page is drawn inside the settings window: no page padding, and the window supplies the title. */
export const EmbeddedContext = createContext(false)
export const useEmbedded = () => useContext(EmbeddedContext)
