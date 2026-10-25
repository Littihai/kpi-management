const test = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const http = require('node:http')
const os = require('node:os')
const path = require('node:path')
const { createLocalServer } = require('../localServer.cjs')

const fake = path.join(__dirname, 'fake-server.cjs')
const folder = () => fs.mkdtempSync(path.join(os.tmpdir(), 'hd-local-'))
const make = (options = {}) => createLocalServer({ exe: process.execPath, args: [fake], dataDir: folder(), startTimeoutMs: 15000, ...options })
const getJson = (url) => new Promise((resolve, reject) => http.get(url, (r) => { let b = ''; r.on('data', (c) => (b += c)); r.on('end', () => resolve(JSON.parse(b))) }).on('error', reject))
const reachable = (url) => new Promise((resolve) => http.get(`${url}/health`, { timeout: 1000 }, (r) => { r.resume(); resolve(true) }).on('error', () => resolve(false)))
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

test('starts on loopback and a free port with its settings in the data folder, then stops', async () => {
  const server = make()
  const started = await server.start()
  try {
    assert.equal(started.running, true)
    assert.match(started.url, /^http:\/\/127\.0\.0\.1:\d+$/)
    const env = await getJson(`${started.url}/env`)
    assert.equal(env.configFile, server.configFile)
    assert.equal(path.dirname(env.configFile), path.dirname(server.logFile))
    assert.ok(env.keyLength >= 32, 'a long enough signing key')
    assert.equal(env.parent, String(process.pid), 'tells the server which process to follow')
    assert.equal(env.environment, 'Production')
  } finally {
    await server.stop()
  }
  assert.equal(server.status().running, false)
  assert.equal(await reachable(started.url), false)
})

test('asking to start again does not start a second one', async () => {
  const server = make()
  try {
    const [a, b] = await Promise.all([server.start(), server.start()])
    const c = await server.start()
    assert.equal(a.url, b.url)
    assert.equal(a.url, c.url)
  } finally { await server.stop() }
})

test('the signing key is made once and kept between runs', async () => {
  const dataDir = folder()
  const first = make({ dataDir })
  const key1 = (await getJson(`${(await first.start()).url}/env`)).key
  await first.stop()
  const second = make({ dataDir })
  const key2 = (await getJson(`${(await second.start()).url}/env`)).key
  await second.stop()
  assert.equal(key1, key2)
})

test('the signing key is encrypted with the Windows account when that is possible', async () => {
  const dataDir = folder()
  const safeStorage = {
    isEncryptionAvailable: () => true,
    encryptString: (s) => Buffer.from('ENC:' + Buffer.from(s).reverse().toString('latin1'), 'latin1'),
    decryptString: (b) => Buffer.from(b.toString('latin1').slice(4), 'latin1').reverse().toString(),
  }
  const server = make({ dataDir, safeStorage })
  const key = (await getJson(`${(await server.start()).url}/env`)).key
  await server.stop()
  assert.ok(!fs.existsSync(path.join(dataDir, 'jwt.key')), 'no plain copy')
  assert.ok(!fs.readFileSync(path.join(dataDir, 'jwt.key.bin')).includes(Buffer.from(key)), 'not readable in the file')
  const again = make({ dataDir, safeStorage })
  assert.equal((await getJson(`${(await again.start()).url}/env`)).key, key)
  await again.stop()
})

test('a missing program is reported, not thrown at the user', async () => {
  const server = make({ exe: path.join(os.tmpdir(), 'no-such-server.exe') })
  await assert.rejects(server.start(), /not part of this installation/)
  assert.equal(server.status().error, 'missing')
  assert.equal(server.status().running, false)
})

test('a server that stops while starting is reported quickly', async () => {
  const server = make({ extraEnv: { FAKE_MODE: 'exit' } })
  const began = Date.now()
  await assert.rejects(server.start(), /stopped while starting/)
  assert.ok(Date.now() - began < 10000)
  assert.equal(server.status().running, false)
})

test('a server that never answers is stopped after the time allowed', async () => {
  const server = make({ extraEnv: { FAKE_MODE: 'silent' }, startTimeoutMs: 1500 })
  await assert.rejects(server.start(), /did not answer in time/)
  assert.equal(server.status().error, 'timeout')
  assert.equal(server.status().running, false)
})

test('disconnecting forgets the database settings and starts again with a new process', async () => {
  const server = make()
  const first = await server.start()
  const pidBefore = (await getJson(`${first.url}/env`)).pid
  fs.writeFileSync(server.configFile, '{"Database":{"Server":"srv"}}')
  fs.writeFileSync(path.join(path.dirname(server.configFile), 'setup-key.txt'), 'key')
  try {
    const again = await server.resetDatabase()
    assert.equal(fs.existsSync(server.configFile), false)
    assert.equal(fs.existsSync(path.join(path.dirname(server.configFile), 'setup-key.txt')), false)
    assert.equal(again.running, true)
    assert.notEqual((await getJson(`${again.url}/env`)).pid, pidBefore)
  } finally { await server.stop() }
})

test('stopping something that is not running is fine', async () => {
  await make().stop()
})

test('after the server dies by itself the status says so', async () => {
  const server = make()
  const { url } = await server.start()
  const { pid } = await getJson(`${url}/env`)
  process.kill(pid)
  for (let i = 0; i < 40 && server.status().running; i++) await sleep(100)
  assert.equal(server.status().running, false)
  assert.match(server.status().error, /stopped/)
})
