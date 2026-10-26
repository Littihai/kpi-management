const test = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const os = require('node:os')
const path = require('node:path')
const { createPrefsStore, DEFAULTS } = require('../prefs.cjs')

const folder = () => fs.mkdtempSync(path.join(os.tmpdir(), 'hd-prefs-'))
const fileIn = (dir) => path.join(dir, 'desktop-prefs.json')

test('the defaults are notifications on, close to tray on, start with Windows off', () => {
  assert.deepEqual({ ...DEFAULTS }, { notifications: true, closeToTray: true, startWithWindows: false })
  assert.deepEqual(createPrefsStore({ file: fileIn(folder()) }).get(), { ...DEFAULTS })
})

test('a choice is saved and found again by the next start', () => {
  const file = fileIn(folder())
  const first = createPrefsStore({ file })
  assert.deepEqual(first.set({ closeToTray: false }), { notifications: true, closeToTray: false, startWithWindows: false })
  assert.deepEqual(first.set({ startWithWindows: true, notifications: false }), { notifications: false, closeToTray: false, startWithWindows: true })
  assert.deepEqual(createPrefsStore({ file }).get(), { notifications: false, closeToTray: false, startWithWindows: true })
  assert.deepEqual(fs.readdirSync(path.dirname(file)), ['desktop-prefs.json'], 'no temporary file left behind')
})

test('only the three yes/no choices are accepted', () => {
  const file = fileIn(folder())
  const store = createPrefsStore({ file })
  assert.deepEqual(store.set({ notifications: 'false', closeToTray: 0, startWithWindows: null, other: true }), { ...DEFAULTS })
  assert.equal(fs.existsSync(file), false, 'nothing changed, nothing written')
  for (const junk of [undefined, null, 'x', 5, []]) assert.deepEqual(store.set(junk), { ...DEFAULTS })
  store.set({ closeToTray: false, other: 'x' })
  assert.deepEqual(Object.keys(JSON.parse(fs.readFileSync(file, 'utf8'))).sort(), ['closeToTray', 'notifications', 'startWithWindows'])
})

test('a damaged or partial file falls back to the defaults for what it cannot give', () => {
  const dir = folder()
  fs.writeFileSync(fileIn(dir), '{ not json')
  assert.deepEqual(createPrefsStore({ file: fileIn(dir) }).get(), { ...DEFAULTS })
  fs.writeFileSync(fileIn(dir), JSON.stringify({ closeToTray: false, notifications: 'yes', extra: 1 }))
  assert.deepEqual(createPrefsStore({ file: fileIn(dir) }).get(), { notifications: true, closeToTray: false, startWithWindows: false })
  fs.writeFileSync(fileIn(dir), '[]')
  assert.deepEqual(createPrefsStore({ file: fileIn(dir) }).get(), { ...DEFAULTS })
})

test('the answer is a copy, so callers cannot change the saved choices by accident', () => {
  const store = createPrefsStore({ file: fileIn(folder()) })
  store.get().notifications = false
  assert.equal(store.get().notifications, true)
})

test('when the file cannot be written the choice still applies for this run', () => {
  const dir = folder()
  const blocker = path.join(dir, 'blocker')
  fs.writeFileSync(blocker, 'a file where a folder is needed')
  const store = createPrefsStore({ file: path.join(blocker, 'desktop-prefs.json') })
  const log = console.error
  console.error = () => {}
  try {
    assert.equal(store.set({ notifications: false }).notifications, false)
  } finally {
    console.error = log
  }
  assert.equal(store.get().notifications, false)
})
