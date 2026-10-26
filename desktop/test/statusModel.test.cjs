const test = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const { describeStatus } = require('../statusModel.cjs')

const iconDir = path.join(__dirname, '..', 'build', 'status')

test('each state has its own icon and wording', () => {
  assert.deepEqual(describeStatus({ state: 'ok', count: 0 }), {
    trayIcon: 'ok.png', overlayIcon: 'ok.png', description: 'ไม่มีงานรอคุณ', tooltip: 'ไม่มีงานรอคุณ',
  })
  assert.deepEqual(describeStatus({ state: 'connecting', count: 0 }), {
    trayIcon: 'connecting.png', overlayIcon: 'connecting.png', description: 'กำลังเชื่อมต่อ…', tooltip: 'กำลังเชื่อมต่อ…',
  })
  assert.deepEqual(describeStatus({ state: 'offline', count: 0 }), {
    trayIcon: 'offline.png', overlayIcon: 'offline.png', description: 'ไม่ได้เชื่อมต่อกับเซิร์ฟเวอร์', tooltip: 'ไม่ได้เชื่อมต่อกับเซิร์ฟเวอร์',
  })
})

test('attention shows the number up to 9 and "9+" above it', () => {
  for (let n = 1; n <= 9; n++) {
    const status = describeStatus({ state: 'attention', count: n })
    assert.equal(status.trayIcon, `attention-${n}.png`)
    assert.equal(status.overlayIcon, `attention-${n}.png`)
    assert.equal(status.tooltip, `มีงานรอคุณ ${n} รายการ`)
  }
  assert.equal(describeStatus({ state: 'attention', count: 10 }).trayIcon, 'attention-9plus.png')
  assert.equal(describeStatus({ state: 'attention', count: 10 }).tooltip, 'มีงานรอคุณ 10 รายการ')
  assert.equal(describeStatus({ state: 'attention', count: 250 }).overlayIcon, 'attention-9plus.png')
})

test('the count is tidied: whole number from 0 to 9999, and only attention uses it', () => {
  assert.equal(describeStatus({ state: 'attention', count: 3.9 }).tooltip, 'มีงานรอคุณ 3 รายการ')
  assert.equal(describeStatus({ state: 'attention', count: 123456 }).tooltip, 'มีงานรอคุณ 9999 รายการ')
  assert.equal(describeStatus({ state: 'attention', count: -4 }).trayIcon, 'ok.png', 'nothing waiting is the green check')
  assert.equal(describeStatus({ state: 'attention', count: NaN }).trayIcon, 'ok.png')
  assert.equal(describeStatus({ state: 'attention', count: '5' }).trayIcon, 'ok.png', 'a string is not a number')
  assert.equal(describeStatus({ state: 'attention' }).trayIcon, 'ok.png')
  assert.equal(describeStatus({ state: 'offline', count: 7 }).trayIcon, 'offline.png')
  assert.equal(describeStatus({ state: 'connecting', count: 7 }).tooltip, 'กำลังเชื่อมต่อ…')
})

test('anything that is not one of the four states is refused', () => {
  for (const input of [undefined, null, 'ok', 42, [], {}, { state: 'busy' }, { state: 'OK' }, { state: ['ok'] }, { count: 3 }]) {
    assert.equal(describeStatus(input), null, JSON.stringify(input))
  }
})

test('a tooltip from the page is trimmed and shortened; an empty one means the standard wording', () => {
  assert.equal(describeStatus({ state: 'ok', count: 0, tooltip: '  สวัสดี  ' }).tooltip, 'สวัสดี')
  assert.equal(describeStatus({ state: 'ok', count: 0, tooltip: '  สวัสดี  ' }).description, 'ไม่มีงานรอคุณ', 'the description keeps the standard wording')
  assert.equal(describeStatus({ state: 'ok', count: 0, tooltip: 'x'.repeat(500) }).tooltip.length, 100)
  assert.equal(describeStatus({ state: 'ok', count: 0, tooltip: '   ' }).tooltip, 'ไม่มีงานรอคุณ')
  assert.equal(describeStatus({ state: 'ok', count: 0, tooltip: 123 }).tooltip, 'ไม่มีงานรอคุณ')
})

test('every icon the model can name is a 32x32 PNG in build/status', () => {
  const named = new Set()
  for (const state of ['ok', 'connecting', 'offline']) named.add(describeStatus({ state, count: 0 }).trayIcon)
  for (let n = 0; n <= 12; n++) named.add(describeStatus({ state: 'attention', count: n }).trayIcon)
  assert.equal(named.size, 13)
  for (const name of named) {
    const bytes = fs.readFileSync(path.join(iconDir, name))
    assert.equal(bytes.subarray(1, 4).toString('latin1'), 'PNG', name)
    assert.equal(bytes.readUInt32BE(16), 32, `${name} width`)
    assert.equal(bytes.readUInt32BE(20), 32, `${name} height`)
  }
})
