import type { Citation } from '../api/client'
import { useT } from '../i18n'

const UUID_SOURCE = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const UUID = new RegExp(UUID_SOURCE, 'gi')

// A bracketed group holding one or more chunk ids — `[id]`, `[id, id]`, `[id][id]` all occur — or a
// bare id the model wrote without brackets.
const MARKER = new RegExp(`\\[[^\\]]*?${UUID_SOURCE}[^\\]]*?\\]|${UUID_SOURCE}`, 'gi')

/**
 * The model's narrative, with its citations rendered as numbers instead of raw identifiers.
 *
 * Citations come back as chunk **ids**, not quotations, so that an invented reference can be caught
 * and dropped — an unverifiable citation is worse than none, because it looks like evidence. That is
 * the right wire format and the wrong thing to show a human: it put 36-character GUIDs in the middle
 * of sentences somebody is meant to read. Here each id becomes the same number as its chip below, and
 * clicking it opens the passage that id points at.
 */
export function Reasoning({
  text,
  citations,
  openCitation,
  onToggleCitation,
}: {
  text: string
  citations: Citation[]
  openCitation: string | null
  onToggleCitation: (chunkId: string) => void
}) {
  const t = useT()

  const numbers = new Map(citations.map((citation, index) => [citation.chunkId, index + 1]))
  const sections = new Map(citations.map((citation) => [citation.chunkId, citation.section ?? '']))

  const parts: Array<string | { ids: string[] }> = []
  let cursor = 0

  for (const match of text.matchAll(MARKER)) {
    const ids = (match[0].match(UUID) ?? [])
      .map((id) => id.toLowerCase())
      // Ids the service could not verify were dropped before storage; a reference to one resolves to
      // nothing, so it is removed rather than left as a dangling marker.
      .filter((id) => numbers.has(id))

    if (match.index > cursor) parts.push(text.slice(cursor, match.index))
    if (ids.length > 0) parts.push({ ids })
    cursor = match.index + match[0].length
  }

  if (cursor < text.length) parts.push(text.slice(cursor))

  return (
    <p className="reasoning">
      {parts.map((part, index) =>
        typeof part === 'string' ? (
          <span key={index}>{part}</span>
        ) : (
          part.ids.map((id) => (
            <button
              key={id}
              type="button"
              className={`cite-marker${openCitation === id ? ' cite-open' : ''}`}
              aria-expanded={openCitation === id}
              aria-label={t('card.citationAria', { n: numbers.get(id)!, section: sections.get(id) ?? '' })}
              onClick={() => onToggleCitation(id)}
            >
              {numbers.get(id)}
            </button>
          ))
        ),
      )}
    </p>
  )
}
