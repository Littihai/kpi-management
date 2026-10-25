// Runs in the sandboxed preload context: only contextBridge and ipcRenderer are used.
const { contextBridge, ipcRenderer } = require('electron')

const CH_STATE = 'desktop:update-state'
const CH_GET = 'desktop:get-update-state'
const CH_CHECK = 'desktop:check-for-updates'
const CH_RESTART = 'desktop:restart-to-update'
const CH_LOCAL_STATUS = 'desktop:local-server:status'
const CH_LOCAL_ENABLE = 'desktop:local-server:enable'
const CH_LOCAL_DISABLE = 'desktop:local-server:disable'
const CH_LOCAL_RESET = 'desktop:local-server:reset-database'
const CH_LOCAL_URL = 'desktop:local-server:url'
const CH_CRED_AVAILABLE = 'desktop:credentials:available'
const CH_CRED_GET = 'desktop:credentials:get'
const CH_CRED_SAVE = 'desktop:credentials:save'
const CH_CRED_UPDATE = 'desktop:credentials:update-password'
const CH_CRED_FORGET = 'desktop:credentials:forget'

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
  /** The Helpdesk server inside the app, connected to SQL Server by the person. Used when no company server was chosen. */
  localServer: {
    /** { available, enabled, running, url, error } */
    status: () => ipcRenderer.invoke(CH_LOCAL_STATUS),
    /** Turns it on (starts it) and remembers that. */
    enable: () => ipcRenderer.invoke(CH_LOCAL_ENABLE),
    /** Turns it off and stops it. */
    disable: () => ipcRenderer.invoke(CH_LOCAL_DISABLE),
    /** "Disconnect": forgets where the database is on this PC (its data is untouched) and starts again, waiting to be connected. */
    resetDatabase: () => ipcRenderer.invoke(CH_LOCAL_RESET),
    /** The address of the running built-in server, or '' when it is off. Answered at once, not as a promise. */
    url: () => ipcRenderer.sendSync(CH_LOCAL_URL),
  },
  /** The sign-in this computer was asked to remember, encrypted with the Windows account. One entry per server address. */
  credentials: {
    available: () => ipcRenderer.invoke(CH_CRED_AVAILABLE),
    get: (server) => ipcRenderer.invoke(CH_CRED_GET, server),
    save: (server, email, password) => ipcRenderer.invoke(CH_CRED_SAVE, server, email, password),
    updatePassword: (server, email, password) => ipcRenderer.invoke(CH_CRED_UPDATE, server, email, password),
    forget: (server) => ipcRenderer.invoke(CH_CRED_FORGET, server),
  },
  /** Calls cb with the current state now and on every change; returns an unsubscribe function. */
  onUpdateState: (cb) => {
    if (typeof cb !== 'function') return () => {}
    const listener = (_event, state) => cb(state)
    ipcRenderer.on(CH_STATE, listener)
    ipcRenderer.invoke(CH_GET).then(cb, () => {})
    return () => ipcRenderer.removeListener(CH_STATE, listener)
  },
})
