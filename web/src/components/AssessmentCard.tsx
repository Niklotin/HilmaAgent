import { useState } from 'react'
import { api, parseBreakdown, type QueueItem } from '../api/client'
import { useDraftNote } from '../drafts'
import { useT } from '../i18n'
import { Reasoning } from './Reasoning'
import { ScoreDetail } from './ScoreDetail'

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
  const t = useT()
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

  function toggleCitation(chunkId: string) {
    setOpenCitation(openCitation === chunkId ? null : chunkId)
  }

  const deadline = item.submissionDeadline ? new Date(item.submissionDeadline) : null
  const daysLeft = deadline ? Math.ceil((deadline.getTime() - Date.now()) / 86_400_000) : null

  return (
    <article className={`card${item.recommendationDisagreement ? ' card-flagged' : ''}`}>
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
          {t('card.scoreSays')} <Verdict value={item.scoreRecommendation} />
        </span>
        <span>
          {t('card.modelSays')} <Verdict value={item.modelRecommendation} />
        </span>
        {item.recommendationDisagreement && (
          <span className="flag" title={t('card.disagreementHint')}>
            {t('card.disagreement')}
          </span>
        )}
      </div>

      <dl className="facts">
        <div>
          <dt>{t('card.deadline')}</dt>
          <dd>
            {deadline ? deadline.toISOString().slice(0, 10) : '—'}
            {daysLeft !== null && daysLeft >= 0 && <span className="muted"> ({daysLeft}d)</span>}
          </dd>
        </div>
        <div>
          <dt>{t('card.value')}</dt>
          <dd>
            {item.estimatedValue != null
              ? `${item.estimatedValue.toLocaleString('fi-FI')} ${item.currency ?? ''}`.trim()
              : t('card.valueMissing')}
          </dd>
        </div>
        <div>
          <dt>{t('card.model')}</dt>
          <dd className="muted">{item.modelId ?? '—'}</dd>
        </div>
      </dl>

      <Reasoning
        text={item.reasoning}
        citations={item.citations}
        openCitation={openCitation}
        onToggleCitation={toggleCitation}
      />

      {item.citations.length > 0 && (
        <div className="citations">
          <span className="muted">{t('card.citations')}</span>
          {item.citations.map((citation, index) => (
            <button
              key={citation.chunkId}
              className="citation-chip"
              aria-expanded={openCitation === citation.chunkId}
              onClick={() => toggleCitation(citation.chunkId!)}
            >
              [{index + 1}] {citation.section}
            </button>
          ))}
          {openCitation && (
            <blockquote>
              {item.citations.find((c) => c.chunkId === openCitation)?.quote ?? t('card.noExcerpt')}
            </blockquote>
          )}
        </div>
      )}

      <button className="link" onClick={toggleBreakdown} aria-expanded={breakdownOpen}>
        {breakdownOpen ? t('card.hideBreakdown') : t('card.showBreakdown')}
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
                <td className="muted">
                  <ScoreDetail detail={rule.detail} code={rule.detailCode} args={rule.detailArgs} />
                </td>
              </tr>
            ))}
            {breakdown.gates.map((gate) => (
              <tr key={gate.gate} className="gate">
                <td>⛔ {gate.gate}</td>
                <td className="num">{t('card.blocks')}</td>
                <td className="muted">
                  <ScoreDetail detail={gate.detail} code={gate.detailCode} args={gate.detailArgs} />
                </td>
              </tr>
            ))}
            {breakdown.warnings.map((warning) => (
              <tr key={warning.code ?? warning.text} className="warning">
                <td colSpan={3} className="muted">
                  ⚠ <ScoreDetail detail={warning.text} code={warning.code} args={warning.args} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <textarea
        placeholder={t('card.notePlaceholder')}
        value={note}
        onChange={(e) => setNote(e.target.value)}
        rows={2}
      />

      {error && <p className="error">{error}</p>}

      {/* A decision with no author is not worth recording, so the buttons wait for a name rather
          than silently attributing the row to nobody. */}
      {!reviewer && <p className="muted">{t('reviewer.required')}</p>}

      <footer className="actions">
        <button disabled={busy || !reviewer} onClick={() => decide('APPROVED')} className="approve">
          {t('card.approve')}
        </button>
        <button disabled={busy || !reviewer} onClick={() => decide('REJECTED')} className="reject">
          {t('card.reject')}
        </button>
        <span className="edit-group">
          {t('card.editTo')}
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
