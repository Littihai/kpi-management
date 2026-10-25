const { app, BrowserWindow, Menu, protocol, net, shell, ipcMain, safeStorage } = require('electron')
const path = require('node:path')
const fs = require('node:fs')
const { pathToFileURL } = require('node:url')
const { createCredentialStore } = require('./credentials.cjs')

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

protocol.registerSchemesAsPrivileged([
  { scheme: SCHEME, privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } },
])

if (!app.requestSingleInstanceLock()) {
  app.quit()
}

let win = null

function resolveFile(urlPath) {
  const clean = path.normalize(decodeURIComponent(urlPath)).split(path.sep).filter(Boolean).join(path.sep)
  const file = path.join(WEB_ROOT, clean)
  if (!file.startsWith(WEB_ROOT)) return null           // never serve anything outside the web build
  if (fs.existsSync(file) && fs.statSync(file).isFile()) return file
  return path.extname(clean) ? null : path.join(WEB_ROOT, 'index.html') // client-side routes fall back to the app
}

function createWindow() {
  win = new BrowserWindow({
    width: 1360,
    height: 860,
    minWidth: 960,
    minHeight: 600,
    backgroundColor: '#f6f8fb',
    title: 'Helpdesk',
    icon: path.join(__dirname, 'build', 'icon.png'),
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
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

function trustedSender(event) {
  try { return new URL(event.senderFrame.url).origin === `${SCHEME}://${HOST}` } catch { return false }
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

app.whenReady().then(() => {
  protocol.handle(SCHEME, (request) => {
    const url = new URL(request.url)
    if (url.host !== HOST) return new Response('Not found', { status: 404 })
    const file = resolveFile(url.pathname === '/' ? '/index.html' : url.pathname)
    if (!file) return new Response('Not found', { status: 404 })
    return net.fetch(pathToFileURL(file).toString())
  })
  createWindow()
  setupUpdater()
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow() })
})

app.on('second-instance', () => {
  if (win) {
    if (win.isMinimized()) win.restore()
    win.focus()
  }
})

app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit() })
