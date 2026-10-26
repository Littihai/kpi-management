const { app, BrowserWindow, Menu, Tray, Notification, nativeImage, protocol, net, shell, ipcMain, safeStorage } = require('electron')
const path = require('node:path')
const fs = require('node:fs')
const { pathToFileURL } = require('node:url')
const { createCredentialStore } = require('./credentials.cjs')
const { createLocalServer } = require('./localServer.cjs')
const { describeStatus } = require('./statusModel.cjs')
const { normalizeNotification, shouldShowToast } = require('./notifyModel.cjs')
const { createPrefsStore } = require('./prefs.cjs')

// The server the company's IT wants this computer to use (optional): the HELPDESK_SERVER_URL environment variable, or
// the first line of %ProgramData%\Helpdesk\server.txt (it survives app updates). A person can still choose another.
function validServerUrl(text) {
  try {
    const url = new URL(String(text || '').trim())
    if (!/^https?:$/.test(url.protocol) || (url.pathname !== '/' && url.pathname !== '') || url.search || url.hash || url.username) return null
    return url.origin
  } catch { return null }
}

function defaultServerUrl() {
  const fromEnv = validServerUrl(process.env.HELPDESK_SERVER_URL)
  if (fromEnv) return fromEnv
  try {
    const file = path.join(process.env.ProgramData || 'C:\\ProgramData', 'Helpdesk', 'server.txt')
    return validServerUrl(fs.readFileSync(file, 'utf8').split(/\r?\n/)[0])
  } catch { return null }
}

// The web build is served from app://local/ so absolute asset paths, the router and
// localStorage behave exactly as on a website (file:// would break all three).
const SCHEME = 'app'
const HOST = 'local'
const WEB_ROOT = path.join(__dirname, 'app')

// Files under build/ are not packed into app.asar: package.json copies build/icon.png and build/status next to it (extraResources).
const BUILD_DIR = app.isPackaged ? path.join(process.resourcesPath, 'build') : path.join(__dirname, 'build')
const APP_ICON = path.join(BUILD_DIR, 'icon.png')
const APP_ID = 'com.ritz.helpdesk'   // the same as build.appId in package.json: Windows shows the app's name and icon on its toasts
const HIDDEN_ARG = '--hidden'        // given when Windows starts the app at sign-in: it goes straight to the system tray

protocol.registerSchemesAsPrivileged([
  { scheme: SCHEME, privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } },
])

const gotLock = app.requestSingleInstanceLock()
if (!gotLock) {
  app.quit()
}
// A development run has no installed shortcut carrying the app id, which Windows needs before it shows toasts.
app.setAppUserModelId(app.isPackaged ? APP_ID : process.execPath)

let win = null
let tray = null
let quitting = false // set by every real quit (see before-quit below); until then the close button may only hide the window

function resolveFile(urlPath) {
  const clean = path.normalize(decodeURIComponent(urlPath)).split(path.sep).filter(Boolean).join(path.sep)
  const file = path.join(WEB_ROOT, clean)
  if (!file.startsWith(WEB_ROOT)) return null           // never serve anything outside the web build
  if (fs.existsSync(file) && fs.statSync(file).isFile()) return file
  return path.extname(clean) ? null : path.join(WEB_ROOT, 'index.html') // client-side routes fall back to the app
}

function createWindow({ hidden = false } = {}) {
  win = new BrowserWindow({
    width: 1360,
    height: 860,
    minWidth: 960,
    minHeight: 600,
    show: !hidden,
    backgroundColor: '#f6f8fb',
    title: 'Helpdesk',
    icon: APP_ICON,
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      backgroundThrottling: false, // hidden in the tray the page must keep receiving real-time updates at full speed
      preload: path.join(__dirname, 'preload.cjs'),
      additionalArguments: [`--desktop-version=${app.getVersion()}`, ...(defaultServerUrl() ? [`--helpdesk-server=${defaultServerUrl()}`] : [])],
    },
  })
  Menu.setApplicationMenu(null)

  // Links to other sites (e.g. attachments, docs) open in the real browser, not inside the app.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:/i.test(url)) shell.openExternal(url)
    return { action: 'deny' }
  })
  win.webContents.on('will-navigate', (event, url) => {
    if (!url.startsWith(`${SCHEME}://${HOST}/`)) {
      event.preventDefault()
      if (/^https?:/i.test(url)) shell.openExternal(url)
    }
  })

  // With "close to tray" on, the close button hides the window and the app keeps running in the system tray. Only while no real quit is on its way, and
  // only when there is a tray to find it again: without one the window closes (and the app quits) as it always did.
  win.on('close', (event) => {
    if (quitting || !tray || !prefs.get().closeToTray) return
    event.preventDefault()
    win.hide()
    tellAboutTray()
  })
  // Windows ending the session (shutdown, restart, sign-out) must never be held up by a hidden window.
  for (const name of ['query-session-end', 'session-end']) win.on(name, () => { quitting = true })
  win.on('show', applyOverlay)                           // a hidden window has no taskbar button, so its overlay is put back when it returns
  win.webContents.on('did-finish-load', applyOverlay)    // ...and the first one needs the taskbar button, which exists by the time the page is up
  win.loadURL(`${SCHEME}://${HOST}/`)
  win.on('closed', () => { win = null })
}

