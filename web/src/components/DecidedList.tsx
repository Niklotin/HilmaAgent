import { useEffect, useState } from 'react'
import { api, type ApprovalDecision, type DecidedItem } from '../api/client'

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

  useEffect(() => {
    api
      .decided()
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }, [])

  if (error) return <p className="error">{error}</p>
  if (!items) return <p className="muted">Loading…</p>
  if (items.length === 0)
    return <p className="muted">No decisions recorded yet. Rule on something in the queue and it appears here.</p>

  return (
    <main>
      <p className="muted">
        {items.length} decided. Decisions are append-only — revisiting one records a new verdict and
        keeps the old.
      </p>
      {items.map((item) => (
        <DecidedCard key={item.id} item={item} reviewer={reviewer} onChanged={onChanged} />
      ))}
    </main>
  )
}

function DecidedCard({
  item,
  reviewer,
  onChanged,
}: {
  item: DecidedItem
  reviewer: string
  onChanged: () => void
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
            {item.buyerName ?? 'Buyer not stated'} · <code>{item.noticeId}</code>
          </p>
        </div>
        <div className="score" title="Computed in code, never by the model">
          <strong>{item.deterministicScore}</strong>
          <span>/100</span>
        </div>
      </header>

      <div className="verdicts">
        <span>
          Score said <Verdict value={item.scoreRecommendation} />
        </span>
        <span>
          Model said <Verdict value={item.modelRecommendation} />
        </span>
        <span>
          Reviewer stood behind <Verdict value={item.effectiveRecommendation} />
        </span>
      </div>

      <div className="decision-strip">
        <span className={`decision decision-${item.decision.toLowerCase()}`}>{item.decision}</span>
        <span className="muted">
          by <strong>{item.reviewedBy || 'unattributed'}</strong> · {when(item.decidedAt)}
        </span>
        {item.revisionCount > 1 && (
          <span className="flag" title="This assessment has been decided more than once.">
            {item.revisionCount} decisions
          </span>
        )}
      </div>

      {item.reviewerNote ? (
        <blockquote className="note">{item.reviewerNote}</blockquote>
      ) : (
        <p className="muted">No note recorded.</p>
      )}

      <div className="decided-actions">
        <button className="link" onClick={toggleHistory} aria-expanded={historyOpen}>
          {historyOpen ? 'Hide' : 'Show'} decision history
        </button>
        <button className="link" onClick={() => setRevisiting(!revisiting)} aria-expanded={revisiting}>
          {revisiting ? 'Cancel' : 'Change my mind'}
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
                  {decision.reviewerNote || <em>no note</em>} — {decision.reviewedBy || 'unattributed'},{' '}
                  {decision.decidedAt ? when(decision.decidedAt) : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {revisiting && (
        <div className="revisit">
          {!reviewer && <p className="error">Set your name in the header before recording a decision.</p>}
          <textarea
            placeholder="Why the change of mind? This is what the history will show."
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
          />
          <div className="actions">
            <button disabled={busy || !reviewer} onClick={() => revisit('APPROVED')} className="approve">
              Approve
            </button>
            <button disabled={busy || !reviewer} onClick={() => revisit('REJECTED')} className="reject">
              Reject
            </button>
            <span className="edit-group">
              Edit to:
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
