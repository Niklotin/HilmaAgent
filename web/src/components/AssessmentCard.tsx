import { useState } from 'react'
import { api, parseBreakdown, type QueueItem } from '../api/client'
import { useDraftNote } from '../drafts'

const LABELS: Record<string, string> = {
  GO: 'GO',
  NO_GO: 'NO-GO',
  INVESTIGATE: 'INVESTIGATE',
}

function Verdict({ value }: { value: string }) {
  return <span className={`verdict verdict-${value.toLowerCase()}`}>{LABELS[value] ?? value}</span>
}

/**
 * One proposal awaiting a decision.
 *
 * The score and the model's verdict are shown side by side rather than merged. Where they differ the
 * card says so plainly: the reviewer is being asked to break a tie, and hiding one side would be
 * deciding on their behalf.
 */
export function AssessmentCard({
  item,
  reviewer,
  onDecided,
}: {
  item: QueueItem
  reviewer: string
  onDecided: () => void
}) {
  const { note, setNote, clearNote } = useDraftNote(item.id)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [openCitation, setOpenCitation] = useState<string | null>(null)
  const [breakdownOpen, setBreakdownOpen] = useState(false)
  const [breakdown, setBreakdown] = useState<ReturnType<typeof parseBreakdown>>(null)

  async function decide(decision: string, editedRecommendation?: string) {
    setBusy(true)
    setError(null)
    try {
      await api.decide(item.id, {
        decision,
        editedRecommendation,
        reviewerNote: note.trim() || undefined,
        reviewedBy: reviewer,
      })
      clearNote()
      onDecided()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setBusy(false)
    }
  }

  async function toggleBreakdown() {
    if (!breakdownOpen && !breakdown) {
      const full = await api.assessment(item.id)
      setBreakdown(parseBreakdown((full as { scoreBreakdownJson?: string }).scoreBreakdownJson))
    }
    setBreakdownOpen(!breakdownOpen)
  }

  const deadline = item.submissionDeadline ? new Date(item.submissionDeadline) : null
  const daysLeft = deadline ? Math.ceil((deadline.getTime() - Date.now()) / 86_400_000) : null

  return (
    <article className={`card${item.recommendationDisagreement ? ' card-flagged' : ''}`}>
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
          Score says <Verdict value={item.scoreRecommendation} />
        </span>
        <span>
          Model says <Verdict value={item.modelRecommendation} />
        </span>
        {item.recommendationDisagreement && (
          <span className="flag" title="The model reached a different conclusion from the score. Neither was overruled.">
            ⚠ disagreement — you decide
          </span>
        )}
      </div>

      <dl className="facts">
        <div>
          <dt>Deadline</dt>
          <dd>
            {deadline ? deadline.toISOString().slice(0, 10) : '—'}
            {daysLeft !== null && daysLeft >= 0 && <span className="muted"> ({daysLeft}d)</span>}
          </dd>
        </div>
        <div>
          <dt>Value</dt>
          <dd>
            {item.estimatedValue != null
              ? `${item.estimatedValue.toLocaleString('fi-FI')} ${item.currency ?? ''}`.trim()
              : 'not stated'}
          </dd>
        </div>
        <div>
          <dt>Model</dt>
          <dd className="muted">{item.modelId ?? '—'}</dd>
        </div>
      </dl>

      <p className="reasoning">{item.reasoning}</p>

      {item.citations.length > 0 && (
        <div className="citations">
          <span className="muted">Cited passages:</span>
          {item.citations.map((citation, index) => (
            <button
              key={citation.chunkId}
              className="citation-chip"
              aria-expanded={openCitation === citation.chunkId}
              onClick={() => setOpenCitation(openCitation === citation.chunkId ? null : (citation.chunkId ?? null))}
            >
              [{index + 1}] {citation.section}
            </button>
          ))}
          {openCitation && (
            <blockquote>
              {item.citations.find((c) => c.chunkId === openCitation)?.quote ?? '(no excerpt stored)'}
            </blockquote>
          )}
        </div>
      )}

      <button className="link" onClick={toggleBreakdown} aria-expanded={breakdownOpen}>
        {breakdownOpen ? 'Hide' : 'Show'} score breakdown
      </button>

      {breakdownOpen && breakdown && (
        <table className="breakdown">
          <tbody>
            {breakdown.rules.map((rule) => (
              <tr key={rule.rule}>
                <td>{rule.rule}</td>
                <td className="num">
                  {rule.awarded}/{rule.max}
                </td>
                <td className="muted">{rule.detail}</td>
              </tr>
            ))}
            {breakdown.gates.map((gate) => (
              <tr key={gate.gate} className="gate">
                <td>⛔ {gate.gate}</td>
                <td className="num">blocks</td>
                <td className="muted">{gate.detail}</td>
              </tr>
            ))}
            {breakdown.warnings.map((warning) => (
              <tr key={warning} className="warning">
                <td colSpan={3} className="muted">
                  ⚠ {warning}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <textarea
        placeholder="Why? (optional — but this note is the most useful column when reading back what went wrong)"
        value={note}
        onChange={(e) => setNote(e.target.value)}
        rows={2}
      />

      {error && <p className="error">{error}</p>}

      {/* A decision with no author is not worth recording, so the buttons wait for a name rather
          than silently attributing the row to nobody. */}
      {!reviewer && <p className="muted">Set your name in the header to record a decision.</p>}

      <footer className="actions">
        <button disabled={busy || !reviewer} onClick={() => decide('APPROVED')} className="approve">
          Approve
        </button>
        <button disabled={busy || !reviewer} onClick={() => decide('REJECTED')} className="reject">
          Reject
        </button>
        <span className="edit-group">
          Edit to:
          {['GO', 'INVESTIGATE', 'NO_GO']
            .filter((value) => value !== item.modelRecommendation)
            .map((value) => (
              <button key={value} disabled={busy || !reviewer} onClick={() => decide('EDITED', value)}>
                {LABELS[value]}
              </button>
            ))}
        </span>
      </footer>
    </article>
  )
}
