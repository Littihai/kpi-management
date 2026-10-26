import { badgeText, type AttentionStatus } from './attention'

// The same colours as the --tone-*-solid tokens in styles/tokens.css (identical in light and dark), grey for not connected.
const COLOURS = { ok: '#2fb344', attention: '#d63939', connecting: '#f59f00', offline: '#8a94a6' } as const
const SIZE = 64

let original: { href: string; type: string } | null = null

const iconLink = () => document.querySelector<HTMLLinkElement>('link[rel~="icon"]')

/** Swaps the tab's icon for a circle in the status colour: a check when nothing waits, the number when something does. */
export function setTabIcon({ state, count }: AttentionStatus) {
  const link = iconLink()
  const canvas = document.createElement('canvas')
  const ctx = canvas.getContext('2d')
  if (!link || !ctx) return
  original ??= { href: link.getAttribute('href') ?? '', type: link.type }

  canvas.width = canvas.height = SIZE
  ctx.fillStyle = COLOURS[state]
  ctx.beginPath()
  ctx.arc(SIZE / 2, SIZE / 2, SIZE / 2 - 2, 0, Math.PI * 2)
  ctx.fill()

  ctx.fillStyle = ctx.strokeStyle = '#fff'
  if (state === 'ok') {
    ctx.lineWidth = 7
    ctx.lineCap = ctx.lineJoin = 'round'
    ctx.beginPath()
    ctx.moveTo(19, 33)
    ctx.lineTo(28, 42)
    ctx.lineTo(46, 23)
    ctx.stroke()
  } else if (state === 'attention') {
    const text = badgeText(count)
    ctx.font = `700 ${text.length > 1 ? 34 : 42}px system-ui, sans-serif`
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(text, SIZE / 2, SIZE / 2 + 2)
  }

  link.type = 'image/png'
  link.href = canvas.toDataURL('image/png')
}

/** Puts the page's own icon back. */
export function restoreTabIcon() {
  const link = iconLink()
  if (!link || !original) return
  link.type = original.type
  link.setAttribute('href', original.href)
  original = null
}
