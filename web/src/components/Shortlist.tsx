import { useEffect, useState } from 'react'
import { api, type ShortlistItem } from '../api/client'
import { useT, type Translate } from '../i18n'

const LABELS: Record<string, string> = { GO: 'GO', NO_GO: 'NO-GO', INVESTIGATE: 'INVESTIGATE' }

/**
 * What the reviewer said yes to and can still act on.
 *
 * Approving used to be the end of the road: a row was written, the card left the queue, and the
 * reviewer was on their own to go and find the tender again. This is the other end — still-open
 * notices somebody backed, soonest deadline first, each linking to where bids are submitted.
 */
export function Shortlist() {
  const [items, setItems] = useState<ShortlistItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const t = useT()

  useEffect(() => {
    api
      .shortlist()
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }, [])

  if (error) return <p className="error">{error}</p>
  if (!items) return <p className="muted">{t('common.loading')}</p>

  if (items.length === 0)
    return (
      <main>
        <p className="muted">{t('shortlist.empty')}</p>
      </main>
    )

  return (
    <main>
      <p className="muted">{t('shortlist.summary', { n: items.length })}</p>

      {items.map((item) => (
        <ShortlistRow key={item.assessmentId} item={item} t={t} />
      ))}
    </main>
  )
}

function ShortlistRow({ item, t }: { item: ShortlistItem; t: Translate }) {
  // Under a week is where a bid stops being a plan and starts being a scramble.
  const urgent = item.daysLeft != null && item.daysLeft <= 7

  return (
    <article className={`shortlist-row${urgent ? ' shortlist-urgent' : ''}`}>
      <div className="shortlist-when">
        {item.daysLeft != null ? (
          <>
            <strong>{item.daysLeft}</strong>
            <span className="muted">
              {item.daysLeft === 1 ? t('shortlist.dayLeft') : t('shortlist.daysLeft')}
            </span>
          </>
        ) : (
          <span className="muted">{t('shortlist.noDeadline')}</span>
        )}
      </div>

      <div className="shortlist-what">
        <h3>{item.noticeTitle ?? item.noticeId}</h3>
        <p className="muted">
          {item.buyerName ?? t('card.noBuyer')} · <code>{item.noticeId}</code>
          {item.submissionDeadline && ` · ${t('assess.closes')} ${item.submissionDeadline.slice(0, 10)}`}
          {item.estimatedValue != null &&
            ` · ${item.estimatedValue.toLocaleString('fi-FI')} ${item.currency ?? ''}`.trimEnd()}
        </p>
        <p className="muted">
          <span className={`verdict verdict-${item.standing.toLowerCase()}`}>
            {LABELS[item.standing] ?? item.standing}
          </span>{' '}
          · {t('shortlist.scored', { score: item.deterministicScore })} · {t('shortlist.backedBy')}{' '}
          <strong>{item.reviewedBy || t('decided.unattributed')}</strong>
        </p>
        {item.reviewerNote && <blockquote className="note">{item.reviewerNote}</blockquote>}
      </div>

      <div className="shortlist-act">
        {item.procurementDocumentsUrl ? (
          <a
            className="cta"
            href={item.procurementDocumentsUrl}
            target="_blank"
            rel="noopener noreferrer"
          >
            {t('shortlist.documents')}
          </a>
        ) : (
          // Legacy notices carry no documents link. Saying so beats linking somewhere invented.
          <span className="muted">{t('shortlist.noLink')}</span>
        )}
      </div>
    </article>
  )
}
