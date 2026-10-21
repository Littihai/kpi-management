const { app, BrowserWindow, Menu, protocol, net, shell } = require('electron')
const path = require('node:path')
const fs = require('node:fs')
const { pathToFileURL } = require('node:url')

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
    webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true },
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

app.whenReady().then(() => {
  protocol.handle(SCHEME, (request) => {
    const url = new URL(request.url)
    if (url.host !== HOST) return new Response('Not found', { status: 404 })
    const file = resolveFile(url.pathname === '/' ? '/index.html' : url.pathname)
    if (!file) return new Response('Not found', { status: 404 })
    return net.fetch(pathToFileURL(file).toString())
  })
  createWindow()
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow() })
})

app.on('second-instance', () => {
  if (win) {
    if (win.isMinimized()) win.restore()
    win.focus()
  }
})

app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit() })