// ---- Automatic updates (GitHub releases) -------------------------------------------------------------
// State sent to the page: { status: 'idle'|'checking'|'available'|'not-available'|'downloading'|'downloaded'|'error',
//                           version?, percent? }. Failures are logged and reported quietly; they never crash the app.
const CH_STATE = 'desktop:update-state'
const CH_GET = 'desktop:get-update-state'
const CH_CHECK = 'desktop:check-for-updates'
const CH_RESTART = 'desktop:restart-to-update'
const CHECK_EVERY_MS = 4 * 60 * 60 * 1000

let updateState = { status: 'idle' }
let autoUpdater = null

function setUpdateState(next) {
  updateState = next
  if (win && !win.isDestroyed()) win.webContents.send(CH_STATE, updateState)
}

// Compares scheme and host, not `origin`: for a custom scheme such as app:// Node reports the origin as the string "null", which
// made every check fail (the update buttons and "remember my password" never worked in the installed app).
function trustedSender(event) {
  try {
    const url = new URL(event.senderFrame.url)
    return url.protocol === `${SCHEME}:` && url.host === HOST
  } catch { return false }
}

async function checkForUpdates() {
  if (!autoUpdater) { setUpdateState({ status: 'not-available' }); return }
  if (updateState.status === 'downloading' || updateState.status === 'downloaded') return
  try {
    await autoUpdater.checkForUpdates()
  } catch (err) {
    console.error('[updater] check failed:', err && err.message)
    setUpdateState({ status: 'error' })
  }
}

function setupUpdater() {
  if (!app.isPackaged) return // dev runs (npm start) never self-update
  try {
    ;({ autoUpdater } = require('electron-updater'))
    autoUpdater.autoDownload = true
    autoUpdater.autoInstallOnAppQuit = true
    autoUpdater.on('checking-for-update', () => setUpdateState({ status: 'checking' }))
    autoUpdater.on('update-available', (i) => setUpdateState({ status: 'available', version: i.version }))
    autoUpdater.on('update-not-available', () => setUpdateState({ status: 'not-available' }))
    autoUpdater.on('download-progress', (p) =>
      setUpdateState({ status: 'downloading', version: updateState.version, percent: Math.round(p.percent || 0) }))
    autoUpdater.on('update-downloaded', (i) => setUpdateState({ status: 'downloaded', version: i.version }))
    autoUpdater.on('error', (err) => {
      console.error('[updater] error:', err && err.message)
      setUpdateState({ status: 'error' })
    })
    setTimeout(checkForUpdates, 15 * 1000)
    setInterval(checkForUpdates, CHECK_EVERY_MS)
  } catch (err) {
    console.error('[updater] unavailable:', err && err.message)
    autoUpdater = null
  }
}

ipcMain.handle(CH_GET, (event) => (trustedSender(event) ? updateState : { status: 'idle' }))
ipcMain.handle(CH_CHECK, async (event) => { if (trustedSender(event)) await checkForUpdates() })
ipcMain.on(CH_RESTART, (event) => {
  if (!trustedSender(event) || !autoUpdater || updateState.status !== 'downloaded') return
  autoUpdater.quitAndInstall()
})

// ---- Remembered sign-in ("remember my password" on the login page) -----------------------------------------------
// Encrypted with the Windows account (see credentials.cjs). Only the app's own pages may ask; the answer to anything
// else is "nothing".
const CH_CRED_AVAILABLE = 'desktop:credentials:available'
const CH_CRED_GET = 'desktop:credentials:get'
const CH_CRED_SAVE = 'desktop:credentials:save'
const CH_CRED_UPDATE = 'desktop:credentials:update-password'
const CH_CRED_FORGET = 'desktop:credentials:forget'

let credentials = null
function credentialStore() {
  if (!credentials) credentials = createCredentialStore({ safeStorage, file: path.join(app.getPath('userData'), 'credentials.bin') })
  return credentials
}

ipcMain.handle(CH_CRED_AVAILABLE, (event) => trustedSender(event) && credentialStore().available())
ipcMain.handle(CH_CRED_GET, (event, server) => (trustedSender(event) ? credentialStore().get(server) : null))
ipcMain.handle(CH_CRED_SAVE, (event, server, email, password) => trustedSender(event) && credentialStore().save(server, email, password))
ipcMain.handle(CH_CRED_UPDATE, (event, server, email, password) => trustedSender(event) && credentialStore().updatePassword(server, email, password))
ipcMain.handle(CH_CRED_FORGET, (event, server) => trustedSender(event) && credentialStore().forget(server))

