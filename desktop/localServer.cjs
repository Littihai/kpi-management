// The built-in server: the Windows app starts the Helpdesk server (the same program a company installs as a Windows service) as its
// own child process, on 127.0.0.1 and a free port, and connects it to SQL Server with the details the person types into the
// connect form. Nothing else listens, nothing leaves the PC except the server's own connection to SQL Server.
//
// What it keeps, under the app's data folder (never inside the install folder, so an update leaves it alone):
//   database.json   where the database is and how to sign in (written by the server's setup page; the password is encrypted)
//   jwt.key(.bin)   the key the server signs tokens with and encrypts that password with; encrypted with the Windows account when possible
//   server.log      what the server wrote
const { spawn } = require('node:child_process')
const crypto = require('node:crypto')
const fs = require('node:fs')
const http = require('node:http')
const net = require('node:net')
const path = require('node:path')

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
const MAX_LOG_BYTES = 5 * 1024 * 1024

function freePort() {
  return new Promise((resolve, reject) => {
    const probe = net.createServer()
    probe.once('error', reject)
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address()
      probe.close(() => resolve(port))
    })
  })
}

/** Does something answer at /health? (The setup page answers 200 too: the server is up, only the database is not chosen.) */
function answers(base, timeoutMs = 1500) {
  return new Promise((resolve) => {
    const request = http.get(`${base}/health`, { timeout: timeoutMs }, (response) => { response.resume(); resolve(response.statusCode < 500) })
    request.on('timeout', () => { request.destroy(); resolve(false) })
    request.on('error', () => resolve(false))
  })
}

function createLocalServer({ exe, args = [], cwd, dataDir, safeStorage = null, startTimeoutMs = 60000, extraEnv = {}, spawnImpl = spawn }) {
  const configFile = path.join(dataDir, 'database.json')
  const logFile = path.join(dataDir, 'server.log')
  let child = null
  let url = ''
  let error = ''
  let stopping = false
  let starting = null

  const canEncrypt = () => { try { return !!safeStorage && safeStorage.isEncryptionAvailable() } catch { return false } }

  /** One random key per PC, kept between runs: the server encrypts the database password with it. */
  function jwtKey() {
    const plain = path.join(dataDir, 'jwt.key')
    const sealed = path.join(dataDir, 'jwt.key.bin')
    try {
      if (fs.existsSync(sealed) && canEncrypt()) return safeStorage.decryptString(fs.readFileSync(sealed))
      if (fs.existsSync(plain)) return fs.readFileSync(plain, 'utf8').trim()
    } catch { /* unreadable (another Windows user's, damaged): a new key; the database password is asked for again */ }
    const key = crypto.randomBytes(48).toString('base64')
    if (canEncrypt()) fs.writeFileSync(sealed, safeStorage.encryptString(key), { mode: 0o600 })
    else fs.writeFileSync(plain, key, { mode: 0o600 })
    return key
  }

  function openLog() {
    try { if (fs.statSync(logFile).size > MAX_LOG_BYTES) fs.truncateSync(logFile, 0) } catch { /* no log yet */ }
    return fs.openSync(logFile, 'a')
  }

  const status = () => ({ running: url !== '', url, error })

  async function launch() {
    error = ''
    if (!exe || !fs.existsSync(exe)) { error = 'missing'; throw new Error('the built-in server is not part of this installation') }
    fs.mkdirSync(dataDir, { recursive: true })
    const port = await freePort()
    const base = `http://127.0.0.1:${port}`
    const log = openLog()
    const env = {
      ...process.env,
      ...extraEnv,
      ASPNETCORE_URLS: base,
      ASPNETCORE_ENVIRONMENT: 'Production',
      Setup__ConfigFile: configFile,
      Jwt__Key: jwtKey(),
      HELPDESK_PARENT_PID: String(process.pid),   // the server leaves when this process is gone, however that happens
      Logging__LogLevel__Default: 'Warning',
    }
    const proc = spawnImpl(exe, args, { cwd: cwd || path.dirname(exe), env, windowsHide: true, stdio: ['ignore', log, log] })
    fs.closeSync(log)   // the child has its own copy
    child = proc
    proc.on('error', (e) => { error = e.message })
    proc.on('exit', (code) => {
      if (child !== proc) return
      child = null
      url = ''
      if (!stopping) error = `stopped (${code})`
    })

    const deadline = Date.now() + startTimeoutMs
    while (Date.now() < deadline) {
      if (child !== proc) throw new Error(`the built-in server stopped while starting: ${error}`)
      if (await answers(base)) { url = base; return status() }
      await sleep(250)
    }
    await stop()
    error = 'timeout'
    throw new Error('the built-in server did not answer in time')
  }

  /** Starts it if it is not running; asking again while it starts waits for the same start. */
  function start() {
    if (url) return Promise.resolve(status())
    if (!starting) starting = launch().finally(() => { starting = null })
    return starting
  }

  async function stop() {
    const proc = child
    if (!proc) return
    stopping = true
    await new Promise((resolve) => {
      const timer = setTimeout(resolve, 5000)
      proc.once('exit', () => { clearTimeout(timer); resolve() })
      proc.kill()
    })
    if (child === proc) { child = null; url = '' }
    stopping = false
  }

  /** "Disconnect": forget where the database is on this PC (the data in it is untouched) and start again, waiting to be connected. */
  async function resetDatabase() {
    await stop()
    for (const file of [configFile, path.join(dataDir, 'setup-key.txt')]) { try { fs.unlinkSync(file) } catch { /* nothing to forget */ } }
    return start()
  }

  return { start, stop, resetDatabase, status, configFile, logFile }
}

module.exports = { createLocalServer }
