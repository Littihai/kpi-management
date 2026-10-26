// The person's choices for the desktop app, kept as a small JSON file in the user data folder (no Electron in here, so it can be tested).
//   notifications     show a Windows notification when something concerns them and the app is not in front
//   closeToTray       the window's close button hides the app to the system tray instead of quitting
//   startWithWindows  start with Windows, hidden in the tray
// An unreadable file counts as "nothing chosen yet"; only these three yes/no values are ever read or written.
const fs = require('node:fs')
const path = require('node:path')

const DEFAULTS = Object.freeze({ notifications: true, closeToTray: true, startWithWindows: false })

function createPrefsStore({ file }) {
  const pick = (source, base) => {
    const next = { ...base }
    if (source && typeof source === 'object') {
      for (const key of Object.keys(DEFAULTS)) if (typeof source[key] === 'boolean') next[key] = source[key]
    }
    return next
  }

  let current = { ...DEFAULTS }
  try { current = pick(JSON.parse(fs.readFileSync(file, 'utf8')), DEFAULTS) } catch { /* missing or damaged: defaults */ }

  return {
    get: () => ({ ...current }),
    /** Applies the yes/no values of `partial` that belong to a known choice, saves them, and returns all the choices. */
    set(partial) {
      const next = pick(partial, current)
      if (Object.keys(DEFAULTS).some((key) => next[key] !== current[key])) {
        current = next
        try {
          fs.mkdirSync(path.dirname(file), { recursive: true })
          const temp = `${file}.tmp`
          fs.writeFileSync(temp, JSON.stringify(current, null, 2))
          fs.renameSync(temp, file) // never leave a half-written file behind
        } catch (err) {
          console.error('[prefs] could not save:', err && err.message) // the choice still applies until the app quits
        }
      }
      return { ...current }
    },
  }
}

module.exports = { createPrefsStore, DEFAULTS }