// ---- System tray, taskbar status and Windows notifications ------------------------------------------------------------
// The page reports what is waiting for the person (setStatus) and passes on every real-time notification it receives (notify). What shows
// is decided here, because only the main process knows whether the window is in front, minimised or hidden in the tray.
const CH_SET_STATUS = 'desktop:set-status'
const CH_NOTIFY = 'desktop:notify'
const CH_OPEN_ISSUE = 'desktop:open-issue'
const CH_PREFS_GET = 'desktop:prefs:get'
const CH_PREFS_SET = 'desktop:prefs:set'

// notifications / closeToTray / startWithWindows, kept in the user data folder (see prefs.cjs).
const prefs = createPrefsStore({ file: path.join(app.getPath('userData'), 'desktop-prefs.json') })
const trayHintFlag = path.join(app.getPath('userData'), 'tray-hint-shown')

let status = describeStatus({ state: 'connecting', count: 0 }) // until the page reports in
const statusImages = new Map()

// Each icon is read once, and only counts when the file is really there: a missing icon must never stop the app.
function statusImage(file) {
  if (!statusImages.has(file)) {
    const image = nativeImage.createFromPath(path.join(BUILD_DIR, 'status', file))
    if (image.isEmpty()) {
      console.error('[status] icon not found:', file)
      return null
    }
    statusImages.set(file, image)
  }
  return statusImages.get(file)
}

function applyOverlay() {
  if (win && !win.isDestroyed()) win.setOverlayIcon(statusImage(status.overlayIcon), status.description) // null clears it
}

function applyStatus(next) {
  status = next
  if (tray) {
    const image = statusImage(next.trayIcon)
    if (image) tray.setImage(image)
    tray.setToolTip(`Helpdesk - ${next.tooltip}`)
  }
  applyOverlay()
}

function showWindow() {
  if (!win || win.isDestroyed()) return
  if (win.isMinimized()) win.restore()
  win.show()
  win.focus()
}

function openIssue(issueId) {
  showWindow()
  if (issueId && win && !win.isDestroyed()) win.webContents.send(CH_OPEN_ISSUE, issueId)
}

function createTray() {
  const image = statusImage(status.trayIcon)
  if (!image) return // no icon, no tray: the window then closes and quits as it always did
  tray = new Tray(image)
  tray.setToolTip(`Helpdesk - ${status.tooltip}`)
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'เปิด Helpdesk', click: () => showWindow() },
    { type: 'separator' },
    { label: 'ออกจากโปรแกรม', click: () => app.quit() },
  ]))
  tray.on('click', () => showWindow())
}

// A Notification that is garbage collected stops reporting clicks, so the ones on screen are kept until they are gone.
const toasts = new Set()
function showToast(options, onClick) {
  if (!Notification.isSupported()) return
  const toast = new Notification({ ...options, icon: APP_ICON })
  const done = () => toasts.delete(toast)
  toast.on('click', () => { done(); onClick() })
  toast.on('close', done)
  toast.on('failed', done)
  toasts.add(toast)
  if (toasts.size > 20) toasts.delete(toasts.values().next().value) // one that is never clicked or closed does not stay forever
  toast.show()
}

// Said once per installation, so that a person who closes the window does not think the app is gone.
function tellAboutTray() {
  if (fs.existsSync(trayHintFlag)) return
  try {
    fs.mkdirSync(path.dirname(trayHintFlag), { recursive: true })
    fs.writeFileSync(trayHintFlag, '1')
  } catch { return } // not saved means it would be said at every close, so it is not said
  showToast({ title: 'Helpdesk ยังทำงานอยู่ที่ถาดระบบ' }, () => showWindow())
}

// Windows starts the app at sign-in with HIDDEN_ARG (the app is only registered when installed: a development run would register Electron itself).
function applyStartWithWindows(enabled) {
  if (!app.isPackaged) return
  try {
    app.setLoginItemSettings({ openAtLogin: enabled, args: [HIDDEN_ARG] })
  } catch (err) {
    console.error('[startup]', err && err.message)
  }
}

ipcMain.on(CH_SET_STATUS, (event, input) => {
  if (!trustedSender(event)) return
  const next = describeStatus(input)
  if (next && JSON.stringify(next) !== JSON.stringify(status)) applyStatus(next)
})

