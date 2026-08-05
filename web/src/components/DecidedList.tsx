import { useCallback, useEffect, useState } from 'react'
import { api, type ApprovalDecision, type DecidedItem } from '../api/client'
import { useT, type Translate } from '../i18n'

const LABELS: Record<string, string> = { GO: 'GO', NO_GO: 'NO-GO', INVESTIGATE: 'INVESTIGATE' }

function Verdict({ value }: { value: string }) {
  return <span className={`verdict verdict-${value.toLowerCase()}`}>{LABELS[value] ?? value}</span>
}

function when(iso: string) {
  return new Date(iso).toLocaleString('fi-FI', { dateStyle: 'short', timeStyle: 'short' })
}

/**
 * Decisions already recorded.
 *
 * This is the read side of the append-only audit trail. Without it the queue drops an assessment the
 * moment it is decided and the reviewer's note — the most useful column there is — becomes
 * unreachable. Revisiting writes a *new* decision rather than editing the old one, so the earlier
 * verdict stays visible underneath.
 */
export function DecidedList({ reviewer, onChanged }: { reviewer: string; onChanged: () => void }) {
  const [items, setItems] = useState<DecidedItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const t = useT()

  const load = useCallback(async () => {
    try {
      setItems(await api.decided())
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // A revision has to reload *this* list, not just the queue: the parent's refresh only touches the
  // queue and the metrics, so without this the card keeps showing the verdict you just replaced.
  const handleChanged = useCallback(async () => {
    await load()
    onChanged()
  }, [load, onChanged])

  if (error) return <p className="error">{error}</p>
  if (!items) return <p className="muted">{t('common.loading')}</p>
  if (items.length === 0)
    return <p className="muted">{t('decided.empty')}</p>

  return (
    <main>
      <p className="muted">{t('decided.summary', { n: items.length })}</p>
      {items.map((item) => (
        <DecidedCard key={item.id} item={item} reviewer={reviewer} onChanged={handleChanged} t={t} />
      ))}
    </main>
  )
}

function DecidedCard({
  item,
  reviewer,
  onChanged,
  t,
}: {
  item: DecidedItem
  reviewer: string
  onChanged: () => void
  t: Translate
}) {
  const [history, setHistory] = useState<ApprovalDecision[] | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [revisiting, setRevisiting] = useState(false)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function toggleHistory() {
    if (!historyOpen && !history) setHistory(await api.decisions(item.id))
    setHistoryOpen(!historyOpen)
  }

  async function revisit(decision: string, editedRecommendation?: string) {
    setBusy(true)
    setError(null)
    try {
      await api.decide(item.id, {
        decision,
        editedRecommendation,
        reviewerNote: note.trim() || undefined,
        reviewedBy: reviewer,
      })
      setHistory(null)
      setHistoryOpen(false)
      setRevisiting(false)
      setNote('')
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <article className="card card-decided">
      <header>
        <div className="card-title">
          <h2>{item.noticeTitle ?? item.noticeId}</h2>
          <p className="muted">
            {item.buyerName ?? t('card.noBuyer')} · <code>{item.noticeId}</code>
          </p>
        </div>
        <div className="score" title={t('card.scoreHint')}>
          <strong>{item.deterministicScore}</strong>
          <span>/100</span>
        </div>
      </header>

      <div className="verdicts">
        <span>
          {t('decided.scoreSaid')} <Verdict value={item.scoreRecommendation} />
        </span>
        <span>
          {t('decided.modelSaid')} <Verdict value={item.modelRecommendation} />
        </span>
        <span>
          {t('decided.stoodBehind')} <Verdict value={item.effectiveRecommendation} />
        </span>
      </div>

      <div className="decision-strip">
        <span className={`decision decision-${item.decision.toLowerCase()}`}>{item.decision}</span>
        <span className="muted">
          {t('decided.by')} <strong>{item.reviewedBy || t('decided.unattributed')}</strong> · {when(item.decidedAt)}
        </span>
        {item.revisionCount > 1 && (
          <span className="flag" title={t('decided.revisionsHint')}>
            {t('decided.revisions', { n: item.revisionCount })}
          </span>
        )}
      </div>

      {item.reviewerNote ? (
        <blockquote className="note">{item.reviewerNote}</blockquote>
      ) : (
        <p className="muted">{t('decided.noNote')}</p>
      )}

      <div className="decided-actions">
        <button className="link" onClick={toggleHistory} aria-expanded={historyOpen}>
          {historyOpen ? t('decided.hideHistory') : t('decided.showHistory')}
        </button>
        <button className="link" onClick={() => setRevisiting(!revisiting)} aria-expanded={revisiting}>
          {revisiting ? t('decided.cancel') : t('decided.changeMind')}
        </button>
      </div>

      {historyOpen && history && (
        <table className="breakdown">
          <tbody>
            {history.map((decision) => (
              <tr key={decision.id}>
                <td>{decision.decision}</td>
                <td className="muted">{decision.editedRecommendation ?? '—'}</td>
                <td className="muted">
                  {decision.reviewerNote || <em>{t('decided.noNoteShort')}</em>} — {decision.reviewedBy || t('decided.unattributed')},{' '}
                  {decision.decidedAt ? when(decision.decidedAt) : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {revisiting && (
        <div className="revisit">
          {!reviewer && <p className="error">{t('reviewer.required')}</p>}
          <textarea
            placeholder={t('decided.revisitPlaceholder')}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
          />
          <div className="actions">
            <button disabled={busy || !reviewer} onClick={() => revisit('APPROVED')} className="approve">
              {t('card.approve')}
            </button>
            <button disabled={busy || !reviewer} onClick={() => revisit('REJECTED')} className="reject">
              {t('card.reject')}
            </button>
            <span className="edit-group">
              {t('card.editTo')}
              {['GO', 'INVESTIGATE', 'NO_GO'].map((value) => (
                <button key={value} disabled={busy || !reviewer} onClick={() => revisit('EDITED', value)}>
                  {LABELS[value]}
                </button>
              ))}
            </span>
          </div>
        </div>
      )}

      {error && <p className="error">{error}</p>}
    </article>
  )
}
