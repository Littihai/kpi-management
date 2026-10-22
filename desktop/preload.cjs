// Runs in the sandboxed preload context: only contextBridge and ipcRenderer are used.
const { contextBridge, ipcRenderer } = require('electron')

const CH_STATE = 'desktop:update-state'
const CH_GET = 'desktop:get-update-state'
const CH_CHECK = 'desktop:check-for-updates'
const CH_RESTART = 'desktop:restart-to-update'

const arg = process.argv.find((a) => a.startsWith('--desktop-version='))
const version = arg ? arg.slice('--desktop-version='.length) : '0.0.0'

const serverArg = process.argv.find((a) => a.startsWith('--helpdesk-server='))
const defaultServerUrl = serverArg ? serverArg.slice('--helpdesk-server='.length) : undefined

contextBridge.exposeInMainWorld('desktop', {
  version,
  isDesktop: true,
  defaultServerUrl,
  checkForUpdates: () => ipcRenderer.invoke(CH_CHECK),
  restartToUpdate: () => ipcRenderer.send(CH_RESTART),
  /** Calls cb with the current state now and on every change; returns an unsubscribe function. */
  onUpdateState: (cb) => {
    if (typeof cb !== 'function') return () => {}
    const listener = (_event, state) => cb(state)
    ipcRenderer.on(CH_STATE, listener)
    ipcRenderer.invoke(CH_GET).then(cb, () => {})
    return () => ipcRenderer.removeListener(CH_STATE, listener)
  },
})