ipcMain.on(CH_NOTIFY, (event, input) => {
  if (!trustedSender(event)) return
  const note = normalizeNotification(input)
  if (!note) return
  const alive = !!win && !win.isDestroyed()
  const window = { exists: alive, visible: alive && win.isVisible(), minimized: alive && win.isMinimized(), focused: alive && win.isFocused() }
  if (shouldShowToast({ enabled: prefs.get().notifications, window })) showToast({ title: note.title, body: note.message }, () => openIssue(note.issueId))
})

ipcMain.handle(CH_PREFS_GET, (event) => (trustedSender(event) ? prefs.get() : null))
ipcMain.handle(CH_PREFS_SET, (event, partial) => {
  if (!trustedSender(event)) return null
  const before = prefs.get()
  const after = prefs.set(partial)
  if (after.startWithWindows !== before.startWithWindows) applyStartWithWindows(after.startWithWindows)
  return after
})

// ---- Built-in server ------------------------------------------------------------------------------------------------
// The Helpdesk server, started by the app on 127.0.0.1 and connected to SQL Server by the person (see localServer.cjs). It is used when no
// company server was chosen; choosing one turns it off. A flag file remembers that it is on, so the next start brings it up first.
const CH_LOCAL_STATUS = 'desktop:local-server:status'
const CH_LOCAL_ENABLE = 'desktop:local-server:enable'
const CH_LOCAL_DISABLE = 'desktop:local-server:disable'
const CH_LOCAL_RESET = 'desktop:local-server:reset-database'
const CH_LOCAL_URL = 'desktop:local-server:url'

const localExe = app.isPackaged ? path.join(process.resourcesPath, 'api', 'KPI.API.exe') : path.join(__dirname, 'api', 'KPI.API.exe')
const localDataDir = path.join(app.getPath('userData'), 'local-server')
const localFlag = path.join(localDataDir, 'enabled')

let builtIn = null
function builtInServer() {
  if (!builtIn) builtIn = createLocalServer({ exe: localExe, dataDir: localDataDir, safeStorage })
  return builtIn
}

function localStatus() {
  return { available: fs.existsSync(localExe), enabled: fs.existsSync(localFlag), ...builtInServer().status() }
}

ipcMain.handle(CH_LOCAL_STATUS, (event) => (trustedSender(event) ? localStatus() : null))
ipcMain.handle(CH_LOCAL_ENABLE, async (event) => {
  if (!trustedSender(event)) return null
  fs.mkdirSync(localDataDir, { recursive: true })
  fs.writeFileSync(localFlag, '1')
  try { await builtInServer().start() } catch (err) { console.error('[built-in server]', err && err.message) }
  return localStatus()
})
ipcMain.handle(CH_LOCAL_DISABLE, async (event) => {
  if (!trustedSender(event)) return null
  try { fs.unlinkSync(localFlag) } catch { /* already off */ }
  await builtInServer().stop()
  return localStatus()
})
ipcMain.handle(CH_LOCAL_RESET, async (event) => {
  if (!trustedSender(event)) return null
  try { await builtInServer().resetDatabase() } catch (err) { console.error('[built-in server]', err && err.message) }
  return localStatus()
})
// Asked synchronously, because the page needs the address before it makes its first call.
ipcMain.on(CH_LOCAL_URL, (event) => { event.returnValue = trustedSender(event) && fs.existsSync(localFlag) ? builtInServer().status().url : '' })

let quittingForServer = false
app.on('before-quit', (event) => {
  quitting = true // every real quit passes here first: the tray's "ออกจากโปรแกรม", restart-to-update, the last window closing with "close to tray" off
  if (quittingForServer || !builtIn || !builtIn.status().running) return
  event.preventDefault()
  quittingForServer = true
  builtIn.stop().finally(() => app.quit())
})

app.whenReady().then(async () => {
  if (!gotLock) return // a second copy only hands over to the first and quits
  protocol.handle(SCHEME, (request) => {
    const url = new URL(request.url)
    if (url.host !== HOST) return new Response('Not found', { status: 404 })
    const file = resolveFile(url.pathname === '/' ? '/index.html' : url.pathname)
    if (!file) return new Response('Not found', { status: 404 })
    return net.fetch(pathToFileURL(file).toString())
  })
  // The built-in server comes up before the window, so the page finds its address on its first call.
  if (fs.existsSync(localFlag)) {
    try { await builtInServer().start() } catch (err) { console.error('[built-in server]', err && err.message) }
  }
  createTray()
  createWindow({ hidden: !!tray && process.argv.includes(HIDDEN_ARG) })
  setupUpdater()
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow() })
})

// A second launch shows the running app, wherever it is (also when it is hidden in the tray), unless it is only Windows starting the app at sign-in.
app.on('second-instance', (_event, argv) => {
  if (!argv.includes(HIDDEN_ARG)) showWindow()
})

app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit() })
app.on('will-quit', () => {
  if (tray) tray.destroy() // otherwise Windows leaves the icon in the tray until the mouse passes over it
  tray = null
})
