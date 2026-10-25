// The sign-in a person asked this computer to remember ("remember my password" on the login page).
//
// Nothing is kept in plain text and nothing leaves the computer: the whole file is encrypted with Electron's safeStorage,
// which on Windows uses DPAPI, so only the same Windows user on the same machine can read it. One entry per server
// address, because the same email can be a different account on another company's server. If the operating system
// cannot encrypt (safeStorage unavailable) the feature is simply off; the password is never written unencrypted.
const fs = require('node:fs')
const path = require('node:path')

const MAX = { server: 2048, email: 320, password: 1024 }

function text(value, max) {
  return typeof value === 'string' && value.length > 0 && value.length <= max ? value : null
}

function createCredentialStore({ safeStorage, file }) {
  const available = () => {
    try { return !!safeStorage.isEncryptionAvailable() } catch { return false }
  }

  // An unreadable file (another Windows user's, damaged, or from an older format) counts as "nothing remembered".
  function readAll() {
    try {
      const data = JSON.parse(safeStorage.decryptString(fs.readFileSync(file)))
      return data && typeof data === 'object' && !Array.isArray(data) ? data : {}
    } catch { return {} }
  }

  function writeAll(data) {
    if (Object.keys(data).length === 0) {
      try { fs.unlinkSync(file) } catch { /* already gone */ }
      return
    }
    fs.mkdirSync(path.dirname(file), { recursive: true })
    const temp = `${file}.tmp`
    fs.writeFileSync(temp, safeStorage.encryptString(JSON.stringify(data)), { mode: 0o600 })
    fs.renameSync(temp, file) // never leave a half-written file behind
  }

  return {
    available,

    /** { email, password } remembered for this server, or null. */
    get(serverArg) {
      const server = text(serverArg, MAX.server)
      if (!server || !available()) return null
      const entry = readAll()[server]
      if (!entry || typeof entry.email !== 'string' || typeof entry.password !== 'string') return null
      return { email: entry.email, password: entry.password }
    },

    save(serverArg, emailArg, passwordArg) {
      const server = text(serverArg, MAX.server)
      const email = text(emailArg, MAX.email)
      const password = text(passwordArg, MAX.password)
      if (!server || !email || !password || !available()) return false
      try {
        writeAll({ ...readAll(), [server]: { email, password } })
        return true
      } catch { return false }
    },

    /** After the person changes their password: keep what is remembered current, but only for the same account. */
    updatePassword(serverArg, emailArg, passwordArg) {
      const server = text(serverArg, MAX.server)
      const email = text(emailArg, MAX.email)
      const password = text(passwordArg, MAX.password)
      if (!server || !email || !password || !available()) return false
      const all = readAll()
      const entry = all[server]
      if (!entry || String(entry.email).toLowerCase() !== email.toLowerCase()) return false
      try {
        writeAll({ ...all, [server]: { email: entry.email, password } })
        return true
      } catch { return false }
    },

    forget(serverArg) {
      const server = text(serverArg, MAX.server)
      if (!server) return false
      const all = readAll()
      if (!(server in all)) return true
      delete all[server]
      try { writeAll(all); return true } catch { return false }
    },
  }
}

module.exports = { createCredentialStore }
