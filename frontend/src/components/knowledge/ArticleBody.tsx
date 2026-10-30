import { Fragment } from 'react'
import { linkify, parseBlocks } from '../../lib/articleText'

function Line({ text }: { text: string }) {
  return (
    <>
      {linkify(text).map((part, i) => (part.href
        ? <a key={i} href={part.href} target="_blank" rel="noopener noreferrer" className="break-all text-accent underline-offset-2 hover:underline">{part.text}</a>
        : <Fragment key={i}>{part.text}</Fragment>))}
    </>
  )
}

/** An article's text. Everything is drawn as React text nodes (never as HTML) and only http/https addresses become links. */
export default function ArticleBody({ body }: { body: string }) {
  return (
    <div className="space-y-4 break-words text-[14.5px] leading-relaxed text-ink">
      {parseBlocks(body).map((block, i) => {
        switch (block.kind) {
          case 'heading':
            return <h2 key={i} className="pt-2 text-base font-semibold text-ink">{block.text}</h2>
          case 'bullets':
            return <ul key={i} className="list-disc space-y-1 pl-6">{block.items.map((item, j) => <li key={j}><Line text={item} /></li>)}</ul>
          case 'steps':
            return <ol key={i} className="list-decimal space-y-1 pl-6">{block.items.map((item, j) => <li key={j}><Line text={item} /></li>)}</ol>
          default:
            return (
              <p key={i}>
                {block.lines.map((line, j) => <Fragment key={j}>{j > 0 && <br />}<Line text={line} /></Fragment>)}
              </p>
            )
        }
      })}
    </div>
  )
}
