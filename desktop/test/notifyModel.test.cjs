const test = require('node:test')
const assert = require('node:assert/strict')
const { normalizeNotification, shouldShowToast } = require('../notifyModel.cjs')

const GUID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301'

test('a notification keeps its title, message and issue', () => {
  assert.deepEqual(
    normalizeNotification({ title: '  มีงานใหม่  ', message: ' เครื่องพิมพ์เสีย ', issueId: GUID }),
    { title: 'มีงานใหม่', message: 'เครื่องพิมพ์เสีย', issueId: GUID },
  )
  assert.deepEqual(normalizeNotification({ title: 'a', message: 'b', issueId: null }), { title: 'a', message: 'b', issueId: null })
})

test('text is shortened and a missing title becomes Helpdesk', () => {
  const long = normalizeNotification({ title: 't'.repeat(500), message: 'm'.repeat(500), issueId: null })
  assert.equal(long.title.length, 100)
  assert.equal(long.message.length, 250)
  assert.deepEqual(normalizeNotification({ message: 'ข้อความ' }), { title: 'Helpdesk', message: 'ข้อความ', issueId: null })
  assert.deepEqual(normalizeNotification({ title: 'เรื่อง' }), { title: 'เรื่อง', message: '', issueId: null })
})

test('nothing to say means no notification', () => {
  for (const input of [undefined, null, 'x', 5, {}, { title: '  ', message: '' }, { title: 1, message: 2 }]) {
    assert.equal(normalizeNotification(input), null, JSON.stringify(input))
  }
})

test('only an id that looks like an issue id is passed on', () => {
  for (const issueId of ['../admin', 'a/b', '', ' ', 'x'.repeat(65), 'id with space', '<script>', 12, {}, ['a']]) {
    assert.equal(normalizeNotification({ title: 'a', issueId }).issueId, null, JSON.stringify(issueId))
  }
  assert.equal(normalizeNotification({ title: 'a', issueId: `  ${GUID} ` }).issueId, GUID)
})

test('a toast only appears when the person is not looking at the app and has not turned it off', () => {
  const front = { exists: true, visible: true, minimized: false, focused: true }
  assert.equal(shouldShowToast({ enabled: true, window: front }), false, 'in front')
  assert.equal(shouldShowToast({ enabled: true, window: { ...front, focused: false } }), true, 'in the background')
  assert.equal(shouldShowToast({ enabled: true, window: { ...front, minimized: true } }), true, 'minimised')
  assert.equal(shouldShowToast({ enabled: true, window: { ...front, visible: false, focused: false } }), true, 'hidden in the tray')
  assert.equal(shouldShowToast({ enabled: true, window: { exists: false, visible: false, minimized: false, focused: false } }), true, 'no window')
  assert.equal(shouldShowToast({ enabled: false, window: { ...front, focused: false } }), false, 'turned off')
  assert.equal(shouldShowToast({ enabled: false, window: front }), false)
})
