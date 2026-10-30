/**
 * An article body is plain text. This turns it into a few block kinds the page draws with ordinary React elements, so
 * nothing the author types is ever treated as markup: a "<script>" is shown as the letters it is.
 */

export type ArticleBlock =
  | { kind: 'heading'; text: string }
  | { kind: 'paragraph'; lines: string[] }
  | { kind: 'bullets'; items: string[] }
  | { kind: 'steps'; items: string[] }

const HEADING = /^#{1,3}\s+(.*\S)\s*$/
const BULLET = /^\s*[-*•]\s+(.*\S)\s*$/
const STEP = /^\s*\d{1,3}[.)]\s+(.*\S)\s*$/

/** Blank lines separate paragraphs; "# title" is a heading, "- item" a bullet, "1. step" a numbered step. */
export function parseBlocks(body: string): ArticleBlock[] {
  const blocks: ArticleBlock[] = []
  for (const raw of body.split(/\r?\n/)) {
    const line = raw.trimEnd()
    if (!line.trim()) { blocks.push({ kind: 'paragraph', lines: [] }); continue }

    const heading = HEADING.exec(line)
    if (heading) { blocks.push({ kind: 'heading', text: heading[1] }); continue }

    const bullet = BULLET.exec(line)
    const step = bullet ? null : STEP.exec(line)
    const last = blocks[blocks.length - 1]
    if (bullet) {
      if (last?.kind === 'bullets') last.items.push(bullet[1])
      else blocks.push({ kind: 'bullets', items: [bullet[1]] })
    } else if (step) {
      if (last?.kind === 'steps') last.items.push(step[1])
      else blocks.push({ kind: 'steps', items: [step[1]] })
    } else if (last?.kind === 'paragraph') {
      // also reuses the empty paragraph a blank line left behind
      last.lines.push(line.trim())
    } else {
      blocks.push({ kind: 'paragraph', lines: [line.trim()] })
    }
  }
  return blocks.filter((b) => b.kind !== 'paragraph' || b.lines.length > 0)
}

export type TextPart = { text: string; href?: string }

const URL_PATTERN = /https?:\/\/[^\s<>"']+/gi
const TRAILING = /[.,;:!?)\]}]+$/

/** Splits a line into plain text and links. Only http and https addresses become links. */
export function linkify(text: string): TextPart[] {
  const parts: TextPart[] = []
  let cursor = 0
  for (const match of text.matchAll(URL_PATTERN)) {
    const start = match.index ?? 0
    const url = match[0].replace(TRAILING, '')
    if (!isWebAddress(url)) continue
    if (start > cursor) parts.push({ text: text.slice(cursor, start) })
    parts.push({ text: url, href: url })
    cursor = start + url.length
  }
  if (cursor < text.length) parts.push({ text: text.slice(cursor) })
  return parts
}

function isWebAddress(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}
